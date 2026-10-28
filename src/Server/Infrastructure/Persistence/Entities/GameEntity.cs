namespace LANPartyHub.Server.Infrastructure.Persistence.Entities;

/// <summary>
/// Row of the <c>GAME</c> table (AD-22, AD-34).
///
/// The table persists exactly the fields of the <c>GameDescriptor</c> contract, with the same
/// name and in the same order, so the table and the contract cannot diverge (AD-34). The
/// collections are stored as JSON text, because the ERD declares them as text columns.
/// </summary>
public sealed class GameEntity
{
    /// <summary>Opaque UUID v7 identifier of the game.</summary>
    public string GameId { get; set; } = string.Empty;

    /// <summary>Human readable name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Description of the game.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Categories used to filter the catalog.</summary>
    public string[] Categories { get; set; } = [];

    /// <summary>Free form tags of the game.</summary>
    public string[] Tags { get; set; } = [];

    /// <summary>Reference of the cover image.</summary>
    public string CoverImage { get; set; } = string.Empty;

    /// <summary>References of the additional images.</summary>
    public string[] Images { get; set; } = [];

    /// <summary>Version published in the catalog, as an opaque string.</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>Exact size of the package in bytes.</summary>
    public long SizeBytes { get; set; }

    /// <summary>Minimum number of players.</summary>
    public int MinPlayers { get; set; }

    /// <summary>Maximum number of players.</summary>
    public int MaxPlayers { get; set; }

    /// <summary>Operating system the game runs on.</summary>
    public string OperatingSystem { get; set; } = string.Empty;

    /// <summary>Distribution type; the MVP only distributes from the server (AD-28).</summary>
    public string DistributionType { get; set; } = string.Empty;

    /// <summary>Installation type; the MVP only installs in portable mode (FR-8).</summary>
    public string InstallationType { get; set; } = string.Empty;

    /// <summary>Launch configuration materialized by the signed manifest.</summary>
    public Shared.Contracts.InstallationManifest.LaunchSettings LaunchConfiguration { get; set; } =
        new(string.Empty, [], string.Empty);

    /// <summary>Dependencies required by the game.</summary>
    public string[] Dependencies { get; set; } = [];

    /// <summary>License of the game.</summary>
    public string License { get; set; } = string.Empty;

    /// <summary>Restrictions on the publication of the game.</summary>
    public string DistributionRestrictions { get; set; } = string.Empty;
}
