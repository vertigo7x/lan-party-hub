using LANPartyHub.Server.Domain.Packages;
using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Server.Host;

/// <summary>
/// Body of the administration of the publication of the packages (AD-5, AD-7).
///
/// <para>
/// The body is a <em>sobre</em> around the contracts of <c>Shared</c>: it carries the
/// <c>schemaVersion</c> that every mutation of the API carries (AD-5), the identifier of
/// the videogame and the data of the package. The metadata of the package travel in the
/// <see cref="PackageDescriptor"/> that the body brings and the operations of the manifest
/// travel in the <see cref="InstallationManifest"/>, so this type does not repeat a single
/// name of a field and cannot diverge from the contracts (AD-7, AD-34).
/// </para>
/// <para>
/// The body is a type of the server and not a contract of <c>Shared</c>: AD-18 fixes
/// exhaustively the twenty contracts of <c>Shared</c> and the body of an administration
/// request is consumed only by the server, never by the client of the LAN.
/// </para>
/// </summary>
public sealed class PackageRequest
{
    /// <summary>
    /// Version of the schema of the body. A body that does not carry it, or that carries a
    /// value that the server does not support, is rejected before anything is written
    /// (AD-5).
    /// </summary>
    public int? SchemaVersion { get; set; }

    /// <summary>
    /// Identifier of the videogame whose package is published. The videogame has to be
    /// already in the catalog, with its license and its restrictions of distribution
    /// (AD-7, FR-1).
    /// </summary>
    public string? GameId { get; set; }

    /// <summary>
    /// The data of the package that is published: its identifier, its version, its exact
    /// size and the reference to its manifest (AD-7).
    /// </summary>
    public PackageDescriptor? Package { get; set; }

    /// <summary>
    /// The installation manifest of the package, with its typed and closed operations
    /// (AD-7, AD-8).
    /// </summary>
    public InstallationManifest? Manifest { get; set; }

    /// <summary>
    /// Projects the body of the request onto the data that the use case receives. The
    /// identifier of the package is the one that the descriptor carries, and the identifier
    /// of the videogame is the one of the body (AD-7).
    /// </summary>
    /// <returns>The data of the publication.</returns>
    /// <exception cref="PackagePayloadMissingException">
    /// The body does not carry the data of the package or the manifest.
    /// </exception>
    public PackagePublicationData ToPublicationData()
    {
        if (Package is null || Manifest is null)
        {
            throw new PackagePayloadMissingException();
        }

        return new PackagePublicationData(
            GameId ?? string.Empty,
            Package.PackageId,
            Package.Version,
            Package.SizeBytes,
            Package.ChunkSizeBytes,
            Manifest.Launch,
            Manifest.Operations);
    }
}

/// <summary>
/// The data of a publication, as the body of the request projects it (AD-7).
///
/// </summary>
/// <param name="GameId">Identifier of the videogame the package belongs to.</param>
/// <param name="PackageId">Identifier of the package that is published.</param>
/// <param name="Version">Opaque version string of the publisher.</param>
/// <param name="SizeBytes">Exact size of the package in bytes.</param>
/// <param name="ChunkSizeBytes">Informative chunk size; optional in the HTTP path of the MVP.</param>
/// <param name="Launch">Launch configuration that the manifest materializes.</param>
/// <param name="Operations">Typed operations of the manifest, in order.</param>
public sealed record PackagePublicationData(
    string GameId,
    string PackageId,
    string Version,
    long SizeBytes,
    long? ChunkSizeBytes,
    InstallationManifest.LaunchSettings Launch,
    IReadOnlyList<InstallationManifest.ManifestOperation> Operations);
