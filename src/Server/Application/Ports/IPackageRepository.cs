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
