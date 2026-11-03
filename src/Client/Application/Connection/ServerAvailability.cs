using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Client.Application.Connection;

/// <summary>
/// Availability of the event server as the CLIENT sees it (AC1, AC3, AC4, FR-3).
///
/// <para>
/// The availability is a state OF THE CLIENT and not a state of the protocol: it lives in
/// the Application layer of the client and <c>Shared</c> does not grow (AR-3, AD-18,
/// AD-33). The contract <see cref="ConnectionState"/> keeps its three values, because the
/// availability of the server is NOT the security state of the ancla de confianza
/// (AD-27).
/// </para>
/// <para>
/// The availability carries the four facts that the persistent panel of the connection
/// shows: the name of the server, the address that is in use, the version of the server and
/// the last operation that finished (AC1, UX-DR-11, UX-DR-23).
/// </para>
/// </summary>
/// <param name="Endpoint">
/// Endpoint of the server of the event, or <c>null</c> when the client is not connected to
/// any server.
/// </param>
/// <param name="ServerName">
/// Name of the server of the event, or <c>null</c> when the server did not publish it.
/// </param>
/// <param name="ServerVersion">
/// Version of the server, or <c>null</c> when the server did not publish it.
/// </param>
/// <param name="Available">
/// Whether the server answered the last probe of availability.
/// </param>
/// <param name="RetryCountdown">
/// Time that is left until the next retry of the probe, or <c>null</c> when the server is
/// available and there is no retry pending.
/// </param>
/// <param name="LastOperation">
/// Description of the last operation that finished, or <c>null</c> when the client has no
/// finished operation yet (AC2, UX-DR-11).
/// </param>
public sealed record ServerAvailability(
    ServerEndpoint? Endpoint,
    string? ServerName,
    string? ServerVersion,
    bool Available,
    TimeSpan? RetryCountdown = null,
    string? LastOperation = null)
{
    /// <summary>
    /// Builds the availability of a server that answered (AC1).
    /// </summary>
    /// <param name="endpoint">Endpoint of the server of the event.</param>
    /// <param name="serverName">Name of the server, or <c>null</c> when it did not publish it.</param>
    /// <param name="serverVersion">Version of the server, or <c>null</c> when it did not publish it.</param>
    /// <param name="lastOperation">Description of the last operation that finished.</param>
    /// <returns>The availability of a server that answered.</returns>
    public static ServerAvailability Reachable(
        ServerEndpoint endpoint,
        string? serverName,
        string? serverVersion,
        string? lastOperation = null) =>
        new(endpoint, serverName, serverVersion, Available: true, RetryCountdown: null, lastOperation);

    /// <summary>
    /// Builds the availability of a server that did NOT answer (AC3, AC4).
    ///
    /// <para>
    /// The catalog that the client applied is CONSERVED: the availability carries no
    /// snapshot, y el cliente conserva el último que aplicó (AC3, UX-DR-32).
    /// </para>
    /// </summary>
    /// <param name="endpoint">Endpoint of the server of the event.</param>
    /// <param name="serverName">Name of the server that was published before the fall.</param>
    /// <param name="serverVersion">Version of the server that was published before the fall.</param>
    /// <param name="retryCountdown">Time that is left until the next retry.</param>
    /// <param name="lastOperation">Description of the last operation that finished.</param>
    /// <returns>The availability of a server that did not answer.</returns>
    public static ServerAvailability Unreachable(
        ServerEndpoint endpoint,
        string? serverName,
        string? serverVersion,
        TimeSpan retryCountdown,
        string? lastOperation = null) =>
        new(
            endpoint,
            serverName,
            serverVersion,
            Available: false,
            retryCountdown,
            lastOperation);
}
