using System.Net;
using System.Text.Json;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Client.Infrastructure.Networking;

/// <summary>
/// Adapter of the probe of the ancla de confianza del servidor (AD-27).
///
/// <para>
/// AD-27: el adaptador lee el ancla que el servidor publica en
/// <c>GET /api/v1/server/info</c> y NADA MÁS. Publicar el ancla NO la hace confiable: el
/// adaptador no decide si es la buena, solo devuelve lo que llegó, y el caso de uso de la
/// fijación lo compara con el fingerprint que el jugador recibió fuera de banda
/// (AD-27).
/// </para>
/// <para>
/// El sondeo tiene un TIEMPO LÍMITE, igual que el de las versiones: una sonda que no
/// acaba nunca deja al jugador esperando (AC3, UX-DR-36).
/// </para>
/// <para>
/// La ruta se declara aquí, y no en la capa de Application, para que el caso de uso no
/// conozca el transporte (AD-1).
/// </para>
/// </summary>
public sealed class HttpTrustAnchorProbe : IServerTrustAnchorProbe
{
    /// <summary>Route that publishes the versions and the ancla de confianza.</summary>
    public const string Route = "/api/v1/server/info";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly ILogger<HttpTrustAnchorProbe> _logger;

    /// <summary>Creates the adapter of the probe of the ancla de confianza.</summary>
    /// <param name="httpClient">
    /// Client of the server of the event, with its base address already set.
    /// </param>
    /// <param name="logger">Log of the adapter.</param>
    public HttpTrustAnchorProbe(HttpClient httpClient, ILogger<HttpTrustAnchorProbe> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClient = httpClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ServerTrustAnchorProbeResult> ProbeAsync(
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

            if (!response.IsSuccessStatusCode)
            {
                // El servidor no ha podido pronunciarse: eso NO es un rechazo del ancla, y
                // el jugador tiene que poder reintentar (AC3).
                _logger.LogWarning(
                    "El servidor {ServerAddress}:{ServerPort} respondió {StatusCode} al sondeo del ancla de confianza: no se puede comprobar.",
                    endpoint.Address,
                    endpoint.Port,
                    (int)response.StatusCode);

                return new ServerTrustAnchorProbeResult(Anchor: null, Reachable: true);
            }

            var payload = await response.Content
                .ReadAsStringAsync(timeoutSource.Token)
                .ConfigureAwait(false);

            var info = JsonSerializer.Deserialize<VersionInfo>(payload, SerializerOptions);

            if (info?.Anchor is null)
            {
                // El servidor no publicó ningún ancla: el cliente no puede emparejarse y
                // el emparejamiento se rechaza sin escribir nada (AC2, AD-27).
                _logger.LogWarning(
                    "El servidor {ServerAddress}:{ServerPort} no publicó ninguna ancla de confianza.",
                    endpoint.Address,
                    endpoint.Port);

                return new ServerTrustAnchorProbeResult(Anchor: null, Reachable: true);
            }

            return new ServerTrustAnchorProbeResult(info.Anchor, Reachable: true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // La cancelación la pidió el jugador: el token viaja intacto (AC3).
            throw;
        }
        catch (OperationCanceledException)
        {
            // La sonda se agotó: el servidor no respondió a tiempo y eso NO es un rechazo
            // del ancla (AC3, UX-DR-36).
            _logger.LogWarning(
                "El sondeo del ancla de confianza del servidor {ServerAddress}:{ServerPort} se agotó.",
                endpoint.Address,
                endpoint.Port);

            return new ServerTrustAnchorProbeResult(Anchor: null, Reachable: false);
        }
        catch (HttpRequestException exception)
        {
            // El destino puede no responder: el rechazo NO se puede afirmar y el cliente
            // sigue siendo un cliente que se puede reintentar (AC3).
            _logger.LogWarning(
                exception,
                "No se pudo sondear el ancla de confianza del servidor {ServerAddress}:{ServerPort}.",
                endpoint.Address,
                endpoint.Port);

            return new ServerTrustAnchorProbeResult(Anchor: null, Reachable: false);
        }
        catch (JsonException exception)
        {
            // El servidor respondió con un cuerpo que no es el contrato: no se puede
            // pronunciarse sobre el ancla (AD-27).
            _logger.LogWarning(
                exception,
                "El servidor {ServerAddress}:{ServerPort} publicó un cuerpo que no se pudo leer como información del servidor.",
                endpoint.Address,
                endpoint.Port);

            return new ServerTrustAnchorProbeResult(Anchor: null, Reachable: true);
        }
    }

    /// <summary>
    /// Builds the URI of the route of the information del servidor.
    /// </summary>
    /// <param name="endpoint">Endpoint of the server of the event.</param>
    /// <returns>The URI of the route.</returns>
    private static Uri BuildUri(ServerEndpoint endpoint) =>
        new($"http://{endpoint.Address}:{endpoint.Port}{Route}", UriKind.Absolute);
}
