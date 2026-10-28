namespace LANPartyHub.Client.Infrastructure.Persistence.Entities;

/// <summary>
/// Row of the <c>CLIENT_IDENTITY</c> table (AD-22, AD-23).
///
/// <para>
/// The table holds the identifier that the client emitted once, in its canonical form: the 32
/// lowercase hexadecimal digits without dashes. It is the single row of configuration that
/// fixes the identity of the team.
/// </para>
/// </summary>
public sealed class ClientIdentityEntity
{
    /// <summary>Canonical form of the identifier of the client.</summary>
    public string ClientId { get; set; } = string.Empty;
}
