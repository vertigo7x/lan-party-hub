using LANPartyHub.Client.Application.Connection;

namespace LANPartyHub.Client.Host;

/// <summary>
/// Strict parser of the command line of the client (AD-12, AR-23).
///
/// <para>
/// The standard command line provider is not used: it ignores unknown options silently and
/// swallows a value that looks like another option. A mistyped flag must be loud, so the
/// parsing is strict and its result enters the configuration as the source of the highest
/// priority (flags &gt; env &gt; file).
/// </para>
/// <para>
/// The client declares exactly three options: <c>--server-address</c>,
/// <c>--install-directory</c> and <c>--theme</c>. There is no flag for a transfer engine,
/// because the MVP has a single one (AD-4, AD-28).
/// </para>
/// </summary>
public sealed class ClientCommandLine
{
    /// <summary>Option that fixes the address of the event server.</summary>
    public const string ServerAddressOption = "--server-address";

    /// <summary>Option that fixes the installation root of the client.</summary>
    public const string InstallDirectoryOption = "--install-directory";

    /// <summary>Option that fixes the theme of the interface.</summary>
    public const string ThemeOption = "--theme";

    /// <summary>
    /// Expected format of the address of the event server. The host and the port travel
    /// apart in the contract <c>ServerEndpoint</c> of <c>Shared</c>, and the command line
    /// carries them together in this form.
    /// </summary>
    public const string ServerAddressFormat = ServerEndpointParser.ServerAddressFormat;

    /// <summary>Lowest valid TCP port.</summary>
    public const int MinimumPort = ServerEndpointParser.MinimumPort;

    /// <summary>Highest valid TCP port.</summary>
    public const int MaximumPort = ServerEndpointParser.MaximumPort;

    private static readonly string[] SupportedOptions = [ServerAddressOption, InstallDirectoryOption, ThemeOption];

    private static readonly IReadOnlyList<string> ReadOnlySupportedOptions = Array.AsReadOnly(SupportedOptions);

    private ClientCommandLine(string? serverAddress, string? installDirectory, string? theme)
    {
        ServerAddress = serverAddress;
        InstallDirectory = installDirectory;
        Theme = theme;
    }

    /// <summary>Value of <c>--server-address</c>, or <c>null</c> when the option was not used.</summary>
    public string? ServerAddress { get; }

    /// <summary>Value of <c>--install-directory</c>, or <c>null</c> when the option was not used.</summary>
    public string? InstallDirectory { get; }

    /// <summary>Value of <c>--theme</c>, or <c>null</c> when the option was not used.</summary>
    public string? Theme { get; }

    /// <summary>The options the client accepts, in the order they are shown to the player.</summary>
    public static IReadOnlyList<string> Options => ReadOnlySupportedOptions;

    /// <summary>
    /// Projects the parsed options onto the keys of the configuration section of the client, so
    /// the command line can be registered as the source of the highest priority.
    /// </summary>
    /// <returns>The values of the options that were used.</returns>
    public IEnumerable<KeyValuePair<string, string?>> ToConfigurationValues()
    {
        if (ServerAddress is not null)
        {
            yield return new KeyValuePair<string, string?>(
                $"{ClientConfiguration.SectionName}:{ClientConfiguration.ServerAddressKey}",
                ServerAddress);
        }

        if (InstallDirectory is not null)
        {
            yield return new KeyValuePair<string, string?>(
                $"{ClientConfiguration.SectionName}:{ClientConfiguration.InstallDirectoryKey}",
                InstallDirectory);
        }

        if (Theme is not null)
        {
            yield return new KeyValuePair<string, string?>(
                $"{ClientConfiguration.SectionName}:{ClientConfiguration.ThemeKey}",
                Theme);
        }
    }

    /// <summary>
    /// Parses the arguments of the client.
    /// </summary>
    /// <param name="args">Arguments of the process.</param>
    /// <param name="commandLine">The parsed command line, or <c>null</c> when the parsing failed.</param>
    /// <param name="error">Message that explains the failure, or <c>null</c> when it succeeded.</param>
    /// <returns><c>true</c> when the arguments are valid.</returns>
    public static bool TryParse(
        IReadOnlyList<string> args,
        out ClientCommandLine? commandLine,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(args);

        string? serverAddress = null;
        string? installDirectory = null;
        string? theme = null;

        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];

            if (argument == ServerAddressOption)
            {
                if (!TryReadValue(args, ref index, ServerAddressOption, out var value, out error))
                {
                    commandLine = null;
                    return false;
                }

                if (!TryParseServerAddress(value, out _, out _, out error))
                {
                    commandLine = null;
                    return false;
                }

                serverAddress = value;
                continue;
            }

            if (argument == InstallDirectoryOption)
            {
                if (!TryReadValue(args, ref index, InstallDirectoryOption, out var value, out error))
                {
                    commandLine = null;
                    return false;
                }

                installDirectory = value;
                continue;
            }

            if (argument == ThemeOption)
            {
                if (!TryReadValue(args, ref index, ThemeOption, out var value, out error))
                {
                    commandLine = null;
                    return false;
                }

                theme = value;
                continue;
            }

            commandLine = null;
            error = $"Opción no admitida '{argument}'. Las opciones admitidas son: {string.Join(", ", SupportedOptions)}.";
            return false;
        }

        commandLine = new ClientCommandLine(serverAddress, installDirectory, theme);
        error = null;
        return true;
    }

    /// <summary>
    /// Parses the arguments of the client and aborts the startup when they are invalid.
    /// </summary>
    /// <param name="args">Arguments of the process.</param>
    /// <returns>The parsed command line.</returns>
    /// <exception cref="ClientStartupException">The arguments are invalid.</exception>
    public static ClientCommandLine Parse(IReadOnlyList<string> args)
    {
        if (!TryParse(args, out var commandLine, out var error))
        {
            throw new ClientStartupException(error!);
        }

        return commandLine!;
    }

    /// <summary>
    /// Parses the address of the event server. The accepted format is
    /// <c>host:puerto</c>: the host is not empty and the port is an integer in the range
    /// 1-65535.
    ///
    /// <para>
    /// The rule is the ONE of the Application layer: the flag, the file of configuration
    /// and the address that the player types mean the same thing (AC1, AD-3).
    /// </para>
    /// </summary>
    /// <param name="value">Value of the address.</param>
    /// <param name="host">Host of the server.</param>
    /// <param name="port">TCP port of the server.</param>
    /// <param name="error">Message that explains the failure, or <c>null</c> when it succeeded.</param>
    /// <returns><c>true</c> when the address is valid.</returns>
    public static bool TryParseServerAddress(
        string? value,
        out string host,
        out int port,
        out string? error)
    {
        host = string.Empty;
        port = 0;

        if (!ServerEndpointParser.TryParse(value, out var endpoint, out _))
        {
            error = DescribeInvalidServerAddress(value);
            return false;
        }

        host = endpoint!.Address;
        port = endpoint.Port;
        error = null;
        return true;
    }

    /// <summary>
    /// Explains why the address of the server was rejected, naming the format that the
    /// player has to write.
    /// </summary>
    /// <param name="value">Value of the address.</param>
    /// <returns>The message that explains the failure.</returns>
    private static string DescribeInvalidServerAddress(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return $"Valor vacío para la dirección del servidor: el formato esperado es '{ServerAddressFormat}'.";
        }

        var separator = value.LastIndexOf(':');

        if (separator <= 0)
        {
            return $"Valor no válido '{value}' para la dirección del servidor: el formato esperado es '{ServerAddressFormat}'.";
        }

        var hostPart = value[..separator];

        if (hostPart.Length == 0 || hostPart.Any(char.IsWhiteSpace))
        {
            return $"Valor no válido '{value}' para la dirección del servidor: el host no puede estar vacío y el formato esperado es '{ServerAddressFormat}'.";
        }

        return $"Valor no válido '{value}' para la dirección del servidor: el puerto debe ser un número entero en el rango {MinimumPort}-{MaximumPort} y el formato esperado es '{ServerAddressFormat}'.";
    }

    private static bool TryReadValue(
        IReadOnlyList<string> args,
        ref int index,
        string option,
        out string value,
        out string? error)
    {
        value = string.Empty;

        if (index + 1 >= args.Count)
        {
            error = $"Missing value: la opción '{option}' requiere un valor.";
            return false;
        }

        var candidate = args[index + 1];

        if (candidate.StartsWith("--", StringComparison.Ordinal))
        {
            error = $"Missing value: la opción '{option}' requiere un valor y '{candidate}' es otra opción, no un valor.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(candidate))
        {
            error = $"Missing value: la opción '{option}' requiere un valor.";
            return false;
        }

        index++;
        value = candidate;
        error = null;
        return true;
    }
}
