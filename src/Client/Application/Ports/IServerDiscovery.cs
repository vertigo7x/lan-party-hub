using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Client.Application.Ports;

/// <summary>
/// Port of the automatic discovery of the event server (AD-3, FR-3).
///
/// <para>
/// The port is declared in the Application layer and it is implemented by the adapter that
/// speaks mDNS with the network of the event. The use case of the discovery never reaches
/// the adapter and never knows the transport, so the rule of the outcome can be exercised
/// without a network (AD-1).
/// </para>
/// <para>
/// The only mechanism of the discovery is the browse of mDNS over UDP 5353: no unicast
/// sweep of the subnet, no SSDP and no DNS-SRV of an external network (AD-3).
/// </para>
/// </summary>
public interface IServerDiscovery
{
    /// <summary>
    /// Searches the servers of the event and returns the ones that answered (AC1).
    ///
    /// <para>
    /// The returned addresses converge on the SAME contract
    /// <see cref="ServerEndpoint"/> that the manual address uses: the discovered address is
    /// one more way to reach the server, not a different one (AC1, AD-3).
    /// </para>
    /// <para>
    /// The port does NOT throw when the network blocks the discovery: a failure is not a
    /// fatal error, because the manual address is a permanent and equivalent way to reach
    /// the server (AC4, AD-3).
    /// </para>
    /// </summary>
    /// <param name="timeout">Time that the search lasts at most.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The servers that answered the search.</returns>
    Task<IReadOnlyList<ServerEndpoint>> DiscoverAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken);
}
