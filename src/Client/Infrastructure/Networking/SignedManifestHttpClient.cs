using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Client.Infrastructure.Networking;

/// <summary>
/// Adapter of the read of the signed installation manifest of a package (AD-2, AD-8, AD-20).
///
/// <para>
/// El adaptador habla HTTP con el servidor del evento: lee la ruta
/// <c>GET /api/v1/packages/{packageId}/manifest</c> y devuelve el sobre firmado con el
/// texto canónico del manifiesto, la firma y el identificador de la clave (AD-2, AD-20).
/// </para>
/// <para>
/// La ruta es de la superficie del cliente, así que el servidor exige el identificador
/// del equipo y la versión del protocolo en EVERY petición: el adaptador usa el cliente
/// HTTP con el manejador de cabeceras instalado, y no estampa nada por su cuenta
/// (AD-6, AD-15, AD-23).
/// </para>
/// <para>
/// El texto canónico viaja como TEXTO y no como objeto: los bytes que se verifican son
/// exactamente los que la firma cubre, sin que este lado los recomponga (AD-2).
/// </para>
/// </summary>
public sealed class SignedManifestHttpClient : ISignedManifestClient
{
    /// <summary>Route that serves the signed manifest of a published package.</summary>
    public const string Route = "/api/v1/packages/{packageId}/manifest";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;

    /// <summary>Creates the adapter of the read of the signed manifest of a package.</summary>
    /// <param name="httpClient">
    /// Client of the server of the event, with its base address already set and the handler
    /// of the headers installed.
    /// </param>
    public SignedManifestHttpClient(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task<InstallationManifest.SignedEnvelope?> GetAsync(string packageId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            Route.Replace("{packageId}", Uri.EscapeDataString(packageId)));

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

        // Un paquete que el servidor no tiene no tiene manifiesto que servir: la respuesta
        // es un 404 y el caso de uso decide que el paquete NO es de fiar (AD-22).
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        // El servidor responde con el sobre firmado o con un error: una respuesta que no es
        // un éxito no es un manifiesto, y el cliente no inventa uno (AD-15).
        response.EnsureSuccessStatusCode();

        var payload = await response.Content
            .ReadFromJsonAsync<SignedManifestPayload>(SerializerOptions, cancellationToken)
            .ConfigureAwait(false);

        if (payload is null
            || string.IsNullOrWhiteSpace(payload.Manifest)
            || string.IsNullOrWhiteSpace(payload.Signature)
            || string.IsNullOrWhiteSpace(payload.KeyId))
        {
            throw new InvalidOperationException(
                "El servidor no devolvió el manifiesto firmado del paquete.");
        }

        return new InstallationManifest.SignedEnvelope(payload.Manifest, payload.Signature, payload.KeyId);
    }

    /// <summary>
    /// Answer of the service of the signed manifest of a package (AD-2, AD-5).
    /// </summary>
    /// <param name="SchemaVersion">Version of the schema of the answer that the server speaks.</param>
    /// <param name="Manifest">Canonical JSON text of the manifest, as it was signed.</param>
    /// <param name="Signature">Ed25519 signature over the canonical text, in lowercase hexadecimal.</param>
    /// <param name="KeyId">Identifier of the key that produced the signature.</param>
    private sealed record SignedManifestPayload(
        int SchemaVersion,
        string Manifest,
        string Signature,
        string KeyId);
}
