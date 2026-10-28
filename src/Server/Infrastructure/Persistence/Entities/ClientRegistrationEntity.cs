namespace LANPartyHub.Server.Infrastructure.Persistence.Entities;

/// <summary>
/// Row of the <c>CLIENT_REGISTRATION</c> table (AD-22, AD-23, AD-30).
///
/// The technical key of the row is the <c>clientId</c>; the <c>machineName</c> is the human
/// label of the team, which the server validates and makes unique. The uniqueness by
/// canonical form is hardened by the migration of the story that owns it (AD-30), so the
/// initial migration only declares the column.
/// </summary>
public sealed class ClientRegistrationEntity
{
    /// <summary>Opaque UUID v7 identifier of the client; the technical key of the row.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Effective human label of the team, unique and validated by the server.</summary>
    public string MachineName { get; set; } = string.Empty;

    /// <summary>Version of the client binary.</summary>
    public string ClientVersion { get; set; } = string.Empty;

    /// <summary>Protocol version the client speaks.</summary>
    public int ProtocolVersion { get; set; }

    /// <summary>
    /// Connection state of the client, as the name of the value of the enumeration of
    /// <c>Shared</c>. It is text and never the enumeration itself (AD-22).
    /// </summary>
    public string ConnectionState { get; set; } = string.Empty;

    /// <summary>Instant of the last contact with the server, in UTC ISO-8601.</summary>
    public string LastSeenAt { get; set; } = string.Empty;
}
