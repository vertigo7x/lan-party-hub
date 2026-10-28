using System.Collections.Concurrent;

namespace LANPartyHub.Server.Host;

/// <summary>
/// Metrics of the host (AD-15). The values are instrumented by Story 1.4; the shape of the
/// snapshot is fixed here so the endpoint does not change when the instrumentation arrives.
/// </summary>
public sealed class HostMetrics
{
    private readonly ConcurrentDictionary<string, long> _errorsByCode = new(StringComparer.Ordinal);

    private long _transferRateBytesPerSecond;
    private int _activeOperations;

    /// <summary>Current transfer rate, in bytes per second.</summary>
    public long TransferRateBytesPerSecond => Interlocked.Read(ref _transferRateBytesPerSecond);

    /// <summary>Number of operations that are running right now.</summary>
    public int ActiveOperations => Volatile.Read(ref _activeOperations);

    /// <summary>Errors counted per stable code of the error catalog, as a defensive copy.</summary>
    public IReadOnlyDictionary<string, long> ErrorsByCode =>
        new Dictionary<string, long>(_errorsByCode, StringComparer.Ordinal);

    /// <summary>Publishes the current transfer rate, in bytes per second.</summary>
    /// <param name="bytesPerSecond">Measured transfer rate.</param>
    public void SetTransferRateBytesPerSecond(long bytesPerSecond)
    {
        Interlocked.Exchange(ref _transferRateBytesPerSecond, bytesPerSecond);
    }

    /// <summary>Publishes the number of operations that are running right now.</summary>
    /// <param name="activeOperations">Number of running operations.</param>
    public void SetActiveOperations(int activeOperations)
    {
        Volatile.Write(ref _activeOperations, activeOperations);
    }

    /// <summary>Counts one error under its stable code.</summary>
    /// <param name="code">Stable code of the error catalog.</param>
    public void RecordError(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return;
        }

        _errorsByCode.AddOrUpdate(code, 1, static (_, current) => current + 1);
    }

    /// <summary>Takes a snapshot of the metrics.</summary>
    /// <returns>The snapshot that the metrics endpoint publishes.</returns>
    public HostMetricsSnapshot GetSnapshot() => new(
        TransferRateBytesPerSecond,
        ActiveOperations,
        new Dictionary<string, long>(_errorsByCode, StringComparer.Ordinal));
}
