using LANPartyHub.Server.Application.Ports;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Server.Application.Catalog;

/// <summary>
/// Use case of the reading of the complete snapshot of the catalog (AC1, AC2, AD-26).
///
/// <para>
/// The use case reads the snapshot through the port of the repository and never reaches the
/// adapter, so the reading can be exercised without a database (AD-1). The snapshot is
/// complete: it carries the version of the catalog, the videogames that are published and
/// the packages that are not withdrawn, and there is no delta in the MVP (AD-26).
/// </para>
/// <para>
/// The use case is the single reader of the snapshot, so the route that publishes it and
/// the route that the client calls answer with the same shape (AD-5, AD-18).
/// </para>
/// </summary>
public sealed class GetCatalogSnapshot
{
    private readonly ICatalogSnapshotRepository _repository;
    private readonly ILogger<GetCatalogSnapshot> _logger;

    /// <summary>Creates the use case of the reading of the snapshot.</summary>
    /// <param name="repository">Port of the repository of the snapshot of the catalog.</param>
    /// <param name="logger">Log of the use case.</param>
    public GetCatalogSnapshot(
        ICatalogSnapshotRepository repository,
        ILogger<GetCatalogSnapshot> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Reads the complete snapshot of the catalog of the event.
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The complete snapshot of the catalog.</returns>
    public async Task<CatalogSnapshot> ExecuteAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _repository.ReadSnapshotAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "El snapshot del catálogo tiene la versión {CatalogVersion}, {Games} videojuegos y {Packages} paquetes publicados.",
            snapshot.CatalogVersion,
            snapshot.Games.Count,
            snapshot.Packages.Count);

        return snapshot;
    }
}
