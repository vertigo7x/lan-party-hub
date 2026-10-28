using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Client.Infrastructure.Logging;

/// <summary>
/// Provider that writes the log of the client in a JSON file next to the binary (AD-15).
///
/// <para>
/// The client is a desktop application and it has no console when the player launches it
/// from the desktop, so the log that the console provider writes is discarded exactly
/// where it is needed. This provider writes the same entries, in the same JSON shape, in a
/// file that lives next to the deployment.
/// </para>
/// <para>
/// The file cannot grow without bound: when it reaches its maximum size it is rotated and
/// the previous one is kept with the suffix <c>.1</c>, so the space that the log takes on
/// the disk is bounded.
/// </para>
/// </summary>
public sealed class JsonFileLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    /// <summary>Name of the file of the log, next to the deployment.</summary>
    public const string FileName = "client.log";

    /// <summary>Maximum size of the file of the log, in bytes.</summary>
    public const long DefaultMaximumSizeInBytes = 1_048_576;

    private readonly string _path;
    private readonly long _maximumSize;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _lock = new();

    private IExternalScopeProvider? _scopeProvider;

    /// <summary>Creates the provider of the file of the log.</summary>
    /// <param name="path">Path of the file of the log.</param>
    /// <param name="maximumSizeInBytes">
    /// Maximum size of the file, in bytes. The file is rotated when it reaches it.
    /// </param>
    /// <param name="timeProvider">Clock of the entries. It defaults to the clock of the system.</param>
    public JsonFileLoggerProvider(
        string path,
        long maximumSizeInBytes = DefaultMaximumSizeInBytes,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (maximumSizeInBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumSizeInBytes),
                maximumSizeInBytes,
                "El tamaño máximo del log tiene que ser mayor que cero.");
        }

        _path = path;
        _maximumSize = maximumSizeInBytes;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    /// <inheritdoc />
    public void SetScopeProvider(IExternalScopeProvider scopeProvider)
    {
        ArgumentNullException.ThrowIfNull(scopeProvider);

        _scopeProvider = scopeProvider;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // There is nothing to close: every entry opens and closes its own file.
    }

    /// <summary>
    /// Writes one entry of the log in the file.
    /// </summary>
    /// <param name="category">Category of the entry.</param>
    /// <param name="level">Level of the entry.</param>
    /// <param name="eventId">Identifier of the event of the entry.</param>
    /// <param name="message">Text of the entry.</param>
    /// <param name="exception">Exception of the entry, or <c>null</c>.</param>
    /// <param name="state">Structured data of the entry, or <c>null</c>.</param>
    internal void Write(
        string category,
        LogLevel level,
        EventId eventId,
        string message,
        Exception? exception,
        IReadOnlyList<KeyValuePair<string, object?>>? state)
    {
        var entry = new JsonObject
        {
            ["Timestamp"] = _timeProvider.GetUtcNow().ToString("O", CultureInfo.InvariantCulture),
            ["EventId"] = eventId.Id,
            ["LogLevel"] = level.ToString(),
            ["Category"] = category,
            ["Message"] = message,
        };

        if (state is not null)
        {
            entry["State"] = ToJsonObject(state);
        }

        var scopes = BuildScopes();

        if (scopes is not null)
        {
            entry["Scopes"] = scopes;
        }

        if (exception is not null)
        {
            entry["Exception"] = exception.ToString();
        }

        WriteLine(entry.ToJsonString());
    }

    /// <summary>
    /// Builds the scopes that are open, in the same shape that the console provider
    /// publishes.
    /// </summary>
    private JsonArray? BuildScopes()
    {
        if (_scopeProvider is null)
        {
            return null;
        }

        var scopes = new JsonArray();

        _scopeProvider.ForEachScope(
            (scope, _) =>
            {
                scopes.Add(ToJson(scope));
            },
            0);

        return scopes;
    }

    /// <summary>
    /// Converts the structured data of an entry into a JSON object.
    /// </summary>
    private static JsonObject ToJsonObject(IReadOnlyList<KeyValuePair<string, object?>> fields)
    {
        var node = new JsonObject();

        foreach (var field in fields)
        {
            node[field.Key] = ToJsonValue(field.Value);
        }

        return node;
    }

    /// <summary>
    /// Converts a scope into a JSON node: an object when it carries fields, and its text
    /// when it does not.
    /// </summary>
    private static JsonNode? ToJson(object? scope) => scope switch
    {
        null => null,
        IReadOnlyList<KeyValuePair<string, object?>> fields => ToJsonObject(fields),
        _ => JsonValue.Create(scope.ToString()),
    };

    /// <summary>
    /// Converts the value of a field into a JSON node.
    /// </summary>
    private static JsonNode? ToJsonValue(object? value) => value switch
    {
        null => null,
        string text => JsonValue.Create(text),
        bool flag => JsonValue.Create(flag),
        int number => JsonValue.Create(number),
        long number => JsonValue.Create(number),
        double number => JsonValue.Create(number),
        decimal number => JsonValue.Create(number),
        _ => JsonValue.Create(value.ToString()),
    };

    /// <summary>
    /// Writes one line of the log in the file, rotating it when it is full.
    /// </summary>
    private void WriteLine(string line)
    {
        var text = line + Environment.NewLine;

        lock (_lock)
        {
            try
            {
                RotateWhenFull(Encoding.UTF8.GetByteCount(text));

                File.AppendAllText(_path, text, Encoding.UTF8);
            }
            catch (IOException)
            {
                // The log is a diagnostic and not a duty: a file that cannot be written
                // never stops the client.
            }
            catch (UnauthorizedAccessException)
            {
                // The log is a diagnostic and not a duty.
            }
        }
    }

    /// <summary>
    /// Rotates the file when the line does not fit in it. The previous file is kept with
    /// the suffix <c>.1</c>, so the space that the log takes on the disk is bounded.
    /// </summary>
    /// <param name="size">Size of the line that is about to be written.</param>
    private void RotateWhenFull(int size)
    {
        var info = new FileInfo(_path);

        if (!info.Exists || info.Length + size <= _maximumSize)
        {
            return;
        }

        var rotated = _path + ".1";

        try
        {
            File.Move(_path, rotated, overwrite: true);
        }
        catch (IOException)
        {
            // The file cannot be rotated, so the new line is appended to the current one.
        }
        catch (UnauthorizedAccessException)
        {
            // The file cannot be rotated, so the new line is appended to the current one.
        }
    }

    /// <summary>
    /// Logger that writes in the file of the provider.
    /// </summary>
    private sealed class FileLogger : ILogger
    {
        private readonly JsonFileLoggerProvider _provider;
        private readonly string _category;

        public FileLogger(JsonFileLoggerProvider provider, string category)
        {
            _provider = provider;
            _category = category;
        }

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull =>
            _provider._scopeProvider?.Push(state) ?? NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            _provider.Write(
                _category,
                logLevel,
                eventId,
                formatter(state, exception),
                exception,
                state as IReadOnlyList<KeyValuePair<string, object?>>);
        }
    }

    /// <summary>
    /// Scope that does nothing. It exists because the log of a process without a scope
    /// provider cannot open one.
    /// </summary>
    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
            // There is nothing to close.
        }
    }
}
