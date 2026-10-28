using System.Globalization;
using System.Text.Json;

namespace LANPartyHub.IntegrationTests;

/// <summary>
/// Captures the JSON log that the console provider writes (AD-15).
///
/// <para>
/// The capture replaces the standard output of the process while it is open, so the tests
/// read the entries that the product really writes and not a copy of them. The capture is
/// exclusive: the tests that read the log take turns, so two of them never read the same
/// output at the same time.
/// </para>
/// </summary>
internal sealed class ConsoleLogCapture : IAsyncDisposable
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly TextWriter _previous;
    private readonly StringWriter _buffer = new(CultureInfo.InvariantCulture);

    private ConsoleLogCapture(TextWriter previous)
    {
        _previous = previous;
    }

    /// <summary>
    /// Opens the capture of the log.
    /// </summary>
    /// <returns>The capture of the log.</returns>
    public static async Task<ConsoleLogCapture> StartAsync()
    {
        await Gate.WaitAsync().ConfigureAwait(false);

        try
        {
            var capture = new ConsoleLogCapture(Console.Out);

            Console.SetOut(capture._buffer);

            return capture;
        }
        catch
        {
            // The semaphore is released, so a capture that could not be opened does not
            // block every test that comes after it.
            Gate.Release();

            throw;
        }
    }

    /// <summary>
    /// Every entry of the log that has been written, parsed as JSON. A line that is not a
    /// JSON entry is not an entry of the log and it is ignored.
    /// </summary>
    public IReadOnlyList<JsonElement> Entries
    {
        get
        {
            var entries = new List<JsonElement>();

            foreach (var line in _buffer.ToString().Split('\n'))
            {
                var trimmed = line.Trim();

                if (trimmed.Length == 0)
                {
                    continue;
                }

                try
                {
                    using var document = JsonDocument.Parse(trimmed);

                    entries.Add(document.RootElement.Clone());
                }
                catch (JsonException)
                {
                    // The line is not an entry of the log.
                }
            }

            return entries;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Console.SetOut(_previous);

        Gate.Release();

        await Task.CompletedTask.ConfigureAwait(false);
    }
}
