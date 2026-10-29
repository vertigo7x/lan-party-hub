using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Server.Application.Catalog;

/// <summary>
/// The eighteen mandatory metadata of FR-1 as the use cases of the catalog receive them
/// (AD-1, AD-34).
///
/// <para>
/// The type lives in the Application layer because it is the input of the use cases, and
/// the adapter of the host projects the body of the request onto it. The response of the
/// API is the contract of <c>Shared</c>, which is a different shape: it carries the
/// identifier of the videogame and it is the only contract of metadata of the system
/// (AD-34).
/// </para>
/// </summary>
public sealed class GameMetadata
{
    /// <summary>Creates the metadata of a videogame.</summary>
    /// <param name="name">Human readable name.</param>
    /// <param name="description">Description of the videogame.</param>
    /// <param name="categories">Categories used to filter the catalog.</param>
    /// <param name="tags">Free form tags of the videogame.</param>
    /// <param name="coverImage">Reference of the cover image.</param>
    /// <param name="images">References of the additional images.</param>
    /// <param name="version">Version published in the catalog, as an opaque string.</param>
    /// <param name="sizeBytes">Exact size of the package in bytes.</param>
    /// <param name="minPlayers">Minimum number of players.</param>
    /// <param name="maxPlayers">Maximum number of players.</param>
    /// <param name="operatingSystem">Operating system the videogame runs on.</param>
    /// <param name="distributionType">Distribution type of the videogame.</param>
    /// <param name="installationType">Installation type of the videogame.</param>
    /// <param name="launchConfiguration">
    /// Launch configuration of the videogame: path of the executable, arguments and working
    /// directory. It is the single field of AD-34, with the same shape that the contract of
    /// the response publishes.
    /// </param>
    /// <param name="dependencies">Dependencies required by the videogame.</param>
    /// <param name="license">License of the videogame.</param>
    /// <param name="distributionRestrictions">Restrictions on the publication of the videogame.</param>
    public GameMetadata(
        string name,
        string description,
        IReadOnlyList<string> categories,
        IReadOnlyList<string> tags,
        string coverImage,
        IReadOnlyList<string> images,
        string version,
        long sizeBytes,
        int minPlayers,
        int maxPlayers,
        string operatingSystem,
        string distributionType,
        string installationType,
        InstallationManifest.LaunchSettings launchConfiguration,
        IReadOnlyList<string> dependencies,
        string license,
        string distributionRestrictions)
    {
        ArgumentNullException.ThrowIfNull(launchConfiguration);

        Name = name;
        Description = description;
        Categories = categories;
        Tags = tags;
        CoverImage = coverImage;
        Images = images;
        Version = version;
        SizeBytes = sizeBytes;
        MinPlayers = minPlayers;
        MaxPlayers = maxPlayers;
        OperatingSystem = operatingSystem;
        DistributionType = distributionType;
        InstallationType = installationType;
        LaunchConfiguration = launchConfiguration;
        Dependencies = dependencies;
        License = license;
        DistributionRestrictions = distributionRestrictions;
    }

    /// <summary>Human readable name.</summary>
    public string Name { get; }

    /// <summary>Description of the videogame.</summary>
    public string Description { get; }

    /// <summary>Categories used to filter the catalog.</summary>
    public IReadOnlyList<string> Categories { get; }

    /// <summary>Free form tags of the videogame.</summary>
    public IReadOnlyList<string> Tags { get; }

    /// <summary>Reference of the cover image.</summary>
    public string CoverImage { get; }

    /// <summary>References of the additional images.</summary>
    public IReadOnlyList<string> Images { get; }

    /// <summary>Version published in the catalog, as an opaque string.</summary>
    public string Version { get; }

    /// <summary>Exact size of the package in bytes.</summary>
    public long SizeBytes { get; }

    /// <summary>Minimum number of players.</summary>
    public int MinPlayers { get; }

    /// <summary>Maximum number of players.</summary>
    public int MaxPlayers { get; }

    /// <summary>Operating system the videogame runs on.</summary>
    public string OperatingSystem { get; }

    /// <summary>Distribution type of the videogame.</summary>
    public string DistributionType { get; }

    /// <summary>Installation type of the videogame.</summary>
    public string InstallationType { get; }

    /// <summary>
    /// Launch configuration of the videogame: path of the executable, arguments and working
    /// directory. It is the single field of AD-34, with the same shape that the contract of
    /// the response publishes.
    /// </summary>
    public InstallationManifest.LaunchSettings LaunchConfiguration { get; }

    /// <summary>Dependencies required by the videogame.</summary>
    public IReadOnlyList<string> Dependencies { get; }

    /// <summary>License of the videogame.</summary>
    public string License { get; }

    /// <summary>Restrictions on the publication of the videogame.</summary>
    public string DistributionRestrictions { get; }
}
