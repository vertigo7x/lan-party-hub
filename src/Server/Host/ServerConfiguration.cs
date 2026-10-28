using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Configuration.Memory;

namespace LANPartyHub.Server.Host;

/// <summary>
/// Effective configuration of the event server (AD-12, AR-23).
///
/// The three sources are registered in the order <c>file &gt; env &gt; flags</c> and the last
/// provider that holds the key wins, so the flags of the command line have the highest
/// priority, the environment variables come next and the configuration file that sits next to
/// the binary has the lowest one.
///
/// The environment variables use <c>__</c> as the separator of a section, so the key
/// <c>Server:httpPort</c> is written as the variable <c>Server__httpPort</c>.
/// </summary>
public sealed class ServerConfiguration
{
    /// <summary>Section of the configuration that holds the settings of the server.</summary>
    public const string SectionName = "Server";

    /// <summary>Key of the data folder, inside <see cref="SectionName"/>.</summary>
    public const string DataDirectoryKey = "dataDirectory";

    /// <summary>Key of the HTTP port, inside <see cref="SectionName"/>.</summary>
    public const string HttpPortKey = "httpPort";

    /// <summary>Name of the data folder used when no source fixes it.</summary>
    public const string DefaultDataDirectory = "data";

    /// <summary>TCP port used when no source fixes it.</summary>
    public const int DefaultHttpPort = 8080;

    /// <summary>Name of the optional configuration file that sits next to the binary.</summary>
    public const string FileName = "appsettings.json";

    private ServerConfiguration(string dataDirectory, int httpPort)
    {
        DataDirectory = dataDirectory;
        HttpPort = httpPort;
    }

    /// <summary>Absolute path of the data folder of the server.</summary>
    public string DataDirectory { get; }

    /// <summary>TCP port of the HTTP surface: the API and the SignalR hub (AD-5).</summary>
    public int HttpPort { get; }

    /// <summary>
    /// Registers the three configuration sources, in the order <c>file &gt; env &gt; flags</c>.
    /// </summary>
    /// <param name="builder">Builder that receives the sources.</param>
    /// <param name="commandLine">Command line of the server, the source of the highest priority.</param>
    public static void AddSources(IConfigurationBuilder builder, ServerCommandLine commandLine)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(commandLine);

        builder.AddJsonFile(FileName, optional: true, reloadOnChange: false);
        builder.AddEnvironmentVariables();
        builder.AddInMemoryCollection(commandLine.ToConfigurationValues());
    }

    /// <summary>
    /// Reads the effective configuration out of the registered sources.
    /// </summary>
    /// <param name="configuration">Configuration with the three sources already registered.</param>
    /// <param name="baseDirectory">Folder the binary lives in; relative paths are resolved against it.</param>
    /// <returns>The effective configuration.</returns>
    /// <exception cref="ServerStartupException">A source fixes an invalid value.</exception>
    public static ServerConfiguration Resolve(IConfiguration configuration, string baseDirectory)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(baseDirectory);

        var section = configuration.GetSection(SectionName);

        var dataDirectory = ResolveDataDirectory(configuration, section, baseDirectory);
        var httpPort = ResolveHttpPort(configuration, section);

        return new ServerConfiguration(dataDirectory, httpPort);
    }

    private static string ResolveDataDirectory(
        IConfiguration configuration,
        IConfigurationSection section,
        string baseDirectory)
    {
        var rawDirectory = section[DataDirectoryKey];

        if (rawDirectory is not null && string.IsNullOrWhiteSpace(rawDirectory))
        {
            throw new ServerStartupException(
                $"Valor vacío en {DescribeSource(configuration, $"{SectionName}:{DataDirectoryKey}")}: la carpeta de datos no puede estar vacía.");
        }

        var directory = rawDirectory is null ? DefaultDataDirectory : rawDirectory.Trim();

        return Path.IsPathRooted(directory)
            ? Path.GetFullPath(directory)
            : Path.GetFullPath(Path.Combine(baseDirectory, directory));
    }

    private static int ResolveHttpPort(IConfiguration configuration, IConfigurationSection section)
    {
        var rawPort = section[HttpPortKey];

        if (rawPort is null)
        {
            return DefaultHttpPort;
        }

        if (string.IsNullOrWhiteSpace(rawPort))
        {
            throw new ServerStartupException(
                $"Valor vacío en {DescribeSource(configuration, $"{SectionName}:{HttpPortKey}")}: el puerto no puede estar vacío.");
        }

        var candidate = rawPort.Trim();

        if (!int.TryParse(candidate, NumberStyles.None, CultureInfo.InvariantCulture, out var httpPort)
            || httpPort < ServerCommandLine.MinimumPort
            || httpPort > ServerCommandLine.MaximumPort)
        {
            throw new ServerStartupException(
                $"Valor no válido '{rawPort}' en {DescribeSource(configuration, $"{SectionName}:{HttpPortKey}")}: el puerto debe ser un número entero en el rango {ServerCommandLine.MinimumPort}-{ServerCommandLine.MaximumPort}.");
        }

        return httpPort;
    }

    /// <summary>
    /// Names the source that holds the value of a key, so the message of a rejected value
    /// says where the organizer has to fix it.
    /// </summary>
    private static string DescribeSource(IConfiguration configuration, string key)
    {
        if (configuration is IConfigurationRoot root)
        {
            foreach (var provider in root.Providers)
            {
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
