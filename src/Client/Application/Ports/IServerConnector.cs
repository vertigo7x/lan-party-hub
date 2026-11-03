using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Client.Application.Ports;

/// <summary>
/// Port of the connection with the event server by an address that the player typed
/// (AC1, AD-3).
///
/// <para>
/// The port is declared in the Application layer and it is implemented by the adapter
/// that opens the socket with the server. The use case of the connection never reaches
/// the adapter and never knows the transport, so the rule of the attempt can be exercised
/// without a network (AD-1).
/// </para>
/// <para>
/// The port does NOT throw when the destination does not answer: a failure is not a
/// fatal error, because the player has to see the three part message of the error and
/// keep the manual way of connecting operative (AC3, AD-3).
/// </para>
/// </summary>
public interface IServerConnector
{
    /// <summary>
    /// Opens the connection with the server of the event (AC1).
    ///
    /// <para>
    /// The returned endpoint converges on the SAME contract
    /// <see cref="ServerEndpoint"/> that the discovery uses: the manual address is one
    /// more way to reach the server, not a different one (AC1, AD-3).
    /// </para>
    /// </summary>
    /// <param name="endpoint">Endpoint of the server of the event.</param>
    /// <param name="timeout">Time that the opening lasts at most.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the connection is open.</returns>
    Task ConnectAsync(
        ServerEndpoint endpoint,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}
