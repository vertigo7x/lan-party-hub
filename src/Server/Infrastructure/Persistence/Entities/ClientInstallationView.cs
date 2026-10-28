namespace LANPartyHub.Server.Infrastructure.Persistence.Entities;

/// <summary>
/// Row of the <c>CLIENT_INSTALLATION_VIEW</c> table (AD-22).
///
/// It is the read only projection of the server over what the clients have installed. It is
/// an entity that is distinct from the <c>CLIENT_INSTALLATION</c> of the client and does not
/// share its name (AD-22). The validation state is a presentation text copied from the
/// <c>StateDisplayMap</c> of <c>Shared</c>, never the enumeration itself.
/// </summary>
public sealed class ClientInstallationView
{
    /// <summary>Opaque UUID v7 identifier of the client.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Opaque UUID v7 identifier of the game.</summary>
    public string GameId { get; set; } = string.Empty;

    /// <summary>Version installed by the client.</summary>
    public string InstalledVersion { get; set; } = string.Empty;

    /// <summary>Display text of the validation state; never the enumeration (AD-22).</summary>
    public string ValidationStateText { get; set; } = string.Empty;
}
