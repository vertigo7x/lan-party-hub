using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LANPartyHub.Client.Application.CatalogSync;
using LANPartyHub.Client.Application.Ports;

namespace LANPartyHub.Client.Infrastructure.Networking;

/// <summary>
/// Adapter of the registration of the intention of a download (AD-5, AD-10, AC1).
///
/// <para>
/// El adaptador habla HTTP con el servidor del evento: lee la ruta
/// <c>POST /api/v1/downloads</c> y devuelve el identificador de la operación que el
/// servidor registró (AC1).
/// </para>
/// <para>
/// La ruta es de la superficie del cliente, así que el servidor exige el identificador
/// del equipo y la versión del protocolo en EVERY petición: el adaptador usa el cliente
/// HTTP con el manejador de cabeceras instalado, y no estampa nada por su cuenta
/// (AD-6, AD-15, AD-23).
/// </para>
/// <para>
/// AD-6, FR-4: un <c>426 Upgrade Required</c> significa que el servidor rechazó la
/// versión del cliente, así que el adaptador lanza la excepción TIPADA
/// <see cref="ClientVersionIncompatibleException"/> y no un error genérico de la red. El
/// diagnóstico que la acompaña sale del catálogo de errores y nombra la versión del
/// cliente, la del servidor y el siguiente paso (AC5, AD-15).
/// </para>
/// </summary>
public sealed class DownloadIntentHttpClient : IDownloadIntentClient
{
    /// <summary>Route that registers the intention of a download.</summary>
    public const string Route = "/api/v1/downloads";

    /// <summary>
    /// Header that carries the version of the binary of the server (AD-6).
    /// </summary>
    public const string ServerVersionHeaderName = "X-Server-Version";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly IClientVersionProvider _versions;

    /// <summary>Creates the adapter of the registration of the intention of a download.</summary>
    /// <param name="httpClient">
    /// Client of the server of the event, with its base address already set and the handler
    /// of the headers installed.
    /// </param>
    /// <param name="versions">
    /// Source of the versions that the client declares, so the diagnóstico de la
    /// incompatibilidad puede nombrar la versión del cliente (AD-6).
    /// </param>
    public DownloadIntentHttpClient(
        HttpClient httpClient,
        IClientVersionProvider? versions = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        _httpClient = httpClient;
        _versions = versions ?? new ClientBuildVersion();
    }

    /// <inheritdoc />
    public async Task<string> RegisterAsync(string packageId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        using var request = new HttpRequestMessage(HttpMethod.Post, Route)
        {
            Content = JsonContent.Create(
                new DownloadIntentBody(1, packageId),
                options: SerializerOptions),
        };

        using var response = await _httpClient
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        // AD-6, FR-4: el servidor rechazó la versión del cliente: el cliente NO descarga.
        // La incompatibilidad es su propia falla y no la caída del servidor, así que
        // lanza la excepción TIPADA y no un error genérico de la red (AC5).
        if (response.StatusCode == HttpStatusCode.UpgradeRequired)
        {
            throw new ClientVersionIncompatibleException(
                _versions.ClientVersion,
                ReadServerVersion(response));
        }

        // El servidor responde con la intención registrada o con un error: una respuesta
        // que no es un éxito no es una operación, y el cliente no inventa un
        // identificador (AC1, AD-15).
        response.EnsureSuccessStatusCode();

        var payload = await response.Content
            .ReadFromJsonAsync<DownloadIntentPayload>(SerializerOptions, cancellationToken)
            .ConfigureAwait(false);

        if (payload is null || string.IsNullOrWhiteSpace(payload.OperationId))
        {
            throw new InvalidOperationException(
                "El servidor no devolvió el identificador de la operación de descarga.");
        }

        return payload.OperationId;
    }

    /// <summary>
    /// Reads the version of the server from the header of the rejection, or <c>null</c>
    /// when the server did not publish it (AD-6).
    /// </summary>
    /// <param name="response">Response of the server.</param>
    /// <returns>The version of the server, or <c>null</c>.</returns>
    private static string? ReadServerVersion(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues(ServerVersionHeaderName, out var values))
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
    /// Body of the registration of the intention of a download (AD-5).
    /// </summary>
    /// <param name="SchemaVersion">Version of the schema of the body that the client speaks.</param>
    /// <param name="PackageId">Opaque identifier of the package that the client wants.</param>
    private sealed record DownloadIntentBody(int SchemaVersion, string PackageId);

    /// <summary>
    /// Answer of the registration of the intention of a download (AD-5).
    /// </summary>
    /// <param name="SchemaVersion">Version of the schema of the body that the server speaks.</param>
    /// <param name="OperationId">Opaque identifier of the registered operation.</param>
    /// <param name="PackageId">Opaque identifier of the package of the intention.</param>
    private sealed record DownloadIntentPayload(int SchemaVersion, string OperationId, string PackageId);
}
