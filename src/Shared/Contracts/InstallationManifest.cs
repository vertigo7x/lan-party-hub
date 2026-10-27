using System.Text.Json.Serialization;

namespace LANPartyHub.Shared.Contracts;

/// <summary>
/// Canonical installation manifest (AD-7, AD-18). It is the only signed object of the
/// system: the signature covers its canonical JSON form (AD-2).
/// The operations are typed and closed; there is no operation that runs arbitrary commands.
///
/// The canonical JSON that is signed is exactly what the serializer produces for this type:
/// the properties in declaration order, the nested objects in their declaration order and
/// the <c>operation</c> discriminator of the polymorphic operations. The canonicalizer of
/// Stories 2.2 and 6.1 has to reproduce that form byte for byte, because the signature is
/// verified against it.
/// </summary>
/// <param name="SchemaVersion">Version of the manifest schema; an unsupported value is rejected (AD-8).</param>
/// <param name="PackageId">Opaque UUID v7 identifier of the package this manifest belongs to.</param>
/// <param name="PackageVersion">Version of the package, as an opaque string.</param>
/// <param name="SizeBytes">Exact size of the package in bytes.</param>
/// <param name="Sha256">Hexadecimal sha256 of the package bytes.</param>
/// <param name="Launch">Launch configuration materialized by the manifest.</param>
/// <param name="Operations">Typed operations to apply, in order.</param>
public sealed record InstallationManifest(
    int SchemaVersion,
    string PackageId,
    string PackageVersion,
    long SizeBytes,
    string Sha256,
    InstallationManifest.LaunchSettings Launch,
    IReadOnlyList<InstallationManifest.ManifestOperation> Operations)
{
    /// <summary>
    /// Launch configuration of the installed game: executable path, arguments and working
    /// directory. It is declared once, here, because the manifest is the object that
    /// materializes it (AD-2, AD-7) and the game descriptor points to it (AD-34).
    /// </summary>
    /// <param name="ExecutablePath">Path of the executable, relative to the install root.</param>
    /// <param name="Arguments">Arguments passed to the executable.</param>
    /// <param name="WorkingDirectory">Working directory of the process.</param>
    public sealed record LaunchSettings(string ExecutablePath, IReadOnlyList<string> Arguments, string WorkingDirectory);

    /// <summary>
    /// Base type of the closed set of manifest operations (AD-7).
    /// </summary>
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "operation")]
    [JsonDerivedType(typeof(Extract), "extract")]
    [JsonDerivedType(typeof(CreateDirectory), "createDirectory")]
    [JsonDerivedType(typeof(CopyFile), "copyFile")]
    [JsonDerivedType(typeof(WriteConfig), "writeConfig")]
    [JsonDerivedType(typeof(SetLaunchConfig), "setLaunchConfig")]
    [JsonDerivedType(typeof(Verify), "verify")]
    public abstract record ManifestOperation;

    /// <summary>Extracts an archive into the install root.</summary>
    /// <param name="ArchivePath">Path of the archive, relative to the package.</param>
    /// <param name="DestinationPath">Destination path, relative to the install root.</param>
    public sealed record Extract(string ArchivePath, string DestinationPath) : ManifestOperation;

    /// <summary>Creates a directory inside the install root.</summary>
    /// <param name="Path">Relative path of the directory.</param>
    public sealed record CreateDirectory(string Path) : ManifestOperation;

    /// <summary>Copies a file into the install root.</summary>
    /// <param name="SourcePath">Path of the source file, relative to the package.</param>
    /// <param name="DestinationPath">Destination path, relative to the install root.</param>
    public sealed record CopyFile(string SourcePath, string DestinationPath) : ManifestOperation;

    /// <summary>Writes a configuration file inside the install root.</summary>
    /// <param name="Path">Relative path of the configuration file.</param>
    /// <param name="Content">Content of the configuration file.</param>
    public sealed record WriteConfig(string Path, string Content) : ManifestOperation;

    /// <summary>Sets the launch configuration of the installed game.</summary>
    /// <param name="Launch">Launch configuration to apply, with the same shape as the one of the manifest.</param>
    public sealed record SetLaunchConfig(InstallationManifest.LaunchSettings Launch) : ManifestOperation;

    /// <summary>Verifies the integrity of a file inside the install root.</summary>
    /// <param name="Path">Relative path of the file to verify.</param>
    public sealed record Verify(string Path) : ManifestOperation;

    /// <summary>
    /// Signed envelope of the installation manifest (AD-2). It is the only object that
    /// carries a signature, and the key identifier lives only here.
    /// </summary>
    /// <param name="Manifest">The canonical manifest that was signed.</param>
    /// <param name="Signature">Ed25519 signature over the canonical JSON of the manifest.</param>
    /// <param name="KeyId">Identifier of the key that produced the signature.</param>
    public sealed record SignedEnvelope(InstallationManifest Manifest, string Signature, string KeyId);
}
