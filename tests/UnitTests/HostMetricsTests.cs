using LANPartyHub.Server.Host;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// Metrics of the host (AD-15). The shape of the snapshot is the surface the endpoint
/// publishes, so it is pinned here with its exact serialized form.
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
    public void The_counters_can_be_recorded_and_they_reach_the_snapshot()
    {
        var metrics = new HostMetrics();

        metrics.SetTransferRateBytesPerSecond(1024);
        metrics.SetActiveOperations(3);
        metrics.RecordError("MANIFEST_SIGNATURE_INVALID");
        metrics.RecordError("MANIFEST_SIGNATURE_INVALID");

        var payload = MetricsEndpoint.Serialize(metrics.GetSnapshot());

        Assert.Equal(
            "{\"transferRateBytesPerSecond\":1024,\"activeOperations\":3,\"errorsByCode\":{\"MANIFEST_SIGNATURE_INVALID\":2}}",
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
}
