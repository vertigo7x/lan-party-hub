using LANPartyHub.Server.Application.Ports;
using LANPartyHub.Server.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace LANPartyHub.Server.Infrastructure.Persistence;

/// <summary>
/// Adapter of the repository of the published packages of the event (AD-1, AD-7, AD-22).
///
/// <para>
/// The adapter writes in the tables <c>PACKAGE_DESCRIPTOR</c> and
/// <c>INSTALLATION_MANIFEST</c> of the SQLite file of the server. The descriptor carries no
/// signature and the manifest carries it, because the signature lives only in the signed
/// envelope of the manifest (AD-2, AD-7).
/// </para>
/// <para>
/// The canonical text of the manifest is persisted as it was signed, so the bytes that a
/// verifier recomputes are exactly the bytes that the signature covers (AD-2).
/// </para>
/// <para>
/// The withdrawal MARKS and never deletes (AC2, AD-26): the row of the package survives
/// and only its mark of withdrawal is written. The version of the catalog grows in the
/// SAME transaction as the write that causes it, because SQLite writes one statement at a
/// time and a version that grew apart from the write that motivated it would tell the
/// client that the catalog changed when it did not (AC3, AD-26).
/// </para>
/// </summary>
public sealed class PackageRepository : IPackageRepository
{
    private readonly CatalogDbContext _context;

    /// <summary>Creates the adapter of the repository of the published packages.</summary>
    /// <param name="context">Context of the database of the server.</param>
    public PackageRepository(CatalogDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
    }

    /// <inheritdoc />
    public async Task AddAsync(
        PackageDescriptorRow descriptor,
        SignedManifestRow manifest,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(manifest);

        // The version of the catalog grows in the SAME transaction as the publication
        // that causes it: a client that reads the version has to see the publication that
        // the version announces, and never a version that announces a publication that
        // did not happen (AC3, AD-26).
        await using var transaction = await _context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        _context.PackageDescriptors.Add(new PackageDescriptorEntity
        {
            PackageId = descriptor.PackageId,
            GameId = descriptor.GameId,
            Version = descriptor.Version,
            SizeBytes = descriptor.SizeBytes,
            ChunkSizeBytes = descriptor.ChunkSizeBytes,
            Sha256 = descriptor.Sha256,
            ManifestRef = descriptor.ManifestRef,
        });

        _context.InstallationManifests.Add(new InstallationManifestEntity
        {
            ManifestId = manifest.ManifestId,
            PackageId = manifest.PackageId,
            SchemaVersion = manifest.SchemaVersion,
            Signature = manifest.Signature,
            KeyId = manifest.KeyId,
            CanonicalManifest = manifest.CanonicalManifest,
        });

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await IncrementCatalogVersionAsync(cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<PackageDescriptorRow?> FindAsync(string packageId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        var entity = await _context.PackageDescriptors
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.PackageId == packageId, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return null;
        }

        return new PackageDescriptorRow(
            entity.PackageId,
            entity.GameId,
            entity.Version,
            entity.SizeBytes,
            entity.ChunkSizeBytes,
            entity.Sha256,
            entity.ManifestRef);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PackageDescriptorRow>> ListAsync(CancellationToken cancellationToken)
    {
        var entities = await _context.PackageDescriptors
            .AsNoTracking()
            .OrderBy(candidate => candidate.Version)
            .ThenBy(candidate => candidate.PackageId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return entities
            .Select(entity => new PackageDescriptorRow(
                entity.PackageId,
                entity.GameId,
                entity.Version,
                entity.SizeBytes,
                entity.ChunkSizeBytes,
                entity.Sha256,
                entity.ManifestRef))
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PublishedPackageRow>> ListPublishedAsync(CancellationToken cancellationToken)
    {
        // The listing joins the videogame, because the organizer has to see the name of
        // the game, its license and its restrictions of distribution together with the
        // package (AC1, FR-1, NFR-8). The join is done here, in the adapter, and not in
        // the use case: the use case never reaches the tables (AD-1).
        var entities = await _context.PackageDescriptors
            .AsNoTracking()
            .Include(package => package.Game)
            .OrderBy(package => package.Game!.Name)
            .ThenBy(package => package.Version)
            .ThenBy(package => package.PackageId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return entities
            .Select(entity => new PublishedPackageRow(
                entity.PackageId,
                entity.GameId,
                entity.Game.Name,
                entity.Version,
                entity.SizeBytes,
                entity.Game.License,
                entity.Game.DistributionRestrictions,
                entity.WithdrawnAt))
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<WithdrawalResult> WithdrawGameAsync(
        string gameId,
        string withdrawnAt,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        ArgumentException.ThrowIfNullOrWhiteSpace(withdrawnAt);

        // The marking and the growth of the version happen in the SAME transaction: the
        // version announces a change of the catalog, and the change has to be committed
        // with it (AC3, AD-26).
        await using var transaction = await _context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        // The withdrawal MARKS and never deletes: the rows of the packages survive and
        // only their mark is written, so the installation that a client already made is
        // not invalidated (AC2, AD-26).
        var marked = await _context.PackageDescriptors
            .Where(package => package.GameId == gameId && package.WithdrawnAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(package => package.WithdrawnAt, withdrawnAt),
                cancellationToken)
            .ConfigureAwait(false);

        var version = await IncrementCatalogVersionAsync(cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new WithdrawalResult(marked, version);
    }

    /// <inheritdoc />
    public async Task<long> GetCatalogVersionAsync(CancellationToken cancellationToken)
    {
        var entity = await _context.CatalogVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == CatalogVersionEntity.SingleRowKey, cancellationToken)
            .ConfigureAwait(false);

        return entity?.CatalogVersion ?? 0;
    }

    /// <summary>
    /// Grows the version of the catalog by one and returns the new value.
    ///
    /// <para>
    /// The single row of the table is created when it does not exist yet, so the first
    /// publication or the first withdrawal of a fresh catalog leaves the version at one
    /// instead of at zero (AC3).
    /// </para>
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The version of the catalog after the increment.</returns>
    private async Task<long> IncrementCatalogVersionAsync(CancellationToken cancellationToken)
    {
        var entity = await _context.CatalogVersions
            .FirstOrDefaultAsync(
                candidate => candidate.Id == CatalogVersionEntity.SingleRowKey,
                cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            entity = new CatalogVersionEntity
            {
                Id = CatalogVersionEntity.SingleRowKey,
                CatalogVersion = 0,
            };

            _context.CatalogVersions.Add(entity);
        }

        entity.CatalogVersion += 1;

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return entity.CatalogVersion;
    }
}
