using LANPartyHub.Server.Application.Packages;
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
    /// <exception cref="PackageManifestMismatchException">
    /// The body declares a reference to the manifest that the server has not minted.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The body carries the data, pero alguno de sus campos no tiene la forma que el
    /// contrato declara.
    /// </exception>
    public PackagePublicationData ToPublicationData()
    {
        if (Package is null || Manifest is null)
        {
            throw new PackagePayloadMissingException();
        }

        // Un cuerpo que lleva los bloques pero no la forma que el contrato declara es un
        // cuerpo que no se puede leer, y su rechazo es el del cuerpo mal formado, no el
        // de una ausencia de datos (AD-5, AD-15).
        if (string.IsNullOrWhiteSpace(GameId))
        {
            throw new ArgumentException("El cuerpo de la publicación no lleva el identificador del videogame.", nameof(GameId));
        }

        if (string.IsNullOrWhiteSpace(Package.PackageId))
        {
            throw new ArgumentException("El cuerpo de la publicación no lleva el identificador del paquete.", nameof(Package));
        }

        if (string.IsNullOrWhiteSpace(Package.Version))
        {
            throw new ArgumentException("El cuerpo de la publicación no lleva la versión del paquete.", nameof(Package));
        }

        // La referencia al manifiesto la ACUÑA el servidor: es el identificador del
        // manifiesto que él firma y persiste, y un cuerpo que la declara está
        // describiendo un manifiesto que no existe. La referencia declarada que no está
        // vacía es un desacuerdo entre el cuerpo y el servidor, y su rechazo es el del
        // desacuerdo, no el del cuerpo mal formado (AD-2, AD-7, AC2).
        if (!string.IsNullOrWhiteSpace(Package.ManifestRef))
        {
            throw new PackageManifestMismatchException(
                PackageFieldNames.ManifestRef,
                string.Empty,
                Package.ManifestRef);
        }

        if (Manifest.Launch is null)
        {
            throw new ArgumentException("El manifiesto de la publicación no lleva la configuración de lanzamiento.", nameof(Manifest));
        }

        if (Manifest.Operations is null)
        {
            throw new ArgumentException("El manifiesto de la publicación no lleva las operaciones.", nameof(Manifest));
        }

        return new PackagePublicationData(
            GameId,
            Package.PackageId,
            Package.Version,
            Package.SizeBytes,
            Package.ChunkSizeBytes,
            Package.Sha256,
            Manifest.Launch,
            Manifest.Operations,
            new ManifestIdentity(
                Manifest.PackageId,
                Manifest.PackageVersion,
                Manifest.SizeBytes,
                Manifest.SchemaVersion,
                Manifest.Sha256));
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
/// <param name="DeclaredSha256">
/// Hash that the descriptor of the body declares. It has to agree with the hash that
/// the manifest declares: the descriptor and the manifest are two halves of the same
/// publication, and a body whose halves disagree does not describe a package (AC7).
/// </param>
/// <param name="Launch">Launch configuration that the manifest materializes.</param>
/// <param name="Operations">Typed operations of the manifest, in order.</param>
/// <param name="ManifestIdentity">Identity that the manifest of the body declares.</param>
public sealed record PackagePublicationData(
    string GameId,
    string PackageId,
    string Version,
    long SizeBytes,
    long? ChunkSizeBytes,
    string DeclaredSha256,
    InstallationManifest.LaunchSettings Launch,
    IReadOnlyList<InstallationManifest.ManifestOperation> Operations,
    ManifestIdentity ManifestIdentity);
