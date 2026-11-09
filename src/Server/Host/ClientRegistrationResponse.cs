using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Server.Host;

/// <summary>
/// Answer of the registration of a client of the event (AD-32).
///
/// <para>
/// The answer carries the EXACT shape <c>{ clientId, machineName, connectionState,
/// lastSeenAt, renamed }</c>. The <c>machineName</c> is ALWAYS the effective name
/// under which the row is registered: the proposed one when it was free, the derived
/// one when there was a collision (AD-30, AD-31).
/// </para>
/// <para>
/// The <c>renamed</c> flag is <c>true</c> ONLY when the server resolved a collision
/// and the returned name DIFFERS from the proposed one. It is <c>false</c> in the
/// plain alta, in the reconnection and when the proposed name was adopted as it was
/// (AD-32).
/// </para>
/// <para>
/// The <c>clientId</c> travels back so the client CONFIRMS that the technical identity
/// that the server holds is its own, and NOT so the server can change it: the server
/// NEVER reassigns a <c>clientId</c> (AD-23).
/// </para>
/// </summary>
public sealed record ClientRegistrationResponse(
    string ClientId,
    string MachineName,
    ConnectionState ConnectionState,
    DateTimeOffset LastSeenAt,
    bool Renamed);
