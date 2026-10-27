namespace LANPartyHub.Shared.Contracts;

/// <summary>
/// Metadata of a videogame (AD-34, AD-18). The field list is exact and the order is fixed:
/// there are no optional fields and no conditional fields.
/// </summary>
/// <param name="GameId">Opaque UUID v7 identifier of the game.</param>
/// <param name="Name">Human readable name.</param>
/// <param name="Description">Description of the game.</param>
/// <param name="Categories">Categories used to filter the catalog.</param>
/// <param name="Tags">Free form tags of the game.</param>
/// <param name="CoverImage">Reference of the cover image.</param>
/// <param name="Images">References of the additional images.</param>
/// <param name="Version">Version published in the catalog, as an opaque string.</param>
/// <param name="SizeBytes">Exact size of the package in bytes.</param>
/// <param name="MinPlayers">Minimum number of players.</param>
/// <param name="MaxPlayers">Maximum number of players.</param>
/// <param name="OperatingSystem">Operating system the game runs on.</param>
/// <param name="DistributionType">Distribution type; the MVP only distributes from the server (AD-28).</param>
/// <param name="InstallationType">Installation type; the MVP only installs in portable mode (FR-8).</param>
/// <param name="LaunchConfiguration">Launch configuration materialized by the signed manifest.</param>
/// <param name="Dependencies">Dependencies required by the game.</param>
/// <param name="License">License of the game.</param>
/// <param name="DistributionRestrictions">Restrictions on the publication of the game.</param>
public sealed record GameDescriptor(
    string GameId,
    string Name,
    string Description,
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> Tags,
    string CoverImage,
    IReadOnlyList<string> Images,
    string Version,
    long SizeBytes,
    int MinPlayers,
    int MaxPlayers,
    string OperatingSystem,
    string DistributionType,
    string InstallationType,
    InstallationManifest.LaunchSettings LaunchConfiguration,
    IReadOnlyList<string> Dependencies,
    string License,
    string DistributionRestrictions);
