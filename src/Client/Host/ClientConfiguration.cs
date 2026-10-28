using LANPartyHub.Client.Application;
using LANPartyHub.Client.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Configuration.Memory;

namespace LANPartyHub.Client.Host;

/// <summary>
/// Effective configuration of the client (AD-12, AR-23).
///
/// <para>
/// The three sources are registered in the order <c>file &gt; env &gt; flags</c> and the last
/// provider that holds the key wins, so the flags of the command line have the highest
/// priority, the environment variables come next and the configuration file that sits next to
/// the binary has the lowest one.
/// </para>
/// <para>
/// The environment variables use <c>__</c> as the separator of a section, so the key
/// <c>Client:installDirectory</c> is written as the variable <c>Client__installDirectory</c>.
/// </para>
/// </summary>
public sealed class ClientConfiguration
{
    /// <summary>Section of the configuration that holds the settings of the client.</summary>
    public const string SectionName = "Client";

    /// <summary>Key of the address of the event server, inside <see cref="SectionName"/>.</summary>
    public const string ServerAddressKey = "serverAddress";

    /// <summary>Key of the installation root, inside <see cref="SectionName"/>.</summary>
    public const string InstallDirectoryKey = "installDirectory";

    /// <summary>Key of the theme of the interface, inside <see cref="SectionName"/>.</summary>
    public const string ThemeKey = "theme";

    /// <summary>Theme used when no source fixes it.</summary>
    public const string DefaultTheme = "System";

    /// <summary>
    /// Name of the optional configuration file that sits next to the binary. It is named
    /// differently from the file of the server because both projects land in the same output
    /// folder of the test projects that reference them, and one would replace the other.
    /// </summary>
    public const string FileName = "client.appsettings.json";

    private ClientConfiguration(string dataFolder, string? serverAddress, string installDirectory, string theme)
    {
        DataFolder = dataFolder;
        ServerAddress = serverAddress;
        InstallDirectory = installDirectory;
        Theme = theme;
    }

    /// <summary>
    /// Absolute path of the data folder of the client. It is <c>%LOCALAPPDATA%\LanPartyHub</c>
    /// and it is not a configurable key: AD-17 fixes it. The tests inject another one.
    /// </summary>
    public string DataFolder { get; }

    /// <summary>
    /// Address of the event server, or <c>null</c> when no source fixes it. The server is
    /// reached through the discovery or through the address that the player types, and both
    /// are equivalent (AD-3).
    /// </summary>
    public string? ServerAddress { get; }

    /// <summary>
    /// Absolute path of the installation root. It is the only directory that the rule of
    /// extraction confines (AD-9).
    /// </summary>
    public string InstallDirectory { get; }

    /// <summary>Theme of the interface of the client.</summary>
    public string Theme { get; }

    /// <summary>
    /// Registers the three configuration sources, in the order <c>file &gt; env &gt; flags</c>.
    /// </summary>
    /// <param name="builder">Builder that receives the sources.</param>
    /// <param name="commandLine">Command line of the client, the source of the highest priority.</param>
    public static void AddSources(IConfigurationBuilder builder, ClientCommandLine commandLine)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(commandLine);

        builder.AddJsonFile(FileName, optional: true, reloadOnChange: false);
        builder.AddEnvironmentVariables();
        builder.AddInMemoryCollection(commandLine.ToConfigurationValues());
    }

    /// <summary>
    /// Reads the effective configuration out of the sources of the deployment.
    /// </summary>
    /// <param name="baseDirectory">Folder of the deployment: it holds the configuration file.</param>
    /// <param name="dataFolder">Absolute path of the data folder of the client.</param>
    /// <param name="commandLine">Command line of the client, the source of the highest priority.</param>
    /// <returns>The effective configuration.</returns>
    /// <exception cref="ClientStartupException">The configuration file is not valid.</exception>
    public static ClientConfiguration Resolve(
        string baseDirectory,
        string dataFolder,
        ClientCommandLine commandLine)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        ArgumentNullException.ThrowIfNull(commandLine);

        try
        {
            var builder = new ConfigurationBuilder().SetBasePath(baseDirectory);

            AddSources(builder, commandLine);

            return Resolve(builder.Build(), dataFolder);
        }
        catch (Exception exception) when (exception is FormatException or InvalidDataException)
        {
            // The provider of the file reports a malformed file with a message of the
            // framework, so it is translated into one that says what to do.
            throw new ClientStartupException(
                $"El fichero de configuración '{FileName}' no es válido: {exception.Message} "
                + "Corrige el JSON del fichero o bórralo para arrancar con los valores por defecto.",
                exception);
        }
    }

    /// <summary>
    /// Reads the effective configuration out of the registered sources.
    /// </summary>
    /// <param name="configuration">Configuration with the three sources already registered.</param>
    /// <param name="dataFolder">Absolute path of the data folder of the client.</param>
    /// <returns>The effective configuration.</returns>
    /// <exception cref="ClientStartupException">A source fixes an invalid value.</exception>
    public static ClientConfiguration Resolve(IConfiguration configuration, string dataFolder)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataFolder);

        var section = configuration.GetSection(SectionName);

        var serverAddress = ResolveServerAddress(configuration, section);
        var installDirectory = ResolveInstallDirectory(configuration, section, dataFolder);
        var theme = ResolveTheme(configuration, section);

        return new ClientConfiguration(Path.GetFullPath(dataFolder), serverAddress, installDirectory, theme);
    }

    /// <summary>
    /// Projects the effective configuration onto the options of the startup of the client.
    /// </summary>
    /// <returns>The options of the startup.</returns>
    public ClientBootstrapOptions ToBootstrapOptions() => new()
    {
        ServerAddress = ServerAddress,
        Theme = Theme,
    };

    private static string? ResolveServerAddress(IConfiguration configuration, IConfigurationSection section)
    {
        var rawAddress = section[ServerAddressKey];

        if (rawAddress is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(rawAddress))
        {
            throw new ClientStartupException(
                $"Valor vacío en {DescribeSource(configuration, $"{SectionName}:{ServerAddressKey}")}: la dirección del servidor no puede estar vacía.");
        }

        if (!ClientCommandLine.TryParseServerAddress(rawAddress.Trim(), out _, out _, out var error))
        {
            throw new ClientStartupException(
                $"Valor no válido '{rawAddress}' en {DescribeSource(configuration, $"{SectionName}:{ServerAddressKey}")}: {error}");
        }

        return rawAddress.Trim();
    }

    private static string ResolveInstallDirectory(
        IConfiguration configuration,
        IConfigurationSection section,
        string dataFolder)
    {
        var rawDirectory = section[InstallDirectoryKey];

        if (rawDirectory is not null && string.IsNullOrWhiteSpace(rawDirectory))
        {
            throw new ClientStartupException(
                $"Valor vacío en {DescribeSource(configuration, $"{SectionName}:{InstallDirectoryKey}")}: el directorio de instalación no puede estar vacío.");
        }

        if (rawDirectory is null)
        {
            // The default installation root lives inside the data folder of the client
            // (AD-9, AD-17).
            return Path.GetFullPath(Path.Combine(dataFolder, ClientDataLayout.InstallFolderName));
        }

        var directory = rawDirectory.Trim();

        return Path.IsPathRooted(directory)
            ? Path.GetFullPath(directory)
            : Path.GetFullPath(Path.Combine(dataFolder, directory));
    }

    private static string ResolveTheme(IConfiguration configuration, IConfigurationSection section)
    {
        var rawTheme = section[ThemeKey];

        if (rawTheme is null)
        {
            return DefaultTheme;
        }

        if (string.IsNullOrWhiteSpace(rawTheme))
        {
            throw new ClientStartupException(
                $"Valor vacío en {DescribeSource(configuration, $"{SectionName}:{ThemeKey}")}: el tema no puede estar vacío.");
        }

        return rawTheme.Trim();
    }

    /// <summary>
    /// Names the source that holds the value of a key, so the message of a rejected value says
    /// where the player has to fix it.
    /// </summary>
    private static string DescribeSource(IConfiguration configuration, string key)
    {
        if (configuration is IConfigurationRoot root)
        {
            var providers = root.Providers.ToArray();

            // The providers are registered in the order file > env > flags and the last one
            // that holds the key wins, so the source that is reported is the last one that
            // holds it and not the first one.
            for (var index = providers.Length - 1; index >= 0; index--)
            {
                var provider = providers[index];

                if (!provider.TryGet(key, out var value) || value is null)
                {
                    continue;
                }

                return provider switch
                {
                    EnvironmentVariablesConfigurationProvider =>
                        $"la variable de entorno '{key.Replace(":", "__", StringComparison.Ordinal)}'",
                    JsonConfigurationProvider =>
                        $"la clave '{key}' del fichero '{FileName}'",
                    MemoryConfigurationProvider =>
                        "la línea de comandos",
                    _ =>
                        $"la clave '{key}' de una fuente de configuración",
                };
            }
        }

        return $"la configuración '{key}'";
    }
}
