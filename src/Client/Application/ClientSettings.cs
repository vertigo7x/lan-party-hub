using LANPartyHub.Client.Application.Ports;

namespace LANPartyHub.Client.Application;

/// <summary>
/// Settings of the client (AC4).
///
/// <para>
/// The installation root is the only directory that the rule of extraction confines (AD-9), so
/// the change of that directory is persisted in the configuration file of the client and it is
/// applied at once: the new root is the only confined one from that moment on.
/// </para>
/// <para>
/// The persisted value lives in the layer of the lowest priority of the configuration, so a
/// flag or an environment variable still wins over it when the client starts again.
/// </para>
/// </summary>
public sealed class ClientSettings
{
    private readonly IClientSettingsStore _store;
    private readonly IClientDataLayout _layout;

    /// <summary>Creates the use case of the settings.</summary>
    /// <param name="store">Persistence of the settings in the configuration file.</param>
    /// <param name="layout">Layout of the data folder of the client.</param>
    public ClientSettings(IClientSettingsStore store, IClientDataLayout layout)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(layout);

        _store = store;
        _layout = layout;
    }

    /// <summary>
    /// The effective installation root: the only directory that the rule of extraction
    /// confines.
    /// </summary>
    public string InstallDirectory => _layout.InstallDirectory;

    /// <summary>
    /// Persists the installation root and applies it at once.
    /// </summary>
    /// <param name="installDirectory">Absolute path of the new installation root.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task with the outcome of the change.</returns>
    public async Task<ClientSettingsResult> SaveInstallDirectoryAsync(
        string installDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);

        await _store.SaveInstallDirectoryAsync(installDirectory, cancellationToken);

        // The new root is applied at once and it becomes the only confined directory.
        _layout.SetInstallDirectory(installDirectory);

        return new ClientSettingsResult { InstallDirectory = _layout.InstallDirectory };
    }
}

/// <summary>
/// Outcome of a change of the settings of the client.
/// </summary>
public sealed class ClientSettingsResult
{
    /// <summary>The effective installation root after the change.</summary>
    public required string InstallDirectory { get; init; }
}
