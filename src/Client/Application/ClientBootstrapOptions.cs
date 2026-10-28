namespace LANPartyHub.Client.Application;

/// <summary>
/// Effective configuration that the startup of the client receives (AD-12, AR-23).
///
/// <para>
/// The installation root is not part of these options: the layout of the data folder is the
/// single source of truth for the confined root, so the option and the layout cannot drift
/// apart.
/// </para>
/// </summary>
public sealed class ClientBootstrapOptions
{
    /// <summary>
    /// Address of the event server, or <c>null</c> when no source fixes it. The server is
    /// reached through the discovery or through the address that the player types, and both are
    /// equivalent (AD-3).
    /// </summary>
    public string? ServerAddress { get; init; }

    /// <summary>Theme of the interface of the client.</summary>
    public required string Theme { get; init; }
}
