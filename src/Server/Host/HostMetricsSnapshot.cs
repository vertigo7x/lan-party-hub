namespace LANPartyHub.Server.Host;

/// <summary>
/// Snapshot of the metrics of the host (AD-15). The shape is fixed: the transfer rate, the
/// number of active operations and the errors counted per code.
/// </summary>
/// <param name="TransferRateBytesPerSecond">Current transfer rate, in bytes per second.</param>
/// <param name="ActiveOperations">Number of operations that are running right now.</param>
/// <param name="ErrorsByCode">Errors counted per stable code of the error catalog.</param>
public sealed record HostMetricsSnapshot(
    long TransferRateBytesPerSecond,
    int ActiveOperations,
    IReadOnlyDictionary<string, long> ErrorsByCode);
