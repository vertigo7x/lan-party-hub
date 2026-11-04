using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
/// </summary>
public sealed class DownloadIntentHttpClient : IDownloadIntentClient
{
    /// <summary>Route that registers the intention of a download.</summary>
    public const string Route = "/api/v1/downloads";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;

    /// <summary>Creates the adapter of the registration of the intention of a download.</summary>
    /// <param name="httpClient">
    /// Client of the server of the event, with its base address already set and the handler
    /// of the headers installed.
    /// </param>
    public DownloadIntentHttpClient(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        _httpClient = httpClient;
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
        // La incompatibilidad es su propia falla y no la caída del servidor.
        if (response.StatusCode == HttpStatusCode.UpgradeRequired)
        {
            throw new HttpRequestException(
                $"El servidor rechazó la versión del cliente con el estado {(int)response.StatusCode}.",
                null,
                response.StatusCode);
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
