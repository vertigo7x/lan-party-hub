using LANPartyHub.Server.Application.Ports;
using LANPartyHub.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace LANPartyHub.Server.Infrastructure.Persistence;

/// <summary>
/// Adapter of the reading of the complete snapshot of the catalog (AD-1, AD-22, AD-26).
///
/// <para>
/// The adapter reads the three pieces of the snapshot from the SQLite file of the server:
/// the version of the catalog, the videogames that are published and the packages that are
/// NOT withdrawn. The withdrawal MARKS and never deletes (AD-26): a withdrawn package is
/// not part of the snapshot, but its row survives, so the installation that a client
/// already made is not invalidated (AC5).
/// </para>
/// <para>
/// The reading happens inside a transaction of the read, so the version and the payload
/// that travel together are the ones of the same instant: a client never receives a
/// version that announces a publication that is not in the payload (AD-26).
/// </para>
/// </summary>
public sealed class CatalogSnapshotRepository : ICatalogSnapshotRepository
{
    private readonly CatalogDbContext _context;

    /// <summary>Creates the adapter of the repository of the snapshot of the catalog.</summary>
    /// <param name="context">Context of the database of the server.</param>
    public CatalogSnapshotRepository(CatalogDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
    }

    /// <inheritdoc />
    public async Task<CatalogSnapshot> ReadSnapshotAsync(CancellationToken cancellationToken)
    {
        // La lectura de las tres piezas del snapshot ocurre dentro de una transacción de
        // lectura: la versión y el payload que viajan juntos son los del mismo instante
        // (AD-26).
        await using var transaction = await _context.Database
            .BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken)
            .ConfigureAwait(false);

        var version = await _context.CatalogVersions
            .AsNoTracking()
            .Where(candidate => candidate.Id == Entities.CatalogVersionEntity.SingleRowKey)
            .Select(candidate => candidate.CatalogVersion)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        // Un paquete retirado no es parte del snapshot: la marca es lo que lo saca, y la
        // fila que lo sostiene sobrevive en el archivo del servidor (AD-26, AC5).
        var packages = await _context.PackageDescriptors
            .AsNoTracking()
            .Where(package => package.WithdrawnAt == null)
            .OrderBy(package => package.PackageId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // La retirada se marca en el paquete, porque la tabla del videojuego no puede
        // guardar esa marca (AD-26, AD-34). Por eso un juego está publicado solo cuando
        // tiene algún paquete no retirado: el juego retirado sale del snapshot y sus
        // filas sobreviven intactas (AC5).
        var publishedGameIds = packages
            .Select(package => package.GameId)
            .ToHashSet(StringComparer.Ordinal);

        var games = await _context.Games
            .AsNoTracking()
            .Where(candidate => publishedGameIds.Contains(candidate.GameId))
            .OrderBy(candidate => candidate.Name)
            .ThenBy(candidate => candidate.GameId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new CatalogSnapshot(
            version,
            [.. games.Select(ToDescriptor)],
            [.. packages.Select(package => new PackageDescriptor(
                package.PackageId,
                package.GameId,
                package.Version,
                package.SizeBytes,
                package.ChunkSizeBytes ?? 0L,
                package.Sha256,
                package.ManifestRef))]);
    }

    /// <summary>
    /// Projects the row of the table <c>GAME</c> onto the contract of <c>Shared</c>, with
    /// the same name and in the same order of the fields (AD-34).
    /// </summary>
    /// <param name="entity">Row of the videogame.</param>
    /// <returns>The contract of the videogame.</returns>
    private static GameDescriptor ToDescriptor(Entities.GameEntity entity) => new(
        entity.GameId,
        entity.Name,
        entity.Description,
        entity.Categories,
        entity.Tags,
        entity.CoverImage,
        entity.Images,
        entity.Version,
        entity.SizeBytes,
        entity.MinPlayers,
        entity.MaxPlayers,
        entity.OperatingSystem,
        entity.DistributionType,
        entity.InstallationType,
        entity.LaunchConfiguration,
        entity.Dependencies,
        entity.License,
        entity.DistributionRestrictions);
}
