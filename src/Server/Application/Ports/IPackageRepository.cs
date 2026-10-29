using LANPartyHub.Server.Domain.Packages;

namespace LANPartyHub.Server.Application.Ports;

/// <summary>
/// Port of the repository of the published packages of the event (AD-1, AD-7, AD-22).
///
/// <para>
/// The port is declared in the Application layer and it is implemented by the adapter that
/// writes in the SQLite file of the server. The mutation of what is persisted happens only
/// there, through this port, and the use cases never reach the adapter (AD-1).
/// </para>
/// <para>
/// The descriptor of the package and the installation manifest are owned by the server and
/// they live in <c>catalog.sqlite</c>. No module of the server reads the table of another
/// module (AD-22).
/// </para>
/// </summary>
public interface IPackageRepository
{
    /// <summary>
    /// Persists a published package: its descriptor and the installation manifest with its
    /// signed envelope.
    /// </summary>
    /// <param name="descriptor">Descriptor of the published package.</param>
    /// <param name="manifest">Installation manifest with its signed envelope.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the package has been persisted.</returns>
    Task AddAsync(
        PackageDescriptorRow descriptor,
        SignedManifestRow manifest,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads a published package by its identifier. The reading is what tells the use case
    /// that the package is already published, and a published package is immutable (AD-7).
    /// </summary>
    /// <param name="packageId">Identifier of the package.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>
    /// The descriptor of the package, or <c>null</c> when the server does not hold a package
    /// with that identifier.
    /// </returns>
    Task<PackageDescriptorRow?> FindAsync(string packageId, CancellationToken cancellationToken);

    /// <summary>
    /// Reads every published package of the event. The reading is what fills the list of
    /// the section of packages of the organizer, and it is not a route of the API: the
    /// routes that publish the catalog belong to the stories that follow (AD-1, AD-5).
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The descriptors of the published packages.</returns>
    Task<IReadOnlyList<PackageDescriptorRow>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reads the published packages of the event enriched with the metadata of the
    /// videogame that owns them (AC1, FR-1).
    ///
    /// <para>
    /// The enriched row is an internal type of the server and it is NOT a contract of
    /// <c>Shared</c>: the twenty contracts of <c>Shared</c> are fixed and the server does
    /// not redeclare any of them (AD-18). The row carries the name of the videogame, its
    /// version, the exact size in bytes, the license, the restrictions of distribution and
    /// the state of the withdrawal, so the organizer sees everything that the catalog
    /// demands before deciding (FR-1, NFR-8, AD-7).
    /// </para>
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The enriched rows of the published packages.</returns>
    Task<IReadOnlyList<PublishedPackageRow>> ListPublishedAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Marks the published packages of a videogame as withdrawn. The withdrawal MARKS and
    /// never deletes: no row of the packages, of the videogames or of the projection of the
    /// installations of the clients is removed (AC2, AD-26).
    /// </summary>
    /// <param name="gameId">Identifier of the videogame to withdraw.</param>
    /// <param name="withdrawnAt">Instant of the withdrawal, in UTC ISO-8601.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>
    /// The number of packages that the withdrawal marked, and the version of the catalog
    /// after the withdrawal.
    /// </returns>
    Task<WithdrawalResult> WithdrawGameAsync(
        string gameId,
        string withdrawnAt,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads the current version of the catalog (AC3, AD-26).
    ///
    /// <para>
    /// The version grows monotonically and it grows in the same transaction as the write
    /// that causes it, so the value that this reading returns is the value that the last
    /// publication or the last withdrawal committed (AC3).
    /// </para>
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The current version of the catalog.</returns>
    Task<long> GetCatalogVersionAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The row of a published package as the port of the repository transports it
/// (AD-7, AD-22).
///
/// <para>
/// The row carries no signature: the signature lives only in the signed envelope of the
/// installation manifest, and the identifier of the key lives only in that envelope too
/// (AD-2, AD-7).
/// </para>
/// </summary>
/// <param name="PackageId">Opaque UUID v7 identifier of the package.</param>
/// <param name="GameId">Opaque UUID v7 identifier of the videogame the package belongs to.</param>
/// <param name="Version">Opaque version string of the publisher.</param>
/// <param name="SizeBytes">Exact size of the package in bytes.</param>
/// <param name="ChunkSizeBytes">
/// Informative chunk size; zero or absent in the HTTP path of the MVP (AD-24).
/// </param>
/// <param name="Sha256">Hexadecimal sha256 of the package bytes.</param>
/// <param name="ManifestRef">Reference to the signed installation manifest.</param>
public sealed record PackageDescriptorRow(
    string PackageId,
    string GameId,
    string Version,
    long SizeBytes,
    long? ChunkSizeBytes,
    string Sha256,
    string ManifestRef);

/// <summary>
/// The row of an installation manifest as the port of the repository transports it
/// (AD-2, AD-22).
///
/// <para>
/// The row carries the canonical text of the manifest and the signature that covers it,
/// because the signature is exactly over that canonical form (AD-2).
/// </para>
/// </summary>
/// <param name="ManifestId">Opaque identifier of the manifest.</param>
/// <param name="PackageId">Opaque UUID v7 identifier of the package the manifest belongs to.</param>
/// <param name="SchemaVersion">Version of the schema of the manifest.</param>
/// <param name="CanonicalManifest">Canonical JSON text of the manifest, as it was signed.</param>
/// <param name="Signature">Ed25519 signature over the canonical text, in lowercase hexadecimal.</param>
/// <param name="KeyId">Identifier of the key that produced the signature.</param>
public sealed record SignedManifestRow(
    string ManifestId,
    string PackageId,
    int SchemaVersion,
    string CanonicalManifest,
    string Signature,
    string KeyId);

/// <summary>
/// A published package of the event, enriched with the metadata of the videogame that
/// owns it (AC1, FR-1, NFR-8).
///
/// <para>
/// The row is an internal type of the server: it is NOT a contract of <c>Shared</c> and it
/// does not redeclare any of the twenty contracts that <c>Shared</c> fixes (AD-18). It
/// exists because the organizer has to see the game, the version, the exact size, the
/// license and the restrictions of distribution before deciding what to do with the
/// package (AC1).
/// </para>
/// <para>
/// The exact size travels in bytes and it is not derived here: the conversion to decimal
/// gigabytes is a rule of presentation and it belongs to the interface, not to the model
/// (AD-7).
/// </para>
/// </summary>
/// <param name="PackageId">Opaque UUID v7 identifier of the package.</param>
/// <param name="GameId">Opaque UUID v7 identifier of the videogame the package belongs to.</param>
/// <param name="GameName">Human readable name of the videogame.</param>
/// <param name="Version">Opaque version string of the publisher.</param>
/// <param name="SizeBytes">Exact size of the package in bytes.</param>
/// <param name="License">License of the videogame.</param>
/// <param name="DistributionRestrictions">Restrictions of distribution of the videogame.</param>
/// <param name="WithdrawnAt">
/// Instant of the withdrawal of the package, in UTC ISO-8601, or <c>null</c> when the
/// package is still in the catalog.
/// </param>
public sealed record PublishedPackageRow(
    string PackageId,
    string GameId,
    string GameName,
    string Version,
    long SizeBytes,
    string License,
    string DistributionRestrictions,
    string? WithdrawnAt);

/// <summary>
/// The outcome of a withdrawal of a videogame (AC2, AD-26).
///
/// <para>
/// The withdrawal marks and never deletes, so the result carries how many packages it
/// marked and the version of the catalog after the withdrawal: the version grew in the
/// same transaction as the marking (AC3).
/// </para>
/// </summary>
/// <param name="WithdrawnPackages">Number of packages that the withdrawal marked.</param>
/// <param name="CatalogVersion">Version of the catalog after the withdrawal.</param>
public sealed record WithdrawalResult(int WithdrawnPackages, long CatalogVersion);
