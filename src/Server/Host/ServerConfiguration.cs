using System.Globalization;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Configuration.Memory;

using LANPartyHub.Server.Domain.Packages;

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

    /// <summary>
    /// Key of the oldest protocol version that the server accepts, inside
    /// <see cref="SectionName"/>.
    /// </summary>
    public const string MinimumProtocolVersionKey = "minimumProtocolVersion";

    /// <summary>
    /// Key of the number of simultaneous range responses that the server admits per client,
    /// inside <see cref="SectionName"/> (AD-14).
    /// </summary>
    public const string MaxConcurrentDownloadsPerClientKey = "maxConcurrentDownloadsPerClient";

    /// <summary>Name of the data folder used when no source fixes it.</summary>
    public const string DefaultDataDirectory = "data";

    /// <summary>TCP port used when no source fixes it.</summary>
    public const int DefaultHttpPort = 8080;

    /// <summary>
    /// Number of simultaneous range responses that the server admits per client when no
    /// source fixes another one (AD-14).
    ///
    /// <para>
    /// El valor por defecto NO depende del resultado de ningún spike: el reparto
    /// equitativo del enlace es un límite de concurrencia, y el límite tiene un valor
    /// aunque la medición del ancho de banda real todavía no exista (AD-14, NFR-6).
    /// </para>
    /// </summary>
    public const int DefaultMaxConcurrentDownloadsPerClient = 2;

    /// <summary>Name of the optional configuration file that sits next to the binary.</summary>
    public const string FileName = "appsettings.json";

    private ServerConfiguration(
        string dataDirectory,
        int httpPort,
        int minimumProtocolVersion,
        int maxConcurrentDownloadsPerClient)
    {
        DataDirectory = dataDirectory;
        HttpPort = httpPort;
        MinimumProtocolVersion = minimumProtocolVersion;
        MaxConcurrentDownloadsPerClient = maxConcurrentDownloadsPerClient;
    }

    /// <summary>Absolute path of the data folder of the server.</summary>
    public string DataDirectory { get; }

    /// <summary>TCP port of the HTTP surface: the API and the SignalR hub (AD-5).</summary>
    public int HttpPort { get; }

    /// <summary>
    /// Oldest protocol version that the server accepts (AD-6).
    ///
    /// <para>
    /// It is the POLICY of the server: el rango mínimo que se publica es
    /// <c>[MinimumProtocolVersion, VersionInfo.CurrentProtocolVersion]</c>. When no source
    /// fixes it, it is the protocol version that the server speaks, so a server that does
    /// not configure anything accepts exactly the version that it speaks (AD-6).
    /// </para>
    /// </summary>
    public int MinimumProtocolVersion { get; }

    /// <summary>
    /// Number of simultaneous range responses that the server admits per client (AD-14).
    ///
    /// <para>
    /// Es el límite POR CLIENTE del reparto del enlace: todas las conexiones de un
    /// cliente beben del mismo cubo, así que un cliente que abre varias descargas
    /// simultáneas no se lleva la parte que le corresponde a cada una (AD-14).
    /// </para>
    /// </summary>
    public int MaxConcurrentDownloadsPerClient { get; }

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
        var minimumProtocolVersion = ResolveMinimumProtocolVersion(configuration, section);
        var maxConcurrentDownloadsPerClient = ResolveMaxConcurrentDownloadsPerClient(configuration, section);

        return new ServerConfiguration(
            dataDirectory,
            httpPort,
            minimumProtocolVersion,
            maxConcurrentDownloadsPerClient);
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

        // La ruta se reconoce como ABSOLUTA por su FORMA, no por la del host: la
        // escribe el operador o la escribe el archivo de configuración, y una ruta
        // de Windows es absoluta aunque la comprobación corra en otra plataforma
        // (AD-12, AD-17).
        return PathForm.IsAbsoluteByForm(directory)
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
    /// Resolves the oldest protocol version that the server accepts (AD-6).
    ///
    /// <para>
    /// The value is validated against the protocol version that the server speaks: a
    /// minimum that is HIGHER than the current version would declare a range that is
    /// empty, and no client would ever be compatible. The startup rejects that value
    /// instead of accepting a server that rejects every client (AD-6).
    /// </para>
    /// </summary>
    /// <param name="configuration">Configuration with the three sources already registered.</param>
    /// <param name="section">Section of the configuration that holds the settings.</param>
    /// <returns>The oldest protocol version that the server accepts.</returns>
    /// <exception cref="ServerStartupException">A source fixes an invalid value.</exception>
    private static int ResolveMinimumProtocolVersion(
        IConfiguration configuration,
        IConfigurationSection section)
    {
        var rawMinimum = section[MinimumProtocolVersionKey];

        if (rawMinimum is null)
        {
            return VersionInfo.CurrentProtocolVersion;
        }

        if (string.IsNullOrWhiteSpace(rawMinimum))
        {
            throw new ServerStartupException(
                $"Valor vacío en {DescribeSource(configuration, $"{SectionName}:{MinimumProtocolVersionKey}")}: la versión mínima del protocolo no puede estar vacía.");
        }

        var candidate = rawMinimum.Trim();

        if (!int.TryParse(candidate, NumberStyles.None, CultureInfo.InvariantCulture, out var minimumProtocolVersion)
            || minimumProtocolVersion < 1)
        {
            throw new ServerStartupException(
                $"Valor no válido '{rawMinimum}' en {DescribeSource(configuration, $"{SectionName}:{MinimumProtocolVersionKey}")}: la versión mínima del protocolo debe ser un número entero mayor que cero.");
        }

        if (minimumProtocolVersion > VersionInfo.CurrentProtocolVersion)
        {
            throw new ServerStartupException(
                $"Valor no válido '{rawMinimum}' en {DescribeSource(configuration, $"{SectionName}:{MinimumProtocolVersionKey}")}: la versión mínima del protocolo no puede ser mayor que la versión que habla el servidor ({VersionInfo.CurrentProtocolVersion}), porque el rango quedaría vacío.");
        }

        return minimumProtocolVersion;
    }

    /// <summary>
    /// Resolves the number of simultaneous range responses that the server admits per
    /// client (AD-14).
    ///
    /// <para>
    /// El límite tiene que ser MAYOR que cero: un límite de cero no repartiría el enlace,
    /// lo bloquearía, y el valor por defecto se aplica cuando ninguna fuente fija otro
    /// (AD-14).
    /// </para>
    /// </summary>
    /// <param name="configuration">Configuration with the three sources already registered.</param>
    /// <param name="section">Section of the configuration that holds the settings.</param>
    /// <returns>The number of simultaneous range responses per client.</returns>
    /// <exception cref="ServerStartupException">A source fixes an invalid value.</exception>
    private static int ResolveMaxConcurrentDownloadsPerClient(
        IConfiguration configuration,
        IConfigurationSection section)
    {
        var rawLimit = section[MaxConcurrentDownloadsPerClientKey];

        if (rawLimit is null)
        {
            return DefaultMaxConcurrentDownloadsPerClient;
        }

        if (string.IsNullOrWhiteSpace(rawLimit))
        {
            throw new ServerStartupException(
                $"Valor vacío en {DescribeSource(configuration, $"{SectionName}:{MaxConcurrentDownloadsPerClientKey}")}: el límite de descargas por cliente no puede estar vacío.");
        }

        var candidate = rawLimit.Trim();

        if (!int.TryParse(candidate, NumberStyles.None, CultureInfo.InvariantCulture, out var limit)
            || limit < 1)
        {
            throw new ServerStartupException(
                $"Valor no válido '{rawLimit}' en {DescribeSource(configuration, $"{SectionName}:{MaxConcurrentDownloadsPerClientKey}")}: el límite de descargas por cliente debe ser un número entero mayor que cero.");
        }

        return limit;
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
