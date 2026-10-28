namespace LANPartyHub.Client.Application.Ports;

/// <summary>
/// Persistence of the settings of the client in its configuration file (AD-12, AR-23).
///
/// <para>
/// The port is declared in the Application layer and it is implemented by the adapter that
/// owns the file system. The configuration file is the layer of the lowest priority, so the
/// value that is written there is the one that applies when no flag and no environment
/// variable fix it.
/// </para>
/// </summary>
public interface IClientSettingsStore
{
    /// <summary>
    /// Writes the installation root in the configuration file and keeps the other keys of the
    /// file.
    /// </summary>
    /// <param name="installDirectory">Absolute path of the installation root.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the file is written.</returns>
    Task SaveInstallDirectoryAsync(string installDirectory, CancellationToken cancellationToken);
}
