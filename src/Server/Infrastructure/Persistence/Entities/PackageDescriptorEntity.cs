namespace LANPartyHub.Server.Infrastructure.Persistence.Entities;

/// <summary>
/// Row of the <c>PACKAGE_DESCRIPTOR</c> table (AD-22, AD-7).
///
/// The descriptor carries no signature: the signature lives in the signed envelope of the
/// installation manifest (AD-2). The chunk size is informative and optional in the HTTP path
/// of the MVP (AD-24).
/// </summary>
public sealed class PackageDescriptorEntity
{
    /// <summary>Opaque UUID v7 identifier of the package.</summary>
    public string PackageId { get; set; } = string.Empty;

    /// <summary>Opaque UUID v7 identifier of the game the package belongs to.</summary>
    public string GameId { get; set; } = string.Empty;

    /// <summary>Opaque version string of the publisher.</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>Exact size of the package in bytes.</summary>
    public long SizeBytes { get; set; }

    /// <summary>
    /// Informative chunk size; zero or absent in the HTTP path of the MVP (AD-24). The ERD
    /// declares it optional, so the column accepts a null value.
    /// </summary>
    public long? ChunkSizeBytes { get; set; }

    /// <summary>Hexadecimal sha256 of the package bytes.</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>Reference to the signed installation manifest.</summary>
    public string ManifestRef { get; set; } = string.Empty;

    /// <summary>
    /// Instant of the withdrawal of the package, in UTC ISO-8601, or <c>null</c> when the
    /// package is still in the catalog.
    ///
    /// <para>
    /// The withdrawal MARKS and never deletes: the row of the package survives the
    /// withdrawal, so the installation that a client already made is not invalidated and
    /// the package can be published again with a new version (AC2, AD-26). The mark lives
    /// here and not in the table of the videogame, because the table of the videogame
    /// persists exactly the eighteen fields of the contract of <c>Shared</c> (AD-34).
    /// </para>
    /// </summary>
    public string? WithdrawnAt { get; set; }

    /// <summary>
    /// The videogame that owns the package. The navigation property is what turns the
    /// listing into a join with the table of the videogames, so the organizer sees the
    /// name, the license and the restrictions of distribution of the game together with
    /// its package (AC1, FR-1, NFR-8).
    /// </summary>
    public GameEntity Game { get; set; } = null!;
}
