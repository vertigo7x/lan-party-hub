using System.Globalization;

namespace LANPartyHub.Server.Host;

/// <summary>
/// Strict parser of the command line of the event server (AD-12, AR-23).
///
/// The standard command line provider is not used: it ignores unknown options silently and
/// swallows a value that looks like another option. A mistyped flag during an event must be
/// loud, so the parsing is strict and its result enters the configuration as the source of
/// the highest priority (flags &gt; env &gt; file).
///
/// The server declares exactly two options: <c>--data-dir</c> and <c>--http-port</c>. There is
/// no flag for a transfer engine, because the MVP has a single one (AD-4, AD-28).
/// </summary>
public sealed class ServerCommandLine
{
    /// <summary>Option that fixes the data folder of the server.</summary>
    public const string DataDirectoryOption = "--data-dir";

    /// <summary>Option that fixes the TCP port of the HTTP surface (API and SignalR).</summary>
    public const string HttpPortOption = "--http-port";

    /// <summary>Lowest valid TCP port.</summary>
    public const int MinimumPort = 1;

    /// <summary>Highest valid TCP port.</summary>
    public const int MaximumPort = 65535;

    private static readonly string[] SupportedOptions = [DataDirectoryOption, HttpPortOption];

    private static readonly IReadOnlyList<string> ReadOnlySupportedOptions = Array.AsReadOnly(SupportedOptions);

    private ServerCommandLine(string? dataDirectory, int? httpPort)
    {
        DataDirectory = dataDirectory;
        HttpPort = httpPort;
    }

    /// <summary>Value of <c>--data-dir</c>, or <c>null</c> when the option was not used.</summary>
    public string? DataDirectory { get; }

    /// <summary>Value of <c>--http-port</c>, or <c>null</c> when the option was not used.</summary>
    public int? HttpPort { get; }

    /// <summary>The options the server accepts, in the order they are shown to the organizer.</summary>
    public static IReadOnlyList<string> Options => ReadOnlySupportedOptions;

    /// <summary>
    /// Projects the parsed options onto the keys of the configuration section of the server,
    /// so the command line can be registered as the source of the highest priority.
    /// </summary>
    /// <returns>The values of the options that were used.</returns>
    public IEnumerable<KeyValuePair<string, string?>> ToConfigurationValues()
    {
        if (DataDirectory is not null)
        {
            yield return new KeyValuePair<string, string?>(
                $"{ServerConfiguration.SectionName}:{ServerConfiguration.DataDirectoryKey}",
                DataDirectory);
        }

        if (HttpPort is not null)
        {
            yield return new KeyValuePair<string, string?>(
                $"{ServerConfiguration.SectionName}:{ServerConfiguration.HttpPortKey}",
                HttpPort.Value.ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    /// Parses the arguments of the server.
    /// </summary>
    /// <param name="args">Arguments of the process.</param>
    /// <param name="commandLine">The parsed command line, or <c>null</c> when the parsing failed.</param>
    /// <param name="error">Message that explains the failure, or <c>null</c> when it succeeded.</param>
    /// <returns><c>true</c> when the arguments are valid.</returns>
    public static bool TryParse(
        IReadOnlyList<string> args,
        out ServerCommandLine? commandLine,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(args);

        string? dataDirectory = null;
        int? httpPort = null;

        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];

            if (argument == DataDirectoryOption)
            {
                if (!TryReadValue(args, ref index, DataDirectoryOption, out var value, out error))
                {
                    commandLine = null;
                    return false;
                }

                dataDirectory = value;
                continue;
            }

            if (argument == HttpPortOption)
            {
                if (!TryReadValue(args, ref index, HttpPortOption, out var value, out error))
                {
                    commandLine = null;
                    return false;
                }

                if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var port)
                    || port < MinimumPort
                    || port > MaximumPort)
                {
                    commandLine = null;
                    error = $"Valor no válido '{value}' para la opción '{HttpPortOption}': el puerto debe ser un número entero en el rango {MinimumPort}-{MaximumPort}.";
                    return false;
                }

                httpPort = port;
                continue;
            }

            commandLine = null;
            error = $"Opción no admitida '{argument}'. Las opciones admitidas son: {string.Join(", ", SupportedOptions)}.";
            return false;
        }

        commandLine = new ServerCommandLine(dataDirectory, httpPort);
        error = null;
        return true;
    }

    /// <summary>
    /// Parses the arguments of the server and aborts the startup when they are invalid.
    /// </summary>
    /// <param name="args">Arguments of the process.</param>
    /// <returns>The parsed command line.</returns>
    /// <exception cref="ServerStartupException">The arguments are invalid.</exception>
    public static ServerCommandLine Parse(IReadOnlyList<string> args)
    {
        if (!TryParse(args, out var commandLine, out var error))
        {
            throw new ServerStartupException(error!);
        }

        return commandLine!;
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
