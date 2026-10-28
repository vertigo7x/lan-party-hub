using LANPartyHub.Client.Host;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// Command line and effective configuration of the client (AD-12, AR-23).
///
/// <para>
/// The tests that touch the environment of the process live in this single class on purpose:
/// xUnit never runs two tests of the same class in parallel, so the variables that are set
/// here are never observed half written by another test of this class. The tests of the
/// server use variables of their own section, so the two classes never collide.
/// </para>
/// </summary>
public sealed class ClientCommandLineTests : IDisposable
{
    private const string ServerAddressVariable = "Client__serverAddress";
    private const string InstallDirectoryVariable = "Client__installDirectory";
    private const string ThemeVariable = "Client__theme";

    private readonly List<string> _temporaryFolders = [];

    [Fact]
    public void A_valid_server_address_install_directory_and_theme_are_parsed()
    {
        var parsed = ClientCommandLine.TryParse(
            ["--server-address", "192.168.1.10:8080", "--install-directory", @"D:\juegos", "--theme", "Dark"],
            out var commandLine,
            out var error);

        Assert.True(parsed, error);
        Assert.NotNull(commandLine);
        Assert.Equal("192.168.1.10:8080", commandLine.ServerAddress);
        Assert.Equal(@"D:\juegos", commandLine.InstallDirectory);
        Assert.Equal("Dark", commandLine.Theme);
    }

    [Fact]
    public void An_empty_command_line_leaves_every_option_unset()
    {
        var parsed = ClientCommandLine.TryParse([], out var commandLine, out var error);

        Assert.True(parsed, error);
        Assert.NotNull(commandLine);
        Assert.Null(commandLine.ServerAddress);
        Assert.Null(commandLine.InstallDirectory);
        Assert.Null(commandLine.Theme);
    }

    [Fact]
    public void An_unknown_option_is_rejected_and_the_supported_ones_are_named()
    {
        var parsed = ClientCommandLine.TryParse(["--tracker-port", "6881"], out var commandLine, out var error);

        Assert.False(parsed);
        Assert.Null(commandLine);
        Assert.NotNull(error);
        Assert.Contains("--tracker-port", error);
        Assert.Contains("--server-address", error);
        Assert.Contains("--install-directory", error);
        Assert.Contains("--theme", error);
    }

    [Fact]
    public void A_missing_value_is_rejected()
    {
        var parsed = ClientCommandLine.TryParse(["--install-directory"], out var commandLine, out var error);

        Assert.False(parsed);
        Assert.Null(commandLine);
        Assert.NotNull(error);
        Assert.Contains("missing value", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--install-directory", error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_value_is_rejected_as_a_missing_value(string value)
    {
        var parsed = ClientCommandLine.TryParse(["--theme", value], out var commandLine, out var error);

        Assert.False(parsed);
        Assert.Null(commandLine);
        Assert.NotNull(error);
        Assert.Contains("missing value", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--theme", error);
    }

    [Fact]
    public void A_value_that_looks_like_another_option_is_rejected_and_does_not_eat_the_theme()
    {
        var parsed = ClientCommandLine.TryParse(
            ["--install-directory", "--theme", "Dark"],
            out var commandLine,
            out var error);

        Assert.False(parsed);
        Assert.Null(commandLine);
        Assert.NotNull(error);
        Assert.Contains("missing value", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--install-directory", error);

        // The value that looks like another option is not swallowed as a value, so the theme
        // is still parsed as the theme of its own option.
        var parsedTheme = ClientCommandLine.TryParse(["--theme", "Dark"], out var onlyTheme, out _);

        Assert.True(parsedTheme);
        Assert.NotNull(onlyTheme);
        Assert.Equal("Dark", onlyTheme.Theme);
    }

    [Fact]
    public void An_invalid_command_line_aborts_the_startup_with_the_message()
    {
        var exception = Assert.Throws<ClientStartupException>(
            () => ClientCommandLine.Parse(["--tracker-port", "6881"]));

        Assert.Contains("--tracker-port", exception.Message);
    }

    [Theory]
    [InlineData("192.168.1.10:8080", "192.168.1.10", 8080)]
    [InlineData("servidor:1", "servidor", 1)]
    [InlineData("servidor:65535", "servidor", 65535)]
    [InlineData("[::1]:8080", "[::1]", 8080)]
    public void The_address_of_the_server_is_a_host_and_a_port(string value, string host, int port)
    {
        Assert.True(
            ClientCommandLine.TryParseServerAddress(value, out var parsedHost, out var parsedPort, out var error),
            error);

        Assert.Equal(host, parsedHost);
        Assert.Equal(port, parsedPort);
    }

    [Theory]
    [InlineData("192.168.1.10")]
    [InlineData("192.168.1.10:")]
    [InlineData(":8080")]
    [InlineData("192.168.1.10:0")]
    [InlineData("192.168.1.10:70000")]
    [InlineData("192.168.1.10:abc")]
    public void An_address_that_is_not_a_host_and_a_port_is_rejected(string value)
    {
        Assert.False(ClientCommandLine.TryParseServerAddress(value, out _, out _, out var error));

        Assert.NotNull(error);
        Assert.Contains(ClientCommandLine.ServerAddressFormat, error);
    }

    [Fact]
    public void An_invalid_server_address_aborts_the_startup()
    {
        var parsed = ClientCommandLine.TryParse(["--server-address", "192.168.1.10"], out var commandLine, out var error);

        Assert.False(parsed);
        Assert.Null(commandLine);
        Assert.NotNull(error);
        Assert.Contains(ClientCommandLine.ServerAddressFormat, error);
    }

    [Fact]
    public void The_defaults_are_the_install_folder_of_the_data_folder_and_the_system_theme()
    {
        var dataFolder = Path.Combine("C:", "datos", "LanPartyHub");

        var resolved = ClientConfiguration.Resolve(new ConfigurationBuilder().Build(), dataFolder);

        Assert.Null(resolved.ServerAddress);
        Assert.Equal(Path.Combine(dataFolder, "install"), resolved.InstallDirectory);
        Assert.Equal(ClientConfiguration.DefaultTheme, resolved.Theme);
    }

    [Fact]
    public void The_flag_wins_over_the_environment_and_over_the_file()
    {
        var baseDirectory = CreateConfigurationFolder(
            @"{ ""Client"": { ""serverAddress"": ""10.0.0.3:3333"", ""installDirectory"": ""del-archivo"", ""theme"": ""del-archivo"" } }");
        var dataFolder = Path.Combine("C:", "datos", "LanPartyHub");

        try
        {
            Environment.SetEnvironmentVariable(ServerAddressVariable, "10.0.0.2:2222");
            Environment.SetEnvironmentVariable(InstallDirectoryVariable, "del-entorno");
            Environment.SetEnvironmentVariable(ThemeVariable, "del-entorno");

            var configuration = BuildConfiguration(
                baseDirectory,
                ["--server-address", "10.0.0.1:1111", "--install-directory", "del-flag", "--theme", "del-flag"]);

            var resolved = ClientConfiguration.Resolve(configuration, dataFolder);

            Assert.Equal("10.0.0.1:1111", resolved.ServerAddress);
            Assert.Equal(Path.GetFullPath(Path.Combine(dataFolder, "del-flag")), resolved.InstallDirectory);
            Assert.Equal("del-flag", resolved.Theme);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ServerAddressVariable, null);
            Environment.SetEnvironmentVariable(InstallDirectoryVariable, null);
            Environment.SetEnvironmentVariable(ThemeVariable, null);
        }
    }

    [Fact]
    public void The_environment_wins_over_the_file()
    {
        var baseDirectory = CreateConfigurationFolder(
            @"{ ""Client"": { ""serverAddress"": ""10.0.0.3:3333"", ""installDirectory"": ""del-archivo"", ""theme"": ""del-archivo"" } }");
        var dataFolder = Path.Combine("C:", "datos", "LanPartyHub");

        try
        {
            Environment.SetEnvironmentVariable(ServerAddressVariable, "10.0.0.2:2222");
            Environment.SetEnvironmentVariable(InstallDirectoryVariable, "del-entorno");
            Environment.SetEnvironmentVariable(ThemeVariable, "del-entorno");

            var configuration = BuildConfiguration(baseDirectory, []);
            var resolved = ClientConfiguration.Resolve(configuration, dataFolder);

            Assert.Equal("10.0.0.2:2222", resolved.ServerAddress);
            Assert.Equal(Path.GetFullPath(Path.Combine(dataFolder, "del-entorno")), resolved.InstallDirectory);
            Assert.Equal("del-entorno", resolved.Theme);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ServerAddressVariable, null);
            Environment.SetEnvironmentVariable(InstallDirectoryVariable, null);
            Environment.SetEnvironmentVariable(ThemeVariable, null);
        }
    }

    [Fact]
    public void The_file_wins_over_the_defaults()
    {
        var baseDirectory = CreateConfigurationFolder(
            @"{ ""Client"": { ""serverAddress"": ""10.0.0.3:3333"", ""installDirectory"": ""del-archivo"", ""theme"": ""del-archivo"" } }");
        var dataFolder = Path.Combine("C:", "datos", "LanPartyHub");

        try
        {
            Environment.SetEnvironmentVariable(ServerAddressVariable, null);
            Environment.SetEnvironmentVariable(InstallDirectoryVariable, null);
            Environment.SetEnvironmentVariable(ThemeVariable, null);

            var configuration = BuildConfiguration(baseDirectory, []);
            var resolved = ClientConfiguration.Resolve(configuration, dataFolder);

            Assert.Equal("10.0.0.3:3333", resolved.ServerAddress);
            Assert.Equal(Path.GetFullPath(Path.Combine(dataFolder, "del-archivo")), resolved.InstallDirectory);
            Assert.Equal("del-archivo", resolved.Theme);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ServerAddressVariable, null);
            Environment.SetEnvironmentVariable(InstallDirectoryVariable, null);
            Environment.SetEnvironmentVariable(ThemeVariable, null);
        }
    }

    [Fact]
    public void An_absolute_install_directory_is_kept_and_it_is_the_only_confined_one()
    {
        var dataFolder = Path.Combine("C:", "datos", "LanPartyHub");
        var baseDirectory = CreateConfigurationFolder(
            @"{ ""Client"": { ""installDirectory"": ""D:\\juegos"" } }");

        var configuration = BuildConfiguration(baseDirectory, []);
        var resolved = ClientConfiguration.Resolve(configuration, dataFolder);

        Assert.Equal(Path.GetFullPath(@"D:\juegos"), resolved.InstallDirectory);
    }

    [Fact]
    public void An_empty_value_in_the_configuration_file_aborts_the_startup_and_names_the_source()
    {
        var baseDirectory = CreateConfigurationFolder(@"{ ""Client"": { ""theme"": """" } }");

        var configuration = BuildConfiguration(baseDirectory, []);

        var exception = Assert.Throws<ClientStartupException>(
            () => ClientConfiguration.Resolve(configuration, Path.Combine("C:", "datos")));

        Assert.Contains("Client:theme", exception.Message);
        Assert.Contains(ClientConfiguration.FileName, exception.Message);
    }

    [Fact]
    public void An_empty_install_directory_in_the_configuration_file_aborts_the_startup()
    {
        var baseDirectory = CreateConfigurationFolder(@"{ ""Client"": { ""installDirectory"": """" } }");

        var configuration = BuildConfiguration(baseDirectory, []);

        var exception = Assert.Throws<ClientStartupException>(
            () => ClientConfiguration.Resolve(configuration, Path.Combine("C:", "datos")));

        Assert.Contains("Client:installDirectory", exception.Message);
        Assert.Contains(ClientConfiguration.FileName, exception.Message);
    }

    [Fact]
    public void A_rejected_value_names_the_source_that_wins()
    {
        var baseDirectory = CreateConfigurationFolder(@"{ ""Client"": { ""theme"": ""del-archivo"" } }");

        try
        {
            // The environment holds the key and it is the last source that has it, so it is
            // the source that the message has to name.
            Environment.SetEnvironmentVariable(ThemeVariable, string.Empty);

            var configuration = BuildConfiguration(baseDirectory, []);

            var exception = Assert.Throws<ClientStartupException>(
                () => ClientConfiguration.Resolve(configuration, Path.Combine("C:", "datos")));

            Assert.Contains(ThemeVariable, exception.Message);
            Assert.DoesNotContain(ClientConfiguration.FileName, exception.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ThemeVariable, null);
        }
    }

    [Fact]
    public void A_configuration_file_that_is_not_valid_json_aborts_the_startup_with_an_actionable_message()
    {
        var baseDirectory = CreateConfigurationFolder("{ esto no es un json ");

        var exception = Assert.Throws<ClientStartupException>(
            () => ClientConfiguration.Resolve(
                baseDirectory,
                Path.Combine("C:", "datos"),
                ClientCommandLine.Parse([])));

        Assert.Contains(ClientConfiguration.FileName, exception.Message);
        Assert.Contains("no es válido", exception.Message);
        Assert.Contains("valores por defecto", exception.Message);
    }

    private static IConfigurationRoot BuildConfiguration(string baseDirectory, string[] args)
    {
        var builder = new ConfigurationBuilder().SetBasePath(baseDirectory);

        ClientConfiguration.AddSources(builder, ClientCommandLine.Parse(args));

        return builder.Build();
    }

    private string CreateConfigurationFolder(string content)
    {
        var folder = Directory.CreateTempSubdirectory("lan-party-hub-client-config-");

        _temporaryFolders.Add(folder.FullName);

        File.WriteAllText(Path.Combine(folder.FullName, ClientConfiguration.FileName), content);

        return folder.FullName;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var folder in _temporaryFolders)
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
                // A file that the operating system still holds does not fail the test.
            }
        }
    }
}
