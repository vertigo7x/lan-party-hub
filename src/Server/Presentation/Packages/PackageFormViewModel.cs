using LANPartyHub.Server.Application.Packages;
using LANPartyHub.Server.Presentation.Formatting;
using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Server.Presentation.Packages;

/// <summary>
/// View model of the form of the publication of a package (AD-1, AC1).
///
/// <para>
/// The view model reflects the data that the organizer types to publish a package: the
/// videogame it belongs to, the version, the exact size in bytes and the operations of the
/// installation manifest. It holds no rule of the domain: the validation belongs to the
/// domain, and it is the domain that rejects the publication (AD-1).
/// </para>
/// <para>
/// The view model exposes the state of the block of the publication, naming the concrete
/// field that is missing, so the organizer knows which one to fill in instead of reading a
/// generic error (AC1, UX-DR-44).
/// </para>
/// </summary>
public sealed class PackageFormViewModel
{
    /// <summary>
    /// Creates the view model of the form of the publication with its data empty.
    /// </summary>
    public PackageFormViewModel()
    {
    }

    /// <summary>
    /// Creates the view model of the form of the publication with the data of a videogame
    /// that is already in the catalog.
    /// </summary>
    /// <param name="game">
    /// Contract of the videogame whose package is published, or <c>null</c> when the form is
    /// empty.
    /// </param>
    public PackageFormViewModel(GameDescriptor? game)
    {
        GameId = game?.GameId;
        Version = game?.Version ?? string.Empty;
        SizeBytes = game?.SizeBytes ?? 0;
        LaunchExecutablePath = game?.LaunchConfiguration.ExecutablePath ?? string.Empty;
        LaunchArguments = game is null ? string.Empty : Join(game.LaunchConfiguration.Arguments);
        LaunchWorkingDirectory = game?.LaunchConfiguration.WorkingDirectory ?? string.Empty;
    }

    /// <summary>
    /// Identifier of the videogame whose package is published.
    /// </summary>
    public string? GameId { get; set; }

    /// <summary>
    /// Identifier of the package that is published. The server generates it when the form
    /// does not bring one.
    /// </summary>
    public string PackageId { get; set; } = string.Empty;

    /// <summary>
    /// Opaque version string of the publisher.
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Exact size of the package in bytes.
    /// </summary>
    public long SizeBytes { get; set; }

    /// <summary>
    /// Path of the executable, relative to the install root.
    /// </summary>
    public string LaunchExecutablePath { get; set; } = string.Empty;

    /// <summary>
    /// Arguments passed to the executable, separated by commas.
    /// </summary>
    public string LaunchArguments { get; set; } = string.Empty;

    /// <summary>
    /// Working directory of the process.
    /// </summary>
    public string LaunchWorkingDirectory { get; set; } = string.Empty;

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
    /// The state of the block of the publication. It is <c>null</c> when the publication
    /// is not blocked, and it names the concrete field that is missing when it is
    /// (AC1, UX-DR-44).
    /// </summary>
    public string? BlockedFieldName { get; private set; }

    /// <summary>
    /// Tells whether the publication is blocked by the catalog because the videogame
    /// does not carry the license or the restrictions of distribution (AC1).
    /// </summary>
    public bool IsBlocked => BlockedFieldName is not null;

    /// <summary>
    /// Records that the publication is blocked because a concrete field of the videogame
    /// is missing, so the form can say <em>which</em> one instead of a generic error
    /// (AC1, UX-DR-44).
    /// </summary>
    /// <param name="fieldName">Name of the field that is missing.</param>
    public void BlockFor(string fieldName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);

        BlockedFieldName = fieldName;
    }

    /// <summary>
    /// Records that the publication is not blocked. It is called when the publication
    /// succeeds, so the form does not keep the state of a previous attempt (AC1).
    /// </summary>
    public void ClearBlock() => BlockedFieldName = null;

    /// <summary>
    /// The names of the fields that the publication demands. The view marks exactly these
    /// ones (AC1).
    /// </summary>
    public static IReadOnlyList<string> RequiredFieldNames =>
    [
        "gameId",
        "packageId",
        "version",
        "sizeBytes",
    ];

    /// <summary>
    /// The label of a field with the mark of the obligation, the way the form shows it
    /// when the publication demands it.
    /// </summary>
    /// <param name="label">Label of the field.</param>
    /// <param name="fieldName">Name of the field.</param>
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
    /// Builds the contract of the package that the body of the publication carries, out of
    /// what the organizer has typed.
    ///
    /// <para>
    /// La referencia al manifiesto va VACÍA, porque la acuña el servidor cuando firma el
    /// manifiesto canónico: un cuerpo que la declara está describiendo un manifiesto que
    /// no existe (AD-2, AC2).
    /// </para>
    /// </summary>
    /// <returns>The contract of the package.</returns>
    public PackageDescriptor ToPackageDescriptor() => new(
        string.IsNullOrWhiteSpace(PackageId) ? string.Empty : PackageId.Trim(),
        GameId ?? string.Empty,
        Version,
        SizeBytes,
        0,
        string.Empty,
        string.Empty);

    /// <summary>
    /// Builds the launch configuration of the manifest, out of what the organizer has typed.
    /// </summary>
    /// <returns>The launch configuration.</returns>
    public InstallationManifest.LaunchSettings ToLaunchSettings() => new(
        LaunchExecutablePath,
        SplitList(LaunchArguments),
        LaunchWorkingDirectory);

    /// <summary>
    /// Builds the identity that the manifest declares, out of what the organizer has
    /// typed. It is the same publication that the descriptor declares, so the two
    /// coincide (AD-2, AC7).
    /// </summary>
    /// <returns>The identity of the manifest.</returns>
    public ManifestIdentity ToManifestIdentity() => new(
        string.IsNullOrWhiteSpace(PackageId) ? string.Empty : PackageId.Trim(),
        Version,
        SizeBytes,
        InstallationManifestSchema.Version,
        string.Empty);

    /// <summary>
    /// Builds the operations of the manifest. The form declares the operations that the
    /// product needs, and they all belong to the closed set of AD-7.
    /// </summary>
    /// <returns>The operations of the manifest, in order.</returns>
    public IReadOnlyList<InstallationManifest.ManifestOperation> ToOperations() =>
    [
        new InstallationManifest.CreateDirectory("game"),
        new InstallationManifest.Extract("game.zip", "game"),
        new InstallationManifest.SetLaunchConfig(ToLaunchSettings()),
    ];

    /// <summary>
    /// Joins a list of the form into the text that the field shows.
    /// </summary>
    /// <param name="values">Values of the field.</param>
    /// <returns>The text of the field.</returns>
    private static string Join(IReadOnlyList<string> values) => string.Join(", ", values);
}
