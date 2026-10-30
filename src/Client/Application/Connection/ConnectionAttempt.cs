using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Client.Application.Connection;

/// <summary>
/// Attempt of connecting with the event server by the address that the player typed
/// (AC1, AC3, AD-3).
///
/// <para>
/// The attempt carries the endpoint that the address described, the state of the
/// connection and, when the attempt failed, the error of the catalog with its three parts
/// (AC1, AC3, AR-4).
/// </para>
/// <para>
/// The state is the one of the contract <see cref="ConnectionState"/>: a connection that
/// opened WITHOUT fixing the trust anchor is
/// <see cref="ConnectionState.AnchorUnverified"/>, because connecting does NOT make the
/// server trustworthy (AD-6, AD-27).
/// </para>
/// </summary>
/// <param name="Endpoint">
/// Endpoint of the server, or <c>null</c> when the address was malformed and no endpoint
/// could be built.
/// </param>
/// <param name="State">State of the connection after the attempt.</param>
/// <param name="Error">
/// Error of the catalog that explains what failed, why and what to do now, or
/// <c>null</c> when the attempt succeeded.
/// </param>
public sealed record ConnectionAttempt(
    ServerEndpoint? Endpoint,
    ConnectionState State,
    ApiError? Error)
{
    /// <summary>
    /// Whether the attempt opened the connection with the server (AC1).
    /// </summary>
    public bool IsConnected => State != ConnectionState.Disconnected;

    /// <summary>
    /// Builds the attempt of a connection that opened (AC1).
    /// </summary>
    /// <param name="endpoint">Endpoint of the server of the event.</param>
    /// <returns>The attempt of the connection.</returns>
    public static ConnectionAttempt Succeeded(ServerEndpoint endpoint) =>
        new(endpoint, ConnectionState.AnchorUnverified, null);

    /// <summary>
    /// Builds the attempt of a connection that did NOT open (AC3).
    /// </summary>
    /// <param name="endpoint">
    /// Endpoint of the server, or <c>null</c> when the address was malformed.
    /// </param>
    /// <param name="error">Error of the catalog that explains the failure.</param>
    /// <returns>The attempt of the connection.</returns>
    public static ConnectionAttempt Failed(ServerEndpoint? endpoint, ApiError error) =>
        new(endpoint, ConnectionState.Disconnected, error);
}
