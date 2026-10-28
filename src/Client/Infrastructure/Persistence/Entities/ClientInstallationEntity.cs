namespace LANPartyHub.Client.Infrastructure.Persistence.Entities;

/// <summary>
/// Row of the <c>CLIENT_INSTALLATION</c> table (AD-22).
///
/// <para>
/// The state of the installation is local and real: the client is its only owner and the
/// server only projects it (AD-10). The path of the installation never leaves the client.
/// </para>
/// </summary>
public sealed class ClientInstallationEntity
{
    /// <summary>Identifier of the game that is installed.</summary>
    public string GameId { get; set; } = string.Empty;

    /// <summary>Version of the game that is installed.</summary>
    public string InstalledVersion { get; set; } = string.Empty;

    /// <summary>Name of the state of the installation.</summary>
    public string ValidationState { get; set; } = string.Empty;

    /// <summary>Local path of the installation. It never leaves the client.</summary>
    public string InstallPath { get; set; } = string.Empty;

    /// <summary>
    /// Opaque identifier of the installation root that the client chose. The column exists
    /// and the story that registers the first installation is the one that fills it.
    /// </summary>
    public string InstallDirectoryId { get; set; } = string.Empty;
}
