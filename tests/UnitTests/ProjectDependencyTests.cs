using System.Text.Json;
using System.Xml.Linq;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// Shape of the dependency graph (AD-1, AD-18, AR-1, AD-28). The checks read the real
/// project files and the real sources of the repository, so they observe the structure that
/// is committed and not a copy of it.
/// </summary>
public sealed class ProjectDependencyTests
{
    private static readonly string[] ForbiddenP2PPackages =
    [
        "MonoTorrent",
        "BitTorrent",
        "BitTorrentClient",
        "P2P",
        "Swarm",
        "Tracker",
    ];

    private static readonly string[] ForbiddenDomainNames =
    [
        "Avalonia",
        "Microsoft.EntityFrameworkCore",
        "Microsoft.Data.Sqlite",
        "Microsoft.AspNetCore",
        "System.Net.Http",
        "HttpClient",
        "Infrastructure",
    ];

    private static readonly string[] ExpectedSolutionProjects =
    [
        "LANPartyHub.Client",
        "LANPartyHub.EndToEndTests",
        "LANPartyHub.IntegrationTests",
        "LANPartyHub.Server",
        "LANPartyHub.Shared",
        "LANPartyHub.UnitTests",
    ];

    /// <summary>
    /// Packages that a role which owns a SQLite file is allowed to reference. The embedded
    /// SQLite version is never pinned on its own (spine, tabla Deferred).
    /// </summary>
    private static readonly string[] AllowedSqlitePackages =
    [
        "Microsoft.Data.Sqlite",
        "Microsoft.EntityFrameworkCore.Sqlite",
    ];

    private static XDocument LoadProject(string projectFile) => XDocument.Load(projectFile);

    private static IReadOnlyList<string> ProjectReferences(XDocument project) => project
        .Descendants()
        .Where(element => element.Name.LocalName == "ProjectReference")
        .Select(element => (string?)element.Attribute("Include") ?? string.Empty)
        .Select(include => Path.GetFileNameWithoutExtension(include))
        .ToArray();

    private static IReadOnlyList<string> PackageReferences(XDocument project) => project
        .Descendants()
        .Where(element => element.Name.LocalName == "PackageReference")
        .Select(element => (string?)element.Attribute("Include") ?? string.Empty)
        .ToArray();

    private static IReadOnlyList<string> FrameworkReferences(XDocument project) => project
        .Descendants()
        .Where(element => element.Name.LocalName == "FrameworkReference")
        .Select(element => (string?)element.Attribute("Include") ?? string.Empty)
        .ToArray();

    private static IReadOnlyDictionary<string, string> PackageReferenceVersions(XDocument project)
    {
        var packages = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var element in project.Descendants().Where(element => element.Name.LocalName == "PackageReference"))
        {
            var id = (string?)element.Attribute("Include") ?? string.Empty;
            var version = (string?)element.Attribute("Version") ?? string.Empty;
            packages[id] = version;
        }

        return packages;
    }

    [Fact]
    public void Shared_does_not_reference_any_other_project()
    {
        var project = LoadProject(Path.Combine(RepositoryLayout.SourceDirectory, "Shared", "LANPartyHub.Shared.csproj"));

        Assert.Empty(ProjectReferences(project));
    }

    [Fact]
    public void Shared_does_not_reference_any_package()
    {
        var project = LoadProject(Path.Combine(RepositoryLayout.SourceDirectory, "Shared", "LANPartyHub.Shared.csproj"));

        Assert.Empty(PackageReferences(project));
    }

    [Fact]
    public void Shared_does_not_reference_any_framework()
    {
        var project = LoadProject(Path.Combine(RepositoryLayout.SourceDirectory, "Shared", "LANPartyHub.Shared.csproj"));

        Assert.Empty(FrameworkReferences(project));
    }

    [Fact]
    public void The_server_references_shared()
    {
        var project = LoadProject(Path.Combine(RepositoryLayout.SourceDirectory, "Server", "LANPartyHub.Server.csproj"));

        Assert.Contains("LANPartyHub.Shared", ProjectReferences(project));
    }

    [Fact]
    public void The_client_references_shared()
    {
        var project = LoadProject(Path.Combine(RepositoryLayout.SourceDirectory, "Client", "LANPartyHub.Client.csproj"));

        Assert.Contains("LANPartyHub.Shared", ProjectReferences(project));
    }

    [Fact]
    public void The_test_projects_reference_the_production_projects()
    {
        var unitTests = LoadProject(Path.Combine(RepositoryLayout.TestDirectory, "UnitTests", "LANPartyHub.UnitTests.csproj"));
        var integrationTests = LoadProject(Path.Combine(RepositoryLayout.TestDirectory, "IntegrationTests", "LANPartyHub.IntegrationTests.csproj"));

        Assert.Contains("LANPartyHub.Shared", ProjectReferences(unitTests));
        Assert.Contains("LANPartyHub.Client", ProjectReferences(unitTests));
        Assert.Contains("LANPartyHub.Server", ProjectReferences(unitTests));
        Assert.Contains("LANPartyHub.Shared", ProjectReferences(integrationTests));
    }

    [Fact]
    public void No_project_references_a_p2p_library()
    {
        var offenders = new List<string>();

        foreach (var projectFile in RepositoryLayout.ProjectFiles())
        {
            var project = LoadProject(projectFile);

            foreach (var package in PackageReferences(project))
            {
                if (ForbiddenP2PPackages.Any(forbidden => package.Contains(forbidden, StringComparison.OrdinalIgnoreCase)))
                {
                    offenders.Add($"{Path.GetFileName(projectFile)} -> {package}");
                }
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void The_avalonia_packages_are_pinned_to_the_version_of_the_spine()
    {
        var project = LoadProject(Path.Combine(RepositoryLayout.SourceDirectory, "Client", "LANPartyHub.Client.csproj"));
        var packages = PackageReferenceVersions(project);
        var avaloniaPackages = packages
            .Where(package => package.Key.StartsWith("Avalonia", StringComparison.Ordinal))
            .ToArray();

        Assert.Contains("Avalonia", packages.Keys);
        Assert.Contains("Avalonia.Desktop", packages.Keys);
        Assert.Contains("Avalonia.Themes.Fluent", packages.Keys);
        Assert.NotEmpty(avaloniaPackages);
        Assert.All(avaloniaPackages, package => Assert.Equal("12.1.4", package.Value));
    }

    [Fact]
    public void No_type_of_the_domain_layer_imports_infrastructure_or_a_framework()
    {
        var domainFiles = RepositoryLayout.SourceFiles(Path.Combine("src", "Server", "Domain"))
            .Concat(RepositoryLayout.SourceFiles(Path.Combine("src", "Client", "Domain")))
            .ToArray();

        Assert.NotEmpty(domainFiles);

        var offenders = new List<string>();

        foreach (var file in domainFiles)
        {
            var content = File.ReadAllText(file);

            foreach (var forbidden in ForbiddenDomainNames)
            {
                if (content.Contains(forbidden, StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetFileName(file)}: {forbidden}");
                }
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void No_type_of_the_application_layer_references_the_outer_layers()
    {
        var applicationFiles = RepositoryLayout.SourceFiles(Path.Combine("src", "Server", "Application"))
            .Concat(RepositoryLayout.SourceFiles(Path.Combine("src", "Client", "Application")))
            .ToArray();

        Assert.NotEmpty(applicationFiles);

        var offenders = new List<string>();

        foreach (var file in applicationFiles)
        {
            var content = File.ReadAllText(file);

            foreach (var forbidden in new[] { "Infrastructure", "Presentation" })
            {
                if (content.Contains(forbidden, StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetFileName(file)}: {forbidden}");
                }
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void No_type_of_the_infrastructure_layer_references_the_presentation_layer()
    {
        var infrastructureFiles = RepositoryLayout.SourceFiles(Path.Combine("src", "Server", "Infrastructure"))
            .Concat(RepositoryLayout.SourceFiles(Path.Combine("src", "Client", "Infrastructure")))
            .ToArray();

        Assert.NotEmpty(infrastructureFiles);

        var offenders = new List<string>();

        foreach (var file in infrastructureFiles)
        {
            if (File.ReadAllText(file).Contains("Presentation", StringComparison.Ordinal))
            {
                offenders.Add(Path.GetFileName(file));
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void Every_project_of_the_solution_targets_net_10()
    {
        foreach (var projectFile in RepositoryLayout.ProjectFiles())
        {
            var project = LoadProject(projectFile);
            var targetFramework = project
                .Descendants()
                .Where(element => element.Name.LocalName == "TargetFramework")
                .Select(element => element.Value)
                .ToArray();

            Assert.Equal(new[] { "net10.0" }, targetFramework);
        }
    }

    [Fact]
    public void The_test_projects_share_the_pinned_runner_stack()
    {
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Microsoft.NET.Test.Sdk"] = "18.11.0",
            ["xunit.v3"] = "3.2.2",
            ["xunit.runner.visualstudio"] = "3.1.5",
        };

        foreach (var projectName in new[] { "UnitTests", "IntegrationTests", "EndToEndTests" })
        {
            var projectFile = Path.Combine(RepositoryLayout.TestDirectory, projectName, $"LANPartyHub.{projectName}.csproj");
            var packages = PackageReferenceVersions(LoadProject(projectFile));

            foreach (var (packageId, version) in expected)
            {
                Assert.True(packages.ContainsKey(packageId), $"{projectName} does not reference '{packageId}'.");
                Assert.Equal(version, packages[packageId]);
            }
        }
    }

    [Fact]
    public void No_production_project_references_a_sqlite_package_it_does_not_own()
    {
        foreach (var projectName in new[] { "Shared", "Server", "Client" })
        {
            var projectFile = Path.Combine(RepositoryLayout.SourceDirectory, projectName, $"LANPartyHub.{projectName}.csproj");
            var packages = PackageReferences(LoadProject(projectFile));

            foreach (var package in packages.Where(package => package.Contains("Sqlite", StringComparison.OrdinalIgnoreCase)))
            {
                if (projectName == "Shared")
                {
                    Assert.Fail($"Shared is contracts only and must not reference '{package}'.");
                }

                Assert.Contains(package, AllowedSqlitePackages);
            }
        }
    }

    [Fact]
    public void The_solution_file_holds_the_six_projects_of_the_solution()
    {
        var solutionFile = Path.Combine(RepositoryLayout.Root, "LANPartyHub.sln");
        var projectNames = new List<string>();

        foreach (var line in File.ReadAllLines(solutionFile))
        {
            if (!line.StartsWith("Project(", StringComparison.Ordinal))
            {
                continue;
            }

            var parts = line.Split('=', 2);

            if (parts.Length != 2)
            {
                continue;
            }

            var fields = parts[1].Split(',');

            if (fields.Length < 2)
            {
                continue;
            }

            var path = fields[1].Trim().Trim('"');

            // The solution folders of the IDE are not projects of the solution.
            if (!path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            projectNames.Add(fields[0].Trim().Trim('"'));
        }

        Assert.Equal(ExpectedSolutionProjects, projectNames.OrderBy(name => name, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void The_sdk_version_is_pinned_at_the_repository_root()
    {
        var globalJson = Path.Combine(RepositoryLayout.Root, "global.json");

        Assert.True(File.Exists(globalJson), "global.json is missing at the repository root.");

        using var document = JsonDocument.Parse(File.ReadAllText(globalJson));
        var root = document.RootElement;

        Assert.True(root.TryGetProperty("sdk", out var sdk));

        Assert.True(sdk.TryGetProperty("version", out var version));
        Assert.Equal("10.0.403", version.GetString());

        Assert.True(sdk.TryGetProperty("rollForward", out var rollForward));
        Assert.Equal("latestFeature", rollForward.GetString());
    }
}
