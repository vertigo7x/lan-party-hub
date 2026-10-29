using LANPartyHub.Server.Application.Catalog;
using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Server.Host;

/// <summary>
/// Body of the administration of the videogames of the catalog (AD-5, AD-34).
///
/// <para>
/// The body carries the <c>schemaVersion</c> that every mutation of the API carries and the
/// eighteen metadata of FR-1. It is a type of the server and not a contract of
/// <c>Shared</c>: the contract of <c>Shared</c> that carries the eighteen fields is the
/// description of the videogame, and it is the shape of the response, while this body is
/// consumed only by the administration surface of the server (AD-34).
/// </para>
/// <para>
/// The body of a modification carries additionally the identifier of the videogame, and it
/// is that identifier the one that stays when the videogame is modified (AD-34).
/// </para>
/// </summary>
public sealed class GameRequest
{
    /// <summary>
    /// Version of the schema of the body. A body that does not carry it, or that carries a
    /// value that the server does not support, is rejected before anything is written
    /// (AD-5).
    /// </summary>
    public int? SchemaVersion { get; set; }

    /// <summary>
    /// Identifier of the videogame to modify. It is absent in the registration, and the
    /// server generates it then (AD-23).
    /// </summary>
    public string? GameId { get; set; }

    /// <summary>Human readable name.</summary>
    public string? Name { get; set; }

    /// <summary>Description of the videogame.</summary>
    public string? Description { get; set; }

    /// <summary>Categories used to filter the catalog.</summary>
    public IReadOnlyList<string>? Categories { get; set; }

    /// <summary>Free form tags of the videogame.</summary>
    public IReadOnlyList<string>? Tags { get; set; }

    /// <summary>Reference of the cover image.</summary>
    public string? CoverImage { get; set; }

    /// <summary>References of the additional images.</summary>
    public IReadOnlyList<string>? Images { get; set; }

    /// <summary>Version published in the catalog, as an opaque string.</summary>
    public string? Version { get; set; }

    /// <summary>Exact size of the package in bytes.</summary>
    public long SizeBytes { get; set; }

    /// <summary>Minimum number of players.</summary>
    public int MinPlayers { get; set; }

    /// <summary>Maximum number of players.</summary>
    public int MaxPlayers { get; set; }

    /// <summary>Operating system the videogame runs on.</summary>
    public string? OperatingSystem { get; set; }

    /// <summary>Distribution type of the videogame.</summary>
    public string? DistributionType { get; set; }

    /// <summary>Installation type of the videogame.</summary>
    public string? InstallationType { get; set; }

    /// <summary>
    /// Launch configuration of the videogame: path of the executable, arguments and working
    /// directory. It is ONE field of the contract of AD-34, with the same nested shape that
    /// the response publishes.
    /// </summary>
    public InstallationManifest.LaunchSettings? LaunchConfiguration { get; set; }

    /// <summary>Dependencies required by the videogame.</summary>
    public IReadOnlyList<string>? Dependencies { get; set; }

    /// <summary>License of the videogame.</summary>
    public string? License { get; set; }

    /// <summary>Restrictions on the publication of the videogame.</summary>
    public string? DistributionRestrictions { get; set; }

    /// <summary>
    /// Projects the body of the request onto the metadata that the use cases of the catalog
    /// receive.
    /// </summary>
    /// <returns>The metadata of the videogame.</returns>
    public GameMetadata ToMetadata() => new(
        Name ?? string.Empty,
        Description ?? string.Empty,
        Categories ?? [],
        Tags ?? [],
        CoverImage ?? string.Empty,
        Images ?? [],
        Version ?? string.Empty,
        SizeBytes,
        MinPlayers,
        MaxPlayers,
        OperatingSystem ?? string.Empty,
        DistributionType ?? string.Empty,
        InstallationType ?? string.Empty,
        LaunchConfiguration ?? new InstallationManifest.LaunchSettings(
            string.Empty,
            [],
            string.Empty),
        Dependencies ?? [],
        License ?? string.Empty,
        DistributionRestrictions ?? string.Empty);
}
