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
}
