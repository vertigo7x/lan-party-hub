using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Domain;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Client.Application;

/// <summary>
/// Startup of the client (AD-9, AD-11, AD-17, AD-23, AD-24).
///
/// <para>
/// The use case runs the sequence of the startup: it applies the effective configuration to
/// the disk by creating the layout of the data folder, it applies the versioned migrations of
/// the database of the client, it loads the identity of the team and it emits it only when
/// there is none, and it reconciles the persisted progress against the partial files.
/// </para>
/// <para>
/// The use case depends on the ports declared in this layer and never on an adapter, so the
/// sequence can be exercised without the file system and without a database.
/// </para>
/// </summary>
public sealed class ClientBootstrap
{
    private readonly IClientDataLayout _layout;
    private readonly IClientStateStore _stateStore;
    private readonly IDownloadProgressReconciler _reconciler;
    private readonly IClientCorrelationScope _correlationScope;
    private readonly ILogger<ClientBootstrap> _logger;

    /// <summary>Creates the use case of the startup.</summary>
    /// <param name="layout">Layout of the data folder of the client.</param>
    /// <param name="stateStore">Access to the database of the client.</param>
    /// <param name="reconciler">Reconciliation of the persisted progress.</param>
    /// <param name="correlationScope">Scope that correlates the log of the client.</param>
    /// <param name="logger">Log of the startup.</param>
    public ClientBootstrap(
        IClientDataLayout layout,
        IClientStateStore stateStore,
        IDownloadProgressReconciler reconciler,
        IClientCorrelationScope correlationScope,
        ILogger<ClientBootstrap> logger)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(stateStore);
        ArgumentNullException.ThrowIfNull(reconciler);
        ArgumentNullException.ThrowIfNull(correlationScope);
        ArgumentNullException.ThrowIfNull(logger);

        _layout = layout;
        _stateStore = stateStore;
        _reconciler = reconciler;
        _correlationScope = correlationScope;
        _logger = logger;
    }

    /// <summary>
    /// Runs the sequence of the startup.
    /// </summary>
    /// <param name="options">Effective configuration of the client.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task with the outcome of the startup.</returns>
    public async Task<ClientStartupResult> RunAsync(
        ClientBootstrapOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        // The effective configuration reaches the disk: the layout of the data folder is
        // created with the installation root, the folder of the partial downloads, the cache of
        // the signed manifests and the file of the database (AD-9, AD-17).
        _layout.EnsureCreated();

        // The versioned migrations are applied at startup. EnsureCreated is never used: it
        // skips the history of the migrations (AD-11).
        await _stateStore.MigrateAsync(cancellationToken);

        // The identity is loaded and it is emitted only when there is none. The identifier is
        // never regenerated, neither on a restart nor on a reinstallation of the binary while
        // the data folder survives (AD-23).
        var identity = await _stateStore.LoadIdentityAsync(cancellationToken);
        var emitted = false;

        if (identity is null)
        {
            identity = ClientIdentity.Emit();
            emitted = true;

            await _stateStore.SaveIdentityAsync(identity, cancellationToken);
        }

        // AD-15, AD-23: the scope of the client is opened as soon as the identity of the
        // team is known, and it is not closed here: the composition root owns it and it
        // lives as long as the session, so every entry that the client writes after the
        // startup carries the client and the unit of work too.
        _correlationScope.Open(identity.CanonicalId, Guid.CreateVersion7().ToString("N"));

        if (emitted)
        {
            _logger.LogInformation(
                "Se ha emitido el identificador del cliente {ClientId}.",
                identity.CanonicalId);
        }
        else
        {
            _logger.LogInformation(
                "El cliente usa el identificador {ClientId} que ya estaba persistido.",
                identity.CanonicalId);
        }

        // The persisted progress is reconciled against the partial files and the operations of
        // an engine that does not exist in the active version are rejected (AD-11, AD-24).
        var reconciliation = await _reconciler.ReconcileAsync(cancellationToken);

        return new ClientStartupResult
        {
            Identity = identity,
            DataFolder = _layout.Folder,
            InstallDirectory = _layout.InstallDirectory,
            DownloadsDirectory = _layout.DownloadsDirectory,
            ManifestsDirectory = _layout.ManifestsDirectory,
            StateDatabasePath = _layout.StateDatabasePath,
            ServerAddress = options.ServerAddress,
            Theme = options.Theme,
            Reconciliation = reconciliation,
        };
    }
}
