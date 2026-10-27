namespace LANPartyHub.UnitTests;

/// <summary>
/// Locates the repository from the test output folder, so the structural checks can inspect
/// the real project files and the real sources instead of a copy of them.
/// </summary>
internal static class RepositoryLayout
{
    private const string SolutionFileName = "LANPartyHub.sln";

    /// <summary>Environment variable that overrides the discovery of the repository root.</summary>
    public const string RootOverrideVariable = "LAN_PARTY_HUB_REPO_ROOT";

    /// <summary>Absolute path of the repository root.</summary>
    public static string Root { get; } = FindRoot();

    /// <summary>Absolute path of the folder that holds the three production projects.</summary>
    public static string SourceDirectory => Path.Combine(Root, "src");

    /// <summary>Absolute path of the folder that holds the three test projects.</summary>
    public static string TestDirectory => Path.Combine(Root, "tests");

    /// <summary>Every project file of the solution, ordered by path.</summary>
    public static IReadOnlyList<string> ProjectFiles()
    {
        var files = new List<string>();

        foreach (var root in new[] { SourceDirectory, TestDirectory })
        {
            files.AddRange(Directory.GetFiles(root, "*.csproj", SearchOption.AllDirectories));
        }

        return files.OrderBy(path => path, StringComparer.Ordinal).ToArray();
    }

    /// <summary>Every C# source file under a directory of the repository, ordered by path.</summary>
    public static IReadOnlyList<string> SourceFiles(string relativeDirectory)
    {
        var directory = Path.Combine(Root, relativeDirectory);

        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

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
