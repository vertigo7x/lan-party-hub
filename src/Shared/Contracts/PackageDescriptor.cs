namespace LANPartyHub.Shared.Contracts;

/// <summary>
/// Immutable metadata of a published package (AD-7, AD-18). It carries no signature:
/// the signature lives in the signed envelope of the installation manifest (AD-2).
/// </summary>
/// <param name="PackageId">Opaque UUID v7 identifier of the package.</param>
/// <param name="GameId">Opaque UUID v7 identifier of the game the package belongs to.</param>
/// <param name="Version">Opaque version string of the publisher.</param>
/// <param name="SizeBytes">Exact size of the package in bytes.</param>
/// <param name="ChunkSizeBytes">Informative chunk size; zero in the HTTP path of the MVP (AD-24).</param>
/// <param name="Sha256">Hexadecimal sha256 of the package bytes.</param>
/// <param name="ManifestRef">Reference to the signed installation manifest.</param>
public sealed record PackageDescriptor(
    string PackageId,
    string GameId,
    string Version,
    long SizeBytes,
    long ChunkSizeBytes,
    string Sha256,
    string ManifestRef);
