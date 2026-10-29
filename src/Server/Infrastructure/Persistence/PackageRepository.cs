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
}
