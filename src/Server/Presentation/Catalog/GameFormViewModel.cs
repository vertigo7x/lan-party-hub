using LANPartyHub.Server.Domain.Catalog;
using LANPartyHub.Server.Presentation.Formatting;
using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Server.Presentation.Catalog;

/// <summary>
/// View model of the form of the alta and the modification of a videogame (AC4).
///
/// <para>
/// The view model reflects the eighteen mandatory metadata of FR-1, it marks as
/// obligatory exactly the ones that FR-1 demands, and it exposes the size as the decimal
/// value that the interface shows (AD-7, AC4).
/// </para>
/// <para>
/// The size is written and edited in exact bytes, the value that is persisted, and it is
/// presented as the derived decimal value. The view model holds no rule of the domain:
/// the validation of the metadata belongs to the domain and it is the domain that
/// rejects the registration (AD-1).
/// </para>
/// <para>
/// The view model also builds the contract of <c>Shared</c> that the use cases receive
/// out of what the organizer has typed, so the form is wired to the use case and not
/// only to a label of status.
/// </para>
/// </summary>
public sealed class GameFormViewModel
{
    /// <summary>
    /// Creates the view model of the form with the eighteen metadata of FR-1 empty.
    /// </summary>
    public GameFormViewModel()
        : this(null)
    {
    }

    /// <summary>
    /// Creates the view model of the form with the metadata of a videogame that is being
    /// modified.
    /// </summary>
    /// <param name="game">
    /// Contract of the videogame that is being modified, or <c>null</c> when the form is
    /// for an alta.
    /// </param>
    public GameFormViewModel(GameDescriptor? game)
    {
        GameId = game?.GameId;
        Name = game?.Name ?? string.Empty;
        Description = game?.Description ?? string.Empty;
        Categories = game is null ? string.Empty : Join(game.Categories);
        Tags = game is null ? string.Empty : Join(game.Tags);
        CoverImage = game?.CoverImage ?? string.Empty;
        Images = game is null ? string.Empty : Join(game.Images);
        Version = game?.Version ?? string.Empty;
        SizeBytes = game?.SizeBytes ?? 0;
        MinPlayers = game?.MinPlayers ?? 1;
        MaxPlayers = game?.MaxPlayers ?? 1;
        OperatingSystem = game?.OperatingSystem ?? string.Empty;
        DistributionType = game?.DistributionType ?? string.Empty;
        InstallationType = game?.InstallationType ?? string.Empty;
        LaunchExecutablePath = game?.LaunchConfiguration.ExecutablePath ?? string.Empty;
        LaunchArguments = game is null ? string.Empty : Join(game.LaunchConfiguration.Arguments);
        LaunchWorkingDirectory = game?.LaunchConfiguration.WorkingDirectory ?? string.Empty;
        Dependencies = game is null ? string.Empty : Join(game.Dependencies);
        License = game?.License ?? string.Empty;
        DistributionRestrictions = game?.DistributionRestrictions ?? string.Empty;
    }

    /// <summary>
    /// Identifier of the videogame that is being modified, or <c>null</c> when the form is
    /// for an alta. The identifier never changes in a modification (AD-34).
    /// </summary>
    public string? GameId { get; }

    /// <summary>Human readable name.</summary>
    public string Name { get; set; }

    /// <summary>Description of the videogame.</summary>
    public string Description { get; set; }

    /// <summary>Categories used to filter the catalog, separated by commas.</summary>
    public string Categories { get; set; }

    /// <summary>Free form tags of the videogame, separated by commas.</summary>
    public string Tags { get; set; }

    /// <summary>Reference of the cover image.</summary>
    public string CoverImage { get; set; }

    /// <summary>References of the additional images, separated by commas.</summary>
    public string Images { get; set; }

    /// <summary>Version published in the catalog, as an opaque string.</summary>
    public string Version { get; set; }

    /// <summary>Exact size of the package in bytes.</summary>
    public long SizeBytes { get; set; }

    /// <summary>Minimum number of players.</summary>
    public int MinPlayers { get; set; }

    /// <summary>Maximum number of players.</summary>
    public int MaxPlayers { get; set; }

    /// <summary>Operating system the videogame runs on.</summary>
    public string OperatingSystem { get; set; }

    /// <summary>Distribution type of the videogame.</summary>
    public string DistributionType { get; set; }

    /// <summary>Installation type of the videogame.</summary>
    public string InstallationType { get; set; }

    /// <summary>Path of the executable, relative to the install root.</summary>
    public string LaunchExecutablePath { get; set; }

    /// <summary>Arguments passed to the executable, separated by commas.</summary>
    public string LaunchArguments { get; set; }

    /// <summary>Working directory of the process.</summary>
    public string LaunchWorkingDirectory { get; set; }

    /// <summary>Dependencies required by the videogame, separated by commas.</summary>
    public string Dependencies { get; set; }

    /// <summary>License of the videogame.</summary>
    public string License { get; set; }

    /// <summary>Restrictions on the publication of the videogame.</summary>
    public string DistributionRestrictions { get; set; }

    /// <summary>
    /// The size as the interface shows it: the numeric value in decimal gigabytes, with
    /// the Spanish decimal separator and exactly one decimal (AD-7, AC4).
    /// </summary>
    public string SizeValue => SizeFormatter.FormatValue(SizeBytes);

    /// <summary>
    /// The unit of the size, which travels apart from the numeric value so the view can
    /// put each one in its own token (AC4).
    /// </summary>
    public string SizeUnit => SizeFormatter.Unit;

    /// <summary>
    /// Tells whether the form is editing a videogame that is already in the catalog.
    /// </summary>
    public bool IsModification => !string.IsNullOrWhiteSpace(GameId);

    /// <summary>
    /// The names of the fields that FR-1 declares obligatory. The view marks exactly these
    /// ones (AC4).
    /// </summary>
    /// <remarks>
    /// The license and the restrictions of distribution are **not** in this list: NFR-8
    /// makes them obligatory before publishing a package, not before registering the
    /// videogame, and the publication is the moment in which the catalog demands them
    /// (AC1 of the story of the publication).
    /// </remarks>
    public static IReadOnlyList<string> RequiredFieldNames =>
    [
        GameRegistration.FieldNames.Name,
        GameRegistration.FieldNames.Description,
        GameRegistration.FieldNames.Categories,
        GameRegistration.FieldNames.Tags,
        GameRegistration.FieldNames.CoverImage,
        GameRegistration.FieldNames.Images,
        GameRegistration.FieldNames.Version,
        GameRegistration.FieldNames.SizeBytes,
        GameRegistration.FieldNames.MinPlayers,
        GameRegistration.FieldNames.MaxPlayers,
        GameRegistration.FieldNames.OperatingSystem,
        GameRegistration.FieldNames.DistributionType,
        GameRegistration.FieldNames.InstallationType,
        GameRegistration.FieldNames.LaunchConfiguration,
        GameRegistration.FieldNames.Dependencies,
    ];

    /// <summary>
    /// The label of a field with the mark of the obligation, the way the form shows it
    /// when FR-1 demands it.
    ///
    /// <para>
    /// The mark is added only when the label does not already carry it: the form writes
    /// the visible labels with the mark and passes that same text here, so the label of
    /// the tooltip would show the mark twice.
    /// </para>
    /// </summary>
    /// <param name="label">Label of the field.</param>
    /// <param name="fieldName">Name of the field, as the domain names it.</param>
    /// <returns>The label with the mark of the obligation, when the field is obligatory.</returns>
    public static string LabelFor(string label, string fieldName)
    {
        if (!RequiredFieldNames.Contains(fieldName, StringComparer.Ordinal))
        {
            return label;
        }

        return label.Contains('*', StringComparison.Ordinal) ? label : $"{label} *";
    }

    /// <summary>
    /// Splits a list of the form into the values that it holds, discarding the empty ones.
    /// </summary>
    /// <param name="value">Text of the field.</param>
    /// <returns>The values of the field.</returns>
    public static IReadOnlyList<string> SplitList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
    }

    /// <summary>
    /// Builds the contract of <c>Shared</c> with the eighteen metadata of FR-1 out of what
    /// the organizer has typed. The contract is the single form that the use cases receive
    /// and the single form that the API publishes (AD-34).
    /// </summary>
    /// <returns>The contract of the videogame.</returns>
    public GameDescriptor ToDescriptor() => new(
        GameId ?? string.Empty,
        Name,
        Description,
        SplitList(Categories),
        SplitList(Tags),
        CoverImage,
        SplitList(Images),
        Version,
        SizeBytes,
        MinPlayers,
        MaxPlayers,
        OperatingSystem,
        DistributionType,
        InstallationType,
        new InstallationManifest.LaunchSettings(
            LaunchExecutablePath,
            SplitList(LaunchArguments),
            LaunchWorkingDirectory),
        SplitList(Dependencies),
        License,
        DistributionRestrictions);

    /// <summary>
    /// Joins a list of the form into the text that the field shows.
    /// </summary>
    /// <param name="values">Values of the field.</param>
    /// <returns>The text of the field.</returns>
    private static string Join(IReadOnlyList<string> values) => string.Join(", ", values);
}
