namespace LANPartyHub.Server.Infrastructure.Persistence.Entities;

/// <summary>
/// Row of the <c>INSTALLATION_MANIFEST</c> table (AD-22, AD-2).
///
/// The server owns the canonical manifest and its signature (AD-22): the canonical manifest
/// is stored as JSON text, next to the signature and the identifier of the key that produced
/// it, because the signature covers exactly that canonical form.
/// </summary>
public sealed class InstallationManifestEntity
{
    /// <summary>Opaque identifier of the manifest.</summary>
    public string ManifestId { get; set; } = string.Empty;

    /// <summary>Opaque UUID v7 identifier of the package the manifest belongs to.</summary>
    public string PackageId { get; set; } = string.Empty;

    /// <summary>Version of the manifest schema.</summary>
    public int SchemaVersion { get; set; }

    /// <summary>Ed25519 signature over the canonical JSON of the manifest.</summary>
    public string Signature { get; set; } = string.Empty;

    /// <summary>Identifier of the key that produced the signature.</summary>
    public string KeyId { get; set; } = string.Empty;

    /// <summary>The canonical manifest, as JSON text.</summary>
    public string CanonicalManifest { get; set; } = string.Empty;
}
