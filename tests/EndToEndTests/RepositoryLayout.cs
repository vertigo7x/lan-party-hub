namespace LANPartyHub.EndToEndTests;

/// <summary>
/// Locates the repository from the test output folder, so the end to end level can read the
/// planning artifacts that declare the acceptance scenario.
/// </summary>
internal static class RepositoryLayout
{
    private const string SolutionFileName = "LANPartyHub.sln";

    /// <summary>Environment variable that overrides the discovery of the repository root.</summary>
    public const string RootOverrideVariable = "LAN_PARTY_HUB_REPO_ROOT";

    /// <summary>Absolute path of the repository root.</summary>
    public static string Root { get; } = FindRoot();

    /// <summary>Absolute path of the file that declares the acceptance scenario of the MVP.</summary>
    public static string ProductRequirementsPath => Path.Combine(
        Root,
        "_bmad-output",
        "planning-artifacts",
        "prds",
        "prd-lan-party-hub-2026-10-26",
        "prd.md");

    private static string FindRoot()
    {
        var overridePath = Environment.GetEnvironmentVariable(RootOverrideVariable);

        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            var overridden = Path.GetFullPath(overridePath);

            if (!File.Exists(Path.Combine(overridden, SolutionFileName)))
            {
                throw new InvalidOperationException(
                    $"'{RootOverrideVariable}' points to '{overridden}', where '{SolutionFileName}' is missing.");
            }

            return overridden;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"The repository root was not found from '{AppContext.BaseDirectory}': '{SolutionFileName}' is missing. " +
            $"Set '{RootOverrideVariable}' to the root of the repository to run these checks in another layout.");
    }
}
