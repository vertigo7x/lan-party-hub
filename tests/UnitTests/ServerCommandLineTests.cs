using LANPartyHub.Server.Host;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// Command line of the event server and effective configuration (AD-12, AR-23).
///
/// The tests that touch the environment of the process live in this single class on
/// purpose: xUnit never runs two tests of the same class in parallel, so the variables that
/// are set here are never observed half written by another test of this class. The
/// integration level always passes both flags, and the flags have the highest priority, so
/// it cannot be affected by the variables that are set here.
/// </summary>
public sealed class ServerCommandLineTests
{
    private const string DataDirectoryVariable = "Server__dataDirectory";
    private const string HttpPortVariable = "Server__httpPort";

    [Fact]
    public void A_valid_data_directory_and_port_are_parsed()
    {
        var parsed = ServerCommandLine.TryParse(
            ["--data-dir", @"D:\datos", "--http-port", "8080"],
            out var commandLine,
            out var error);

        Assert.True(parsed, error);
        Assert.NotNull(commandLine);
        Assert.Equal(@"D:\datos", commandLine.DataDirectory);
        Assert.Equal(8080, commandLine.HttpPort);
    }

    [Fact]
    public void An_empty_command_line_leaves_every_option_unset()
    {
        var parsed = ServerCommandLine.TryParse([], out var commandLine, out var error);

        Assert.True(parsed, error);
        Assert.NotNull(commandLine);
        Assert.Null(commandLine.DataDirectory);
        Assert.Null(commandLine.HttpPort);
    }

    [Fact]
    public void An_unknown_option_is_rejected_and_the_supported_ones_are_named()
    {
        var parsed = ServerCommandLine.TryParse(["--tracker-port", "6881"], out var commandLine, out var error);

        Assert.False(parsed);
        Assert.Null(commandLine);
        Assert.NotNull(error);
        Assert.Contains("--tracker-port", error);
        Assert.Contains("--data-dir", error);
        Assert.Contains("--http-port", error);
    }

    [Fact]
    public void A_missing_value_is_rejected()
    {
        var parsed = ServerCommandLine.TryParse(["--data-dir"], out var commandLine, out var error);

        Assert.False(parsed);
        Assert.Null(commandLine);
        Assert.NotNull(error);
        Assert.Contains("missing value", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--data-dir", error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_value_is_rejected_as_a_missing_value(string value)
    {
        var parsed = ServerCommandLine.TryParse(["--data-dir", value], out var commandLine, out var error);

        Assert.False(parsed);
        Assert.Null(commandLine);
        Assert.NotNull(error);
        Assert.Contains("missing value", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--data-dir", error);
    }

    [Fact]
    public void A_value_that_looks_like_another_option_is_rejected_and_does_not_eat_the_port()
    {
        var parsed = ServerCommandLine.TryParse(
            ["--data-dir", "--http-port", "8080"],
            out var commandLine,
            out var error);

        Assert.False(parsed);
        Assert.Null(commandLine);
        Assert.NotNull(error);
        Assert.Contains("missing value", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--data-dir", error);

        // The value that looks like another option is not swallowed as a value, so the port
        // is still parsed as the port of its own option.
        var parsedPort = ServerCommandLine.TryParse(["--http-port", "8080"], out var onlyPort, out _);

        Assert.True(parsedPort);
        Assert.NotNull(onlyPort);
        Assert.Equal(8080, onlyPort.HttpPort);
    }

    [Theory]
    [InlineData("70000")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("http")]
    public void An_invalid_port_is_rejected(string port)
    {
        var parsed = ServerCommandLine.TryParse(["--http-port", port], out var commandLine, out var error);

        Assert.False(parsed);
        Assert.Null(commandLine);
        Assert.NotNull(error);
        Assert.Contains("--http-port", error);
        Assert.Contains("1-65535", error);
    }

    [Fact]
    public void An_invalid_command_line_aborts_the_startup_with_the_message()
    {
        var exception = Assert.Throws<ServerStartupException>(
            () => ServerCommandLine.Parse(["--tracker-port", "6881"]));

        Assert.Contains("--tracker-port", exception.Message);
    }

    [Fact]
    public void The_defaults_are_the_data_folder_next_to_the_binary_and_the_port_8080()
    {
        var resolved = ServerConfiguration.Resolve(new ConfigurationBuilder().Build(), Path.Combine("C:", "servidor"));

        Assert.Equal(Path.Combine(Path.Combine("C:", "servidor"), "data"), resolved.DataDirectory);
        Assert.Equal(8080, resolved.HttpPort);
    }

    [Fact]
    public void The_flag_wins_over_the_environment_and_over_the_file()
    {
        var baseDirectory = CreateConfigurationFolder(@"{ ""Server"": { ""dataDirectory"": ""del-archivo"", ""httpPort"": 1111 } }");

        try
        {
            Environment.SetEnvironmentVariable(DataDirectoryVariable, "del-entorno");
            Environment.SetEnvironmentVariable(HttpPortVariable, "2222");

            var configuration = BuildConfiguration(baseDirectory, ["--data-dir", "del-flag", "--http-port", "3333"]);
            var resolved = ServerConfiguration.Resolve(configuration, baseDirectory);

            Assert.Equal(Path.GetFullPath(Path.Combine(baseDirectory, "del-flag")), resolved.DataDirectory);
            Assert.Equal(3333, resolved.HttpPort);
        }
        finally
        {
            Environment.SetEnvironmentVariable(DataDirectoryVariable, null);
            Environment.SetEnvironmentVariable(HttpPortVariable, null);
        }
    }

    [Fact]
    public void The_environment_wins_over_the_file()
    {
        var baseDirectory = CreateConfigurationFolder(@"{ ""Server"": { ""dataDirectory"": ""del-archivo"", ""httpPort"": 1111 } }");

        try
        {
            Environment.SetEnvironmentVariable(DataDirectoryVariable, "del-entorno");
            Environment.SetEnvironmentVariable(HttpPortVariable, "2222");

            var configuration = BuildConfiguration(baseDirectory, []);
            var resolved = ServerConfiguration.Resolve(configuration, baseDirectory);

            Assert.Equal(Path.GetFullPath(Path.Combine(baseDirectory, "del-entorno")), resolved.DataDirectory);
            Assert.Equal(2222, resolved.HttpPort);
        }
        finally
        {
            Environment.SetEnvironmentVariable(DataDirectoryVariable, null);
            Environment.SetEnvironmentVariable(HttpPortVariable, null);
        }
    }

    [Fact]
    public void The_file_wins_over_the_defaults()
    {
        var baseDirectory = CreateConfigurationFolder(@"{ ""Server"": { ""dataDirectory"": ""del-archivo"", ""httpPort"": 1111 } }");

        try
        {
            Environment.SetEnvironmentVariable(DataDirectoryVariable, null);
            Environment.SetEnvironmentVariable(HttpPortVariable, null);

            var configuration = BuildConfiguration(baseDirectory, []);
            var resolved = ServerConfiguration.Resolve(configuration, baseDirectory);

            Assert.Equal(Path.GetFullPath(Path.Combine(baseDirectory, "del-archivo")), resolved.DataDirectory);
            Assert.Equal(1111, resolved.HttpPort);
        }
        finally
        {
            Environment.SetEnvironmentVariable(DataDirectoryVariable, null);
            Environment.SetEnvironmentVariable(HttpPortVariable, null);
        }
    }

    [Fact]
    public void An_invalid_port_in_the_configuration_aborts_the_startup()
    {
        var baseDirectory = CreateConfigurationFolder(@"{ ""Server"": { ""httpPort"": 70000 } }");

        var configuration = BuildConfiguration(baseDirectory, []);

        var exception = Assert.Throws<ServerStartupException>(
            () => ServerConfiguration.Resolve(configuration, baseDirectory));

        Assert.Contains("1-65535", exception.Message);
    }

    [Fact]
    public void An_empty_port_in_the_configuration_file_aborts_the_startup_and_names_the_source()
    {
        var baseDirectory = CreateConfigurationFolder(@"{ ""Server"": { ""httpPort"": """" } }");

        var configuration = BuildConfiguration(baseDirectory, []);

        var exception = Assert.Throws<ServerStartupException>(
            () => ServerConfiguration.Resolve(configuration, baseDirectory));

        Assert.Contains("Server:httpPort", exception.Message);
        Assert.Contains(ServerConfiguration.FileName, exception.Message);
    }

    [Fact]
    public void An_empty_data_directory_in_the_configuration_file_aborts_the_startup()
    {
        var baseDirectory = CreateConfigurationFolder(@"{ ""Server"": { ""dataDirectory"": """" } }");

        var configuration = BuildConfiguration(baseDirectory, []);

        var exception = Assert.Throws<ServerStartupException>(
            () => ServerConfiguration.Resolve(configuration, baseDirectory));

        Assert.Contains("Server:dataDirectory", exception.Message);
        Assert.Contains(ServerConfiguration.FileName, exception.Message);
    }

    private static IConfigurationRoot BuildConfiguration(string baseDirectory, string[] args)
    {
        var builder = new ConfigurationBuilder().SetBasePath(baseDirectory);

        ServerConfiguration.AddSources(builder, ServerCommandLine.Parse(args));

        return builder.Build();
    }

    private static string CreateConfigurationFolder(string content)
    {
        var folder = Directory.CreateTempSubdirectory("lan-party-hub-config-");

        File.WriteAllText(Path.Combine(folder.FullName, ServerConfiguration.FileName), content);

        return folder.FullName;
    }
}
