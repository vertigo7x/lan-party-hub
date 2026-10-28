using System.Collections.Concurrent;
using LANPartyHub.Server.Application.Ports;

namespace LANPartyHub.Server.Host;

/// <summary>
/// Metrics of the host (AD-15).
///
/// <para>
/// The three metrics are instrumented, not published by hand: the transfer rate is
/// measured over a sliding window of the bytes that the host writes, the number of active
/// operations counts the units of work that are being served right now, and the errors are
/// counted per stable code of the error catalog.
/// </para>
/// <para>
/// The rate is the number of bytes written divided by the time that the window really
/// covers, so it decays on its own when the host stops transferring. The instants come
/// from a monotonic source, so a clock that moves backwards cannot keep the rate alive.
/// </para>
/// <para>
/// The values are published locally by the metrics endpoint of the host. Nothing leaves
/// the machine: no telemetry is sent to any external service (NFR-10).
/// </para>
/// </summary>
public sealed class HostMetrics : IMetricsCounters
{
    /// <summary>
    /// Length of the window over which the transfer rate is measured.
    /// </summary>
    public static readonly TimeSpan TransferRateWindow = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Shortest lapse of time that the rate can be measured over. Without it, the rate of
    /// the very first write would be divided by almost nothing.
    /// </summary>
    public static readonly TimeSpan MinimumTransferRateLapse = TimeSpan.FromMilliseconds(1);

    private readonly ConcurrentDictionary<string, long> _errorsByCode = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;
    private readonly Lock _rateLock = new();
    private readonly Queue<(long Timestamp, long Bytes)> _writtenBytes = new();

    private int _activeOperations;

    /// <summary>Creates the metrics of the host.</summary>
    /// <param name="timeProvider">
    /// Clock of the metrics. It defaults to the clock of the system and it exists so the
    /// window of the transfer rate can be exercised without waiting.
    /// </param>
    public HostMetrics(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Current transfer rate, in bytes per second: the bytes written inside the window
    /// divided by the time that the window really covers.
    /// </summary>
    public long TransferRateBytesPerSecond
    {
        get
        {
            lock (_rateLock)
            {
                var now = _timeProvider.GetTimestamp();

                DiscardExpiredSamples(now);

                if (_writtenBytes.Count == 0)
                {
                    return 0;
                }

                long total = 0;

                foreach (var sample in _writtenBytes)
                {
                    total += sample.Bytes;
                }

                var lapse = _timeProvider.GetElapsedTime(_writtenBytes.Peek().Timestamp, now);

                if (lapse < MinimumTransferRateLapse)
                {
                    lapse = MinimumTransferRateLapse;
                }

                return (long)(total / lapse.TotalSeconds);
            }
        }
    }

    /// <summary>Number of operations that are running right now.</summary>
    public int ActiveOperations => Volatile.Read(ref _activeOperations);

    /// <summary>Errors counted per stable code of the error catalog, as a defensive copy.</summary>
    public IReadOnlyDictionary<string, long> ErrorsByCode =>
        new Dictionary<string, long>(_errorsByCode, StringComparer.Ordinal);

    /// <summary>
    /// Counts the bytes that the host has written. The bytes feed the transfer rate, which
    /// is measured over the window of the metrics.
    /// </summary>
    /// <param name="bytes">Number of bytes that the host has written.</param>
    public void RecordBytesWritten(long bytes)
    {
        if (bytes <= 0)
        {
            return;
        }

        lock (_rateLock)
        {
            _writtenBytes.Enqueue((_timeProvider.GetTimestamp(), bytes));

            DiscardExpiredSamples(_timeProvider.GetTimestamp());
        }
    }

    /// <summary>
    /// Registers a unit of work that starts. Every unit of work that the host serves is
    /// counted while it runs.
    /// </summary>
    public void OperationStarted() => Interlocked.Increment(ref _activeOperations);

    /// <summary>
    /// Registers a unit of work that finishes. The count never goes below zero, so a
    /// completion that arrives without its start cannot corrupt the metric.
    /// </summary>
    public void OperationCompleted()
    {
        while (true)
        {
            var current = Volatile.Read(ref _activeOperations);

            if (current <= 0)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _activeOperations, current - 1, current) == current)
            {
                return;
            }
        }
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

    /// <summary>
    /// Drops the samples that have fallen out of the window. The caller holds the lock of
    /// the rate.
    /// </summary>
    /// <param name="now">Current instant of the monotonic source.</param>
    private void DiscardExpiredSamples(long now)
    {
        var window = (long)(TransferRateWindow.TotalSeconds * _timeProvider.TimestampFrequency);

        while (_writtenBytes.Count > 0 && now - _writtenBytes.Peek().Timestamp >= window)
        {
            _writtenBytes.Dequeue();
        }
    }
}
