using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LANPartyHub.Client.Application.CatalogSync;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Client.Infrastructure.Networking;

/// <summary>
/// Adapter of the reading of the complete snapshot of the catalog from the server
/// (AD-5, AD-26).
///
/// <para>
/// The adapter speaks HTTP with the event server: it reads the route
/// <c>GET /api/v1/catalog</c> and it deserializes the answer into the contract of
/// <c>Shared</c>. The catalog is synchronized by COMPLETE snapshot and never by delta, so
/// this route is the only one that the synchronization uses (AD-26).
/// </para>
/// <para>
/// The adapter does not decide whether the snapshot is applied: that rule lives in the
/// applier of the application layer, and the adapter only reads (AD-1).
/// </para>
/// <para>
/// The failure of the reading is a failure of the reading: the use case keeps the last
/// catalog that it applied and communicates the pending synchronization (AC3).
/// </para>
/// </summary>
public sealed class CatalogHttpClient : ICatalogSnapshotClient
{
    /// <summary>Route that publishes the complete snapshot of the catalog.</summary>
    public const string Route = "/api/v1/catalog";

    /// <summary>Header that carries the version of the binary of the client (AD-6).</summary>
    public const string ClientVersionHeaderName = "X-Client-Version";

    /// <summary>Header that carries the version of the binary of the server (AD-6).</summary>
    public const string ServerVersionHeaderName = "X-Server-Version";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly IClientVersionProvider _versions;

    /// <summary>Creates the adapter of the reading of the snapshot of the catalog.</summary>
    /// <param name="httpClient">
    /// Client of the server of the event, with its base address already set.
    /// </param>
    /// <param name="versions">Versions that the client declares in every request.</param>
    public CatalogHttpClient(HttpClient httpClient, IClientVersionProvider versions)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(versions);

        _httpClient = httpClient;
        _versions = versions;
    }

    /// <inheritdoc />
    public async Task<CatalogSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient
            .GetAsync(Route, cancellationToken)
            .ConfigureAwait(false);

        // AD-6, FR-4: el servidor rechazó la versión del cliente: el cliente NO aplica el
        // snapshot, NO descarga y NO instala. La incompatibilidad es su propia falla y no
        // la caída del servidor.
        if (response.StatusCode == HttpStatusCode.UpgradeRequired)
        {
            throw new ClientVersionIncompatibleException(
                _versions.ClientVersion,
                ReadServerVersion(response));
        }

        // El servidor responde con el snapshot completo o con un error: una respuesta que
        // no es un éxito no es un catálogo, y el cliente conserva el que ya tiene (AC3).
        response.EnsureSuccessStatusCode();

        var snapshot = await response.Content
            .ReadFromJsonAsync<CatalogSnapshot>(SerializerOptions, cancellationToken)
            .ConfigureAwait(false);

        return snapshot ?? throw new InvalidOperationException(
            "El servidor no devolvió un snapshot del catálogo.");
    }

    /// <summary>
    /// Reads the version of the server that published in its response, or <c>null</c> when
    /// the header no llega.
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
}
