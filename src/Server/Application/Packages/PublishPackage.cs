using System.Security.Cryptography;
using LANPartyHub.Server.Application.Ports;
using LANPartyHub.Server.Domain.Catalog;
using LANPartyHub.Server.Domain.Packages;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Server.Application.Packages;

/// <summary>
/// Use case of the publication of a package of the event (AD-1, AD-7, AD-8, AD-22).
///
/// <para>
/// The use case publishes a package of a videogame that is already in the catalog. It
/// validates that the videogame carries the license and the restrictions of distribution,
/// because the catalog forbids publishing a package whose content is not permitted
/// (FR-1, NFR-8). When one of the two is missing, the publication is blocked and the
/// rejection names the concrete field, so the organizer knows which one to fill in
/// (AD-25).
/// </para>
/// <para>
/// The use case reads the bytes of the package through a port, calculates the hash over
/// those bytes, builds the installation manifest with its typed and closed operations,
/// canonicalizes it, signs the canonical text through the port of the signature and
/// persists both the descriptor and the signed envelope of the manifest. The descriptor
/// carries no signature: the signature lives only in the envelope (AD-2, AD-7).
/// </para>
/// <para>
/// A published package is immutable: a publication that names a package that is already
/// published is rejected, and changing the content requires publishing a new version
/// (AD-7).
/// </para>
/// </summary>
public sealed class PublishPackage
{
    private readonly IGameRepository _gameRepository;
    private readonly IPackageRepository _packageRepository;
    private readonly IPackageContentStore _contentStore;
    private readonly IManifestSigner _signer;
    private readonly ILogger<PublishPackage> _logger;

    /// <summary>Creates the use case of the publication of a package.</summary>
    /// <param name="gameRepository">Port of the repository of the videogames.</param>
    /// <param name="packageRepository">Port of the repository of the published packages.</param>
    /// <param name="contentStore">Port of the reading of the bytes of the packages.</param>
    /// <param name="signer">Port of the signature of the installation manifest.</param>
    /// <param name="logger">Log of the use case.</param>
    public PublishPackage(
        IGameRepository gameRepository,
        IPackageRepository packageRepository,
        IPackageContentStore contentStore,
        IManifestSigner signer,
        ILogger<PublishPackage> logger)
    {
        ArgumentNullException.ThrowIfNull(gameRepository);
        ArgumentNullException.ThrowIfNull(packageRepository);
        ArgumentNullException.ThrowIfNull(contentStore);
        ArgumentNullException.ThrowIfNull(signer);
        ArgumentNullException.ThrowIfNull(logger);

        _gameRepository = gameRepository;
        _packageRepository = packageRepository;
        _contentStore = contentStore;
        _signer = signer;
        _logger = logger;
    }

    /// <summary>
    /// Publishes the package of a videogame of the catalog.
    /// </summary>
    /// <param name="gameId">Identifier of the videogame the package belongs to.</param>
    /// <param name="packageId">Identifier of the package to publish.</param>
    /// <param name="version">Opaque version string of the publisher.</param>
    /// <param name="sizeBytes">Exact size of the package in bytes.</param>
    /// <param name="chunkSizeBytes">
    /// Informative chunk size; zero or absent in the HTTP path of the MVP (AD-24).
    /// </param>
    /// <param name="launch">Launch configuration that the manifest materializes.</param>
    /// <param name="operations">Typed operations of the manifest, in order.</param>
    /// <param name="declaredSha256">
    /// Hash that the descriptor of the publication declares. The server is the authority
    /// of the hash: it computes it over the real bytes and rejects a declared value that
    /// disagrees with the one of the manifest (AD-2, AC7).
    /// </param>
    /// <param name="declaredIdentity">
    /// Identity that the installation manifest that the body carries declares. It is
    /// compared with the identity of the descriptor before anything is written, so the
    /// correspondence between the manifest and the package is signed and not assumed
    /// (AD-2, AC7).
    /// </param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The descriptor of the published package, without signature.</returns>
    /// <exception cref="PackageLicenseMissingException">
    /// The videogame does not carry the license that the catalog demands.
    /// </exception>
    /// <exception cref="PackageDistributionRestrictionsMissingException">
    /// The videogame does not carry the restrictions of distribution that the catalog demands.
    /// </exception>
    /// <exception cref="PackageNotFoundException">The videogame is not in the catalog.</exception>
    /// <exception cref="PackageAlreadyPublishedException">The package is already published.</exception>
    /// <exception cref="PackageContentMismatchException">
    /// The bytes of the package are absent, or their size does not match the declared one.
    /// </exception>
    /// <exception cref="PackageManifestMismatchException">
    /// The manifest declares an identity that is not the one of the descriptor.
    /// </exception>
    /// <exception cref="ManifestOperationNotAllowedException">
    /// The manifest declares an operation that is outside the closed set of AD-7.
    /// </exception>
    public async Task<PackageDescriptor> ExecuteAsync(
        string gameId,
        string packageId,
        string version,
        long sizeBytes,
        long? chunkSizeBytes,
        InstallationManifest.LaunchSettings launch,
        IReadOnlyList<InstallationManifest.ManifestOperation> operations,
        string declaredSha256,
        ManifestIdentity declaredIdentity,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentNullException.ThrowIfNull(launch);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(declaredIdentity);

        var game = await _gameRepository.FindAsync(gameId, cancellationToken).ConfigureAwait(false);

        if (game is null)
        {
            throw new PackageNotFoundException(gameId);
        }

        // The catalog demands the license and the restrictions of distribution before a
        // package can be published, and the rejection names the concrete field that is
        // missing (FR-1, NFR-8, AD-25).
        if (string.IsNullOrWhiteSpace(game.License))
        {
            throw new PackageLicenseMissingException(GameRegistration.FieldNames.License);
        }

        if (string.IsNullOrWhiteSpace(game.DistributionRestrictions))
        {
            throw new PackageDistributionRestrictionsMissingException(
                GameRegistration.FieldNames.DistributionRestrictions);
        }

        // The operations are typed and closed: an operation that is not one of the six of
        // AD-7 is rejected before anything is written (AD-7, AD-8).
        ManifestOperations.EnsureAllowed(operations);

        // The manifest declares its own version of the schema, and a manifest of a schema
        // that this server does not understand is rejected before anything is written
        // (AD-8). The version of the schema of the BODY is a different thing, and it has
        // already been checked by the endpoint (AD-5).
        if (declaredIdentity.SchemaVersion != InstallationManifestSchema.Version)
        {
            throw new SchemaVersionNotSupportedException(declaredIdentity.SchemaVersion);
        }

        // The manifest and the descriptor are two shapes of the same publication, so the
        // identity that the manifest declares has to be the one of the descriptor. A
        // mismatch means that the correspondence between the manifest and the package is
        // not signed, so the publication is rejected before anything is written (AC7).
        EnsureIdentityMatches(declaredIdentity, packageId, version, sizeBytes, declaredSha256);

        // A published package is immutable: changing its content requires publishing a new
        // version (AD-7).
        var published = await _packageRepository.FindAsync(packageId, cancellationToken).ConfigureAwait(false);

        if (published is not null)
        {
            throw new PackageAlreadyPublishedException(packageId);
        }

        var bytes = await _contentStore.ReadBytesAsync(packageId, cancellationToken).ConfigureAwait(false);

        if (bytes is null || bytes.LongLength != sizeBytes)
        {
            throw new PackageContentMismatchException(sizeBytes, bytes?.LongLength);
        }

        // The hash is calculated over the bytes that were just read, so the value that is
        // signed and the value that is persisted come from one single reading (AD-2, AD-7).
        var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        // El hash que el cuerpo declara no es la autoridad: la autoridad es el hash
        // que el servidor calcula sobre los bytes reales. Un hash declarado que NO está
        // vacío y que no coincide con el calculado se rechaza, porque un valor declarado
        // que se ignoraría en silencio dejaría que un cuerpo afirmara algo que el
        // manifiesto firmado no dice (AD-2, AC7).
        //
        // Un hash declarado VACÍO no es un desacuerdo: es la ausencia de una afirmación,
        // y el manifiesto que el servidor firma lleva SIEMPRE el hash calculado, porque
        // es él el que ata el manifiesto a los bytes que describe (AD-2, AD-7).
        if (!string.IsNullOrWhiteSpace(declaredIdentity.Sha256)
            && !string.Equals(declaredIdentity.Sha256, sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new PackageManifestMismatchException(
                PackageFieldNames.Sha256,
                sha256,
                declaredIdentity.Sha256);
        }

        var manifest = new InstallationManifest(
            InstallationManifestSchema.Version,
            packageId,
            version,
            sizeBytes,
            sha256,
            launch,
            operations);

        var canonical = ManifestCanonicalizer.Canonicalize(manifest);
        var signature = _signer.Sign(canonical);

        var manifestId = Guid.CreateVersion7().ToString("N");

        await _packageRepository
            .AddAsync(
                new PackageDescriptorRow(
                    packageId,
                    gameId,
                    version,
                    sizeBytes,
                    chunkSizeBytes,
                    sha256,
                    manifestId),
                new SignedManifestRow(
                    manifestId,
                    packageId,
                    InstallationManifestSchema.Version,
                    canonical,
                    signature,
                    _signer.GetKeyId()),
                cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Paquete publicado con el identificador {PackageId} del videogame {GameId} y la versión {Version}.",
            packageId,
            gameId,
            version);

        return new PackageDescriptor(
            packageId,
            gameId,
            version,
            sizeBytes,
            chunkSizeBytes ?? 0,
            sha256,
            manifestId);
    }

    /// <summary>
    /// Requires that the identity that the manifest declares is the identity of the
    /// descriptor of the package (AD-2, AC7).
    ///
    /// <para>
    /// The identifier, the version and the exact size are compared. The hash is not
    /// compared here, because the authority of the hash is the one that the server
    /// computes over the real bytes, and it is compared with the declared one once the
    /// bytes have been read.
    /// </para>
    /// </summary>
    /// <param name="declaredIdentity">Identity that the manifest declares.</param>
    /// <param name="packageId">Identifier that the descriptor declares.</param>
    /// <param name="version">Version that the descriptor declares.</param>
    /// <param name="sizeBytes">Exact size that the descriptor declares.</param>
    /// <exception cref="PackageManifestMismatchException">
    /// The manifest declares an identity that is not the one of the descriptor.
    /// </exception>
    private static void EnsureIdentityMatches(
        ManifestIdentity declaredIdentity,
        string packageId,
        string version,
        long sizeBytes,
        string declaredSha256)
    {
        if (!string.Equals(declaredIdentity.PackageId, packageId, StringComparison.Ordinal))
        {
            throw new PackageManifestMismatchException(
                PackageFieldNames.PackageId,
                packageId,
                declaredIdentity.PackageId);
        }

        if (!string.Equals(declaredIdentity.PackageVersion, version, StringComparison.Ordinal))
        {
            throw new PackageManifestMismatchException(
                "packageVersion",
                version,
                declaredIdentity.PackageVersion);
        }

        if (declaredIdentity.SizeBytes != sizeBytes)
        {
            throw new PackageManifestMismatchException(
                PackageFieldNames.SizeBytes,
                sizeBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
                declaredIdentity.SizeBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        // The descriptor of the body declares its own hash, and the manifest declares
        // another one. The two halves of the body describe the same package, so their
        // hashes have to agree: a body whose halves disagree does not describe a
        // package, and the publication is rejected (AC7).
        //
        // Un hash declarado VACÍO en una de las dos mitades no es un desacuerdo: es la
        // ausencia de una afirmación, y el hash que cuenta es el que el servidor calcula
        // sobre los bytes reales y firma en el manifiesto canónico (AD-2, AD-7).
        if (!string.IsNullOrWhiteSpace(declaredSha256)
            && !string.IsNullOrWhiteSpace(declaredIdentity.Sha256)
            && !string.Equals(declaredSha256, declaredIdentity.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new PackageManifestMismatchException(
                PackageFieldNames.Sha256,
                declaredIdentity.Sha256,
                declaredSha256);
        }
    }
}

/// <summary>
/// The version of the schema of the installation manifest that this server publishes
/// (AD-7, AD-8).
///
/// <para>
/// This is the single source of truth of the version of the schema of the manifest: the
/// use case that publishes it and the endpoint that receives it read it from here, so the
/// two cannot drift apart. It is NOT the version of the schema of the body of the
/// request, which is the version of the API and lives in the endpoint (AD-5).
/// </para>
/// </summary>
public static class InstallationManifestSchema
{
    /// <summary>
    /// Version of the schema of the installation manifest that this server supports. A
    /// manifest of another version is rejected before anything is written (AD-8).
    /// </summary>
    public const int Version = 1;
}

/// <summary>
/// The identity that the installation manifest declares (AD-2, AD-7).
///
/// <para>
/// The manifest y el descriptor son dos formas de la misma publicación, así que la
/// identidad que el manifiesto declara tiene que coincidir con la del descriptor. El
/// caso de uso la compara y rechaza la publicación cuando no coincide (AC7).
/// </para>
/// </summary>
/// <param name="PackageId">Identifier of the package that the manifest declares.</param>
/// <param name="PackageVersion">Version that the manifest declares.</param>
/// <param name="SizeBytes">Exact size that the manifest declares.</param>
/// <param name="SchemaVersion">
/// Version of the schema of the manifest that declares the identity. A manifest of a
/// schema that this server does not support is rejected before anything is written
/// (AD-8).
/// </param>
/// <param name="Sha256">
/// Hash that the manifest declares. It may be empty, and then the hash that the server
/// computes over the real bytes is the only one that counts. A declared hash that
/// disagrees with the computed one is rejected, because a declared value that is ignored
/// in silence would let a body claim something that the signed manifest does not.
/// </param>
public sealed record ManifestIdentity(
    string PackageId,
    string PackageVersion,
    long SizeBytes,
    int SchemaVersion,
    string Sha256);
