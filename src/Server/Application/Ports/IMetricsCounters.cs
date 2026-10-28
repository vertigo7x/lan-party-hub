namespace LANPartyHub.Server.Application.Ports;

/// <summary>
/// Counters of the host that the adapters of the server feed (AD-15).
///
/// <para>
/// The port is declared in the Application layer and it is implemented by the metrics of
/// the host. The adapters that serve a request know how many bytes leave and how many
/// units of work run, but they do not know how the metrics are published, and the
/// dependency points towards the inside.
/// </para>
/// <para>
/// The values are published locally by the metrics endpoint of the host. Nothing leaves
/// the machine: no telemetry is sent to any external service (NFR-10).
/// </para>
/// </summary>
public interface IMetricsCounters
{
    /// <summary>
    /// Counts the bytes that the host has written. The bytes feed the transfer rate.
    /// </summary>
    /// <param name="bytes">Number of bytes that the host has written.</param>
    void RecordBytesWritten(long bytes);

    /// <summary>
    /// Registers a unit of work that starts.
    /// </summary>
    void OperationStarted();

    /// <summary>
    /// Registers a unit of work that finishes.
    /// </summary>
    void OperationCompleted();

    /// <summary>
    /// Counts one error under its stable code of the catalog.
    /// </summary>
    /// <param name="code">Stable code of the error catalog.</param>
    void RecordError(string code);
}
