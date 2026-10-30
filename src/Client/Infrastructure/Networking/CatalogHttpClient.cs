using System.Net.Http.Json;
using System.Text.Json;
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

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;

    /// <summary>Creates the adapter of the reading of the snapshot of the catalog.</summary>
    /// <param name="httpClient">
    /// Client of the server of the event, with its base address already set.
    /// </param>
    public CatalogHttpClient(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task<CatalogSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient
            .GetAsync(Route, cancellationToken)
            .ConfigureAwait(false);

        // El servidor responde con el snapshot completo o con un error: una respuesta que
        // no es un éxito no es un catálogo, y el cliente conserva el que ya tiene (AC3).
        response.EnsureSuccessStatusCode();

        var snapshot = await response.Content
            .ReadFromJsonAsync<CatalogSnapshot>(SerializerOptions, cancellationToken)
            .ConfigureAwait(false);

        return snapshot ?? throw new InvalidOperationException(
            "El servidor no devolvió un snapshot del catálogo.");
    }
}
