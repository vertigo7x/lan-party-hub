using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Server.Application.Ports;

/// <summary>
/// Port of the notification of a change of the catalog (AD-5, AD-26).
///
/// <para>
/// The port is declared in the Application layer and it is implemented by the adapter that
/// pushes the event to the clients of the LAN. The use cases depend on the port and never
/// on the adapter, so the publication and the withdrawal can be exercised without a hub
/// (AD-1).
/// </para>
/// <para>
/// The notification carries ONLY the version of the catalog: it is the trigger of the
/// resynchronization, and the client reads the snapshot complete by REST (AD-5, AD-26).
/// No event server to client transports data of other clients (AD-29).
/// </para>
/// </summary>
public interface ICatalogChangeNotifier
{
    /// <summary>
    /// Notifies the clients of the LAN that the catalog changed (AC1, AD-26).
    ///
    /// <para>
    /// The notification happens AFTER the commit of the write that grew the version: a
    /// client that receives the trigger reads a snapshot that already contains the change
    /// (AD-26).
    /// </para>
    /// </summary>
    /// <param name="catalogVersion">Version of the catalog after the change.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the notification has been sent.</returns>
    Task NotifyAsync(long catalogVersion, CancellationToken cancellationToken);
}
