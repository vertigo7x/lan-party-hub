namespace LANPartyHub.Shared.Contracts;

/// <summary>
/// Organization view of a registered client (AD-18, AD-29). It is the shape returned by
/// <c>GET /api/v1/clients</c>: the client list never travels through a SignalR event.
/// </summary>
public sealed record ClientRegistrationView
{
    /// <summary>Creates the view of a registered client.</summary>
    /// <param name="clientId">Opaque UUID v7 identifier, technical key of the row (AD-23).</param>
    /// <param name="machineName">Effective human label of the team, unique and validated by the server (AD-30).</param>
    /// <param name="clientVersion">Version of the client binary.</param>
    /// <param name="protocolVersion">Protocol version the client speaks (AD-6).</param>
    /// <param name="connectionState">Current connection state of the client.</param>
    /// <param name="lastSeenAt">Instant of the last contact with the server, normalized to UTC.</param>
    public ClientRegistrationView(
        string clientId,
        string machineName,
        string clientVersion,
        int protocolVersion,
        ConnectionState connectionState,
        DateTimeOffset lastSeenAt)
    {
        ClientId = clientId;
        MachineName = machineName;
        ClientVersion = clientVersion;
        ProtocolVersion = protocolVersion;
        ConnectionState = connectionState;
        LastSeenAt = lastSeenAt.ToUniversalTime();
    }

    /// <summary>Opaque UUID v7 identifier, technical key of the row (AD-23).</summary>
    public string ClientId { get; init; }

    /// <summary>Effective human label of the team, unique and validated by the server (AD-30).</summary>
    public string MachineName { get; init; }

    /// <summary>Version of the client binary.</summary>
    public string ClientVersion { get; init; }

    /// <summary>Protocol version the client speaks (AD-6).</summary>
    public int ProtocolVersion { get; init; }

    /// <summary>Current connection state of the client.</summary>
    public ConnectionState ConnectionState { get; init; }

    /// <summary>UTC instant of the last contact with the server.</summary>
    public DateTimeOffset LastSeenAt { get; init; }
}
