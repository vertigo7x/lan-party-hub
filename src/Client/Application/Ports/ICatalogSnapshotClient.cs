using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Client.Application.Ports;

/// <summary>
/// Port of the reading of the complete snapshot of the catalog from the server
/// (AD-5, AD-26).
///
/// <para>
/// The port is declared in the Application layer and it is implemented by the adapter that
/// speaks HTTP with the server of the event. The synchronization never reaches the adapter
/// and never knows the transport, so the rule of the version can be exercised without a
/// server (AD-1).
/// </para>
/// </summary>
public interface ICatalogSnapshotClient
{
    /// <summary>
    /// Reads the complete snapshot of the catalog of the event (AC1, AD-26).
    ///
    /// <para>
    /// The reading fails when the server is not available: the client keeps the last
    /// snapshot that it applied and communicates the pending synchronization (AC3).
    /// </para>
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The complete snapshot of the catalog.</returns>
    Task<CatalogSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);
}
