using LANPartyHub.Server.Host;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// Metrics of the host (AD-15). The shape of the snapshot is the surface the endpoint
/// publishes, so it is pinned here with its exact serialized form, and the three metrics
/// are exercised as instruments and not as values that somebody publishes by hand.
/// </summary>
public sealed class HostMetricsTests
{
    [Fact]
    public void The_snapshot_of_a_host_without_traffic_has_the_exact_serialized_shape()
    {
        var metrics = new HostMetrics();

        var payload = MetricsEndpoint.Serialize(metrics.GetSnapshot());

        Assert.Equal(
            "{\"transferRateBytesPerSecond\":0,\"activeOperations\":0,\"errorsByCode\":{}}",
            payload);
    }

    [Fact]
    public void The_bytes_that_the_host_writes_feed_the_transfer_rate()
    {
        var clock = new ManualTimeProvider();
        var metrics = new HostMetrics(clock);

        metrics.RecordBytesWritten(5_000);
        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(5_000, metrics.TransferRateBytesPerSecond);
        Assert.Equal(5_000, metrics.GetSnapshot().TransferRateBytesPerSecond);
    }

    [Fact]
    public void The_transfer_rate_is_the_bytes_over_the_time_that_the_window_really_covers()
    {
        var clock = new ManualTimeProvider();
        var metrics = new HostMetrics(clock);

        metrics.RecordBytesWritten(5_000);
        clock.Advance(TimeSpan.FromSeconds(2));

        // The bytes took two seconds to be written, so the rate is half of them and not a
        // fifth of them.
        Assert.Equal(2_500, metrics.TransferRateBytesPerSecond);
    }

    [Fact]
    public void The_transfer_rate_decays_when_the_window_passes_without_traffic()
    {
        var clock = new ManualTimeProvider();
        var metrics = new HostMetrics(clock);

        metrics.RecordBytesWritten(5_000);

        clock.Advance(HostMetrics.TransferRateWindow + TimeSpan.FromSeconds(1));

        Assert.Equal(0, metrics.TransferRateBytesPerSecond);
    }

    [Fact]
    public void The_bytes_that_leave_the_window_do_not_count_anymore()
    {
        var clock = new ManualTimeProvider();
        var metrics = new HostMetrics(clock);

        metrics.RecordBytesWritten(5_000);
        clock.Advance(HostMetrics.TransferRateWindow + TimeSpan.FromSeconds(1));

        metrics.RecordBytesWritten(2_500);
        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(2_500, metrics.TransferRateBytesPerSecond);
    }

    [Fact]
    public void The_rate_does_not_depend_on_the_wall_clock()
    {
        var clock = new ManualTimeProvider();
        var metrics = new HostMetrics(clock);

        metrics.RecordBytesWritten(5_000);
        clock.Advance(TimeSpan.FromSeconds(1));

        var before = metrics.TransferRateBytesPerSecond;

        // The wall clock jumps backwards: the instants come from a monotonic source, so
        // the rate does not change and the samples do not expire.
        clock.MoveWallClockBackwards(TimeSpan.FromHours(1));

        Assert.Equal(before, metrics.TransferRateBytesPerSecond);
    }

    [Fact]
    public void The_operations_that_run_are_counted_while_they_run()
    {
        var metrics = new HostMetrics();

        metrics.OperationStarted();
        metrics.OperationStarted();

        Assert.Equal(2, metrics.GetSnapshot().ActiveOperations);

        metrics.OperationCompleted();

        Assert.Equal(1, metrics.GetSnapshot().ActiveOperations);
    }

    [Fact]
    public void The_count_of_active_operations_never_goes_below_zero()
    {
        var metrics = new HostMetrics();

        metrics.OperationCompleted();

        Assert.Equal(0, metrics.GetSnapshot().ActiveOperations);
    }

    [Fact]
    public void The_counters_can_be_recorded_and_they_reach_the_snapshot()
    {
        var clock = new ManualTimeProvider();
        var metrics = new HostMetrics(clock);

        metrics.RecordBytesWritten(1_024);
        clock.Advance(TimeSpan.FromSeconds(2));

        metrics.OperationStarted();
        metrics.OperationStarted();
        metrics.OperationStarted();
        metrics.RecordError("MANIFEST_SIGNATURE_INVALID");
        metrics.RecordError("MANIFEST_SIGNATURE_INVALID");

        var payload = MetricsEndpoint.Serialize(metrics.GetSnapshot());

        Assert.Equal(
            "{\"transferRateBytesPerSecond\":512,\"activeOperations\":3,\"errorsByCode\":{\"MANIFEST_SIGNATURE_INVALID\":2}}",
            payload);
    }

    [Fact]
    public void The_errors_by_code_are_a_defensive_copy_of_the_counters()
    {
        var metrics = new HostMetrics();

        var before = metrics.ErrorsByCode;

        metrics.RecordError("ANCHOR_FINGERPRINT_MISMATCH");

        Assert.Empty(before);
        Assert.Single(metrics.ErrorsByCode);
    }

    [Fact]
    public void An_error_without_a_code_is_not_counted()
    {
        var metrics = new HostMetrics();

        metrics.RecordError(string.Empty);
        metrics.RecordError("   ");

        Assert.Empty(metrics.ErrorsByCode);
    }

    /// <summary>
    /// Clock that only moves when the test moves it, so the window of the transfer rate
    /// can be exercised without waiting. The instants come from a monotonic source and the
    /// wall clock is a separate one, so the test can move them apart.
    /// </summary>
    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;
        private DateTimeOffset _wallClock = DateTimeOffset.UnixEpoch;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _timestamp;

        public override DateTimeOffset GetUtcNow() => _wallClock;

        public void Advance(TimeSpan delta)
        {
            _timestamp += delta.Ticks;
            _wallClock += delta;
        }

        public void MoveWallClockBackwards(TimeSpan delta) => _wallClock -= delta;
    }
}
