using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Client.Application.Ports;

/// <summary>
/// Port of the reception of the trigger of a change of the catalog (AD-5, AD-26).
///
/// <para>
/// The port is declared in the Application layer and it is implemented by the adapter that
/// listens to the hub of the state of the client. The trigger carries ONLY the version of
/// the catalog: the client reads the snapshot complete by REST when it receives it
/// (AD-5, AD-26).
/// </para>
/// </summary>
public interface ICatalogChangeNotifier
{
    /// <summary>
    /// Registers the handler that reacts to the trigger of a change of the catalog.
    ///
    /// <param name="handler">Handler of the trigger.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the handler has been registered.</returns>
    Task SubscribeAsync(
        Func<CatalogChanged, CancellationToken, Task> handler,
        CancellationToken cancellationToken);
}
