using System.Globalization;
using System.Net;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Client.Infrastructure.Networking;

/// <summary>
/// Adapter of the probe of the versions of the event server (AD-6, FR-4).
///
/// <para>
/// El adaptador habla HTTP con el servidor y lee la ruta <c>GET /api/v1/server/info</c>.
/// El servidor es el que decide la compatibilidad: cuando las versiones no son
/// compatibles responde <c>426 Upgrade Required</c> con las cabeceras
/// <c>X-Server-Version</c> y <c>X-Minimum-Protocol-Version</c>, y el adaptador se limita
/// a leerlas (AD-6).
/// </para>
/// <para>
/// El sondeo tiene un TIEMPO LÍMITE: una sonda que no acaba nunca deja al jugador
/// esperando, así que la petición se corta al límite y el resultado es «no se pudo
/// comprobar», que NO es lo mismo que «es incompatible» (AC3, UX-DR-36).
/// </para>
/// <para>
/// La ruta y las cabeceras se declaran aquí, y no en la capa de Application, para que el
/// caso de uso de la conexión no conozca el transporte (AD-1).
/// </para>
/// </summary>
public sealed class HttpServerVersionProbe : IServerVersionProbe
{
    /// <summary>Route that publishes the versions of the server.</summary>
    public const string Route = "/api/v1/server/info";

    /// <summary>Header that carries the version of the binary of the server (AD-6).</summary>
    public const string ServerVersionHeaderName = "X-Server-Version";

    /// <summary>
    /// Header that carries the oldest protocol version that the server accepts (AD-6).
    /// </summary>
    public const string MinimumProtocolVersionHeaderName = "X-Minimum-Protocol-Version";

    /// <summary>
    /// Header that carries the name of the server of the event.
    ///
    /// <para>
    /// El nombre viaja como CABECERA, con el mismo patrón que la versión, y no como un
    /// campo nuevo del contrato <see cref="VersionInfo"/> de <c>Shared</c>: el contrato no
    /// crece (AD-18, AD-33).
    /// </para>
    /// </summary>
    public const string ServerNameHeaderName = "X-Server-Name";

    private readonly HttpClient _httpClient;
    private readonly ILogger<HttpServerVersionProbe> _logger;

    /// <summary>Creates the adapter of the probe of the versions of the server.</summary>
    /// <param name="httpClient">
    /// Client of the server of the event, with its base address already set.
    /// </param>
    /// <param name="logger">Log of the adapter.</param>
    public HttpServerVersionProbe(HttpClient httpClient, ILogger<HttpServerVersionProbe> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClient = httpClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ServerVersionProbeResult> ProbeAsync(
        ServerEndpoint endpoint,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(endpoint));
            using var response = await _httpClient
                .SendAsync(request, timeoutSource.Token)
                .ConfigureAwait(false);

            var serverVersion = ReadHeader(response, ServerVersionHeaderName);
            var minimumProtocolVersion = ReadMinimumProtocolVersion(response);
            var serverName = ReadHeader(response, ServerNameHeaderName);

            if (response.StatusCode == HttpStatusCode.UpgradeRequired)
            {
                _logger.LogWarning(
                    "El servidor {ServerAddress}:{ServerPort} rechazó la versión del cliente: el mínimo que acepta es {MinimumProtocolVersion}.",
                    endpoint.Address,
                    endpoint.Port,
                    minimumProtocolVersion);

                return new ServerVersionProbeResult(
                    Compatible: false,
                    serverVersion,
                    minimumProtocolVersion,
                    serverName,
                    Reachable: true);
            }

            if (!response.IsSuccessStatusCode)
            {
                // El servidor no ha podido pronunciarse: eso NO es una incompatibilidad,
                // y el jugador tiene que poder reintentar (AC3).
                _logger.LogWarning(
                    "El servidor {ServerAddress}:{ServerPort} respondió {StatusCode} al sondeo de las versiones: no se puede comprobar la compatibilidad.",
                    endpoint.Address,
                    endpoint.Port,
                    (int)response.StatusCode);

                return new ServerVersionProbeResult(
                    Compatible: true,
                    serverVersion,
                    minimumProtocolVersion,
                    serverName,
                    Reachable: true);
            }

            return new ServerVersionProbeResult(
                Compatible: true,
                serverVersion,
                minimumProtocolVersion,
                serverName,
                Reachable: true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // La cancelación la pidió el jugador: el token viaja intacto (AC3).
            throw;
        }
        catch (OperationCanceledException)
        {
            // La sonda se agotó: el servidor no respondió a tiempo y eso NO es una
            // incompatibilidad (AC3, UX-DR-36).
            _logger.LogWarning(
                "El sondeo de las versiones del servidor {ServerAddress}:{ServerPort} se agotó: no se puede comprobar la compatibilidad.",
                endpoint.Address,
                endpoint.Port);

            return new ServerVersionProbeResult(
                Compatible: true,
                ServerVersion: null,
                MinimumProtocolVersion: null,
                ServerName: null,
                Reachable: false);
        }
        catch (HttpRequestException exception)
        {
            // El destino puede no responder: la incompatibilidad NO se puede afirmar y el
            // cliente sigue siendo un cliente que se puede reintentar (AC3).
            _logger.LogWarning(
                exception,
                "No se pudo sondear el servidor {ServerAddress}:{ServerPort}: no se puede comprobar la compatibilidad.",
                endpoint.Address,
                endpoint.Port);

            return new ServerVersionProbeResult(
                Compatible: true,
                ServerVersion: null,
                MinimumProtocolVersion: null,
                ServerName: null,
                Reachable: false);
        }
    }

    /// <summary>
    /// Builds the URI of the route of the versions del servidor.
    /// </summary>
    /// <param name="endpoint">Endpoint of the server of the event.</param>
    /// <returns>The URI of the route.</returns>
    private static Uri BuildUri(ServerEndpoint endpoint) =>
        new($"http://{endpoint.Address}:{endpoint.Port}{Route}", UriKind.Absolute);

    /// <summary>
    /// Reads the single value of a header of the response. A header that no llega, que
    /// llega vacía o que llega repetida se lee como ausente.
    /// </summary>
    /// <param name="response">Response of the server.</param>
    /// <param name="name">Name of the header.</param>
    /// <returns>The value of the header, or <c>null</c>.</returns>
    private static string? ReadHeader(HttpResponseMessage response, string name)
    {
        if (!response.Headers.TryGetValues(name, out var values))
        {
            return null;
        }

        var list = values.ToArray();

        if (list.Length != 1)
        {
            return null;
        }

        var value = list[0]?.Trim();

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>
    /// Reads the oldest protocol version that the server accepts, or <c>null</c> when the
    /// header no llega o no lleva un entero válido.
    /// </summary>
    /// <param name="response">Response of the server.</param>
    /// <returns>The minimum protocol version, or <c>null</c>.</returns>
    private static int? ReadMinimumProtocolVersion(HttpResponseMessage response)
    {
        var value = ReadHeader(response, MinimumProtocolVersionHeaderName);

        if (value is null)
        {
            return null;
        }

        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var minimum)
            ? minimum
            : null;
    }
}
