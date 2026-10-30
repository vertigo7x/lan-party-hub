using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Client.Application.CatalogSync;

/// <summary>
/// Use case of the synchronization of the catalog of the event (AC1, AC2, AC3, AD-5,
/// AD-26).
///
/// <para>
/// The use case is triggered by the event <see cref="CatalogChanged"/> of the hub: it
/// reads the complete snapshot of the catalog by REST and it applies it through the
/// applier, which is the only place where the rule of the version lives (AC1, AD-5).
/// </para>
/// <para>
/// When the server is not available the use case does NOT erase anything: the client
/// CONSERVES the last catalog that it applied and it communicates the pending
/// synchronization, so the player sees the notice «Catálogo desactualizado · sincronizado
/// hace 12 min» with the action «Sincronizar ahora» and the navigation is NOT blocked
/// (AC3, UX-DR-32).
/// </para>
/// <para>
/// The use case depends on the ports declared in this layer and never on an adapter, so it
/// can be exercised without a server and without a database (AD-1).
/// </para>
/// </summary>
public sealed class CatalogSyncService
{
    private readonly ICatalogSnapshotClient _client;
    private readonly ICatalogSnapshotStore _store;
    private readonly ICatalogChangeNotifier _notifier;
    private readonly CatalogSnapshotApplier _applier;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CatalogSyncService> _logger;
    private readonly Lock _gate = new();

    private long? _announcedServerCatalogVersion;

    /// <summary>Creates the use case of the synchronization of the catalog.</summary>
    /// <param name="client">Port of the reading of the snapshot from the server.</param>
    /// <param name="store">Port of the persistence of the snapshot of the client.</param>
    /// <param name="notifier">Port of the reception of the trigger of a change.</param>
    /// <param name="applier">Applier of the snapshot of the catalog.</param>
    /// <param name="logger">Log of the use case.</param>
    /// <param name="timeProvider">
    /// Source of the instant of the last successful synchronization. It defaults to the real
    /// one and it exists so that the age of the catalog can be exercised with a known
    /// instant.
    /// </param>
    public CatalogSyncService(
        ICatalogSnapshotClient client,
        ICatalogSnapshotStore store,
        ICatalogChangeNotifier notifier,
        CatalogSnapshotApplier applier,
        ILogger<CatalogSyncService> logger,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(notifier);
        ArgumentNullException.ThrowIfNull(applier);
        ArgumentNullException.ThrowIfNull(logger);

        _client = client;
        _store = store;
        _notifier = notifier;
        _applier = applier;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Starts the synchronization: it subscribes to the trigger of a change of the catalog
    /// and it performs the first synchronization (AC1, AC-3).
    ///
    /// <para>
    /// The trigger carries the version that the server publishes, and that version is
    /// CONSERVED as the maximum that the client knows: it is the only signal that says that
    /// the catalog of the client is OUTDATED even when the reading of the snapshot fails
    /// (AC3, AR-5).
    /// </para>
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the subscription is registered.</returns>
    public Task StartAsync(CancellationToken cancellationToken) =>
        _notifier.SubscribeAsync(
            async (changed, token) =>
            {
                RememberAnnouncedVersion(changed.CatalogVersion);

                await SynchronizeAsync(token).ConfigureAwait(false);
            },
            cancellationToken);

    /// <summary>
    /// Synchronizes the catalog: it reads the snapshot of the server and it applies it when
    /// its version is greater than the one that the client has (AC1, AC2).
    ///
    /// <para>
    /// The state that the use case returns carries the version that the server publishes,
    /// which is the one that says whether the snapshot that the client holds is OUTDATED:
    /// with the server down the client conserves its last snapshot and it conserves also
    /// the version that the server announced, so the client knows that its catalog is
    /// outdated even when the reading has failed (AC3, AR-5).
    /// </para>
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The state of the synchronization after the attempt.</returns>
    public async Task<CatalogSyncState> SynchronizeAsync(CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await _client.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);

            var outcome = await _applier.ApplyAsync(snapshot, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "La sincronización del catálogo terminó con la versión {CatalogVersion}.",
                outcome.CatalogVersion);

            return CatalogSyncState.UpToDate(
                outcome.CatalogVersion,
                _timeProvider.GetUtcNow(),
                outcome.WithdrawnOperations,
                outcome.WithdrawnInstallations,
                snapshot,
                ResolveServerCatalogVersion(snapshot.CatalogVersion));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // El servidor no está disponible: el cliente CONSERVA el último catálogo que
            // aplicó y comunica el estado de sincronización pendiente (AC3, UX-DR-32).
            var appliedVersion = await _store
                .GetAppliedVersionAsync(cancellationToken)
                .ConfigureAwait(false);

            _logger.LogWarning(
                exception,
                "No se pudo sincronizar el catálogo: el cliente conserva el catálogo con la versión {CatalogVersion}.",
                appliedVersion);

            return CatalogSyncState.Pending(
                appliedVersion,
                await _store
                    .GetLastSynchronizedAtAsync(cancellationToken)
                    .ConfigureAwait(false),
                await _store
                    .LoadAsync(cancellationToken)
                    .ConfigureAwait(false),
                ResolveServerCatalogVersion(appliedVersion));
        }
    }

    /// <summary>
    /// Conserves the version that the server announced through the hub, which is the
    /// maximum that the client knows (AC3, AR-5).
    ///
    /// <para>
    /// The version of the server NEVER decreases: the version that the server announces is
    /// always greater or equal than the previous one, and the maximum is the one that is
    /// conserved (AC1, AR-5).
    /// </para>
    /// </summary>
    /// <param name="catalogVersion">Version that the server announced.</param>
    private void RememberAnnouncedVersion(long catalogVersion)
    {
        lock (_gate)
        {
            if (_announcedServerCatalogVersion is null ||
                catalogVersion > _announcedServerCatalogVersion.Value)
            {
                _announcedServerCatalogVersion = catalogVersion;
            }
        }
    }

    /// <summary>
    /// Resolves the version of the catalog that the server publishes, which is the version
    /// that the server announced OR the version of the snapshot that the client just read
    /// (AC3, AR-5).
    ///
    /// <para>
    /// The snapshot that the client reads from the server carries the version that the
    /// server publishes at that instant, so the version of the server is NEVER smaller than
    /// the version of the snapshot: the comparison of the resolver stays correct (AC3,
    /// AR-5).
    /// </para>
    /// </summary>
    /// <param name="knownVersion">
    /// Version that the client knows: the version of the snapshot that was read or the
    /// version that the client has applied.
    /// </param>
    /// <returns>The version of the catalog that the server publishes.</returns>
    private long? ResolveServerCatalogVersion(long knownVersion)
    {
        lock (_gate)
        {
            if (_announcedServerCatalogVersion is null)
            {
                return knownVersion;
            }

            return _announcedServerCatalogVersion.Value > knownVersion
                ? _announcedServerCatalogVersion.Value
                : knownVersion;
        }
    }
}

/// <summary>
/// The state of the synchronization of the catalog of the client (AC3, UX-DR-32).
/// </summary>
/// <param name="CatalogVersion">Version of the catalog that the client has.</param>
/// <param name="Synchronized">Whether the client holds a catalog that is up to date.</param>
/// <param name="LastSynchronizedAt">Instant of the last successful synchronization.</param>
/// <param name="WithdrawnOperations">
/// Operations that terminate with the result <see cref="OperationOutcome.PackageWithdrawn"/>.
/// </param>
/// <param name="WithdrawnInstallations">
/// Installations of games that were withdrawn from the catalog, conserved with their derived
/// view state.
/// </param>
/// <param name="Snapshot">
/// The snapshot of the catalog that the client has applied, or <c>null</c> when the client
/// never applied one.
///
/// <para>
/// The snapshot travels with the state because the surface of the catalog filters over
/// it (AC1, AC2). With the server down the state carries the LAST snapshot that was
/// applied, so the player keeps seeing the catalog that he had and the search keeps
/// working over it (AC3, UX-DR-32).
/// </para>
/// </param>
/// <param name="ServerCatalogVersion">
/// Version of the catalog that the server publishes, or <c>null</c> when the client does
/// NOT know it, which happens when the server did not answer.
///
/// <para>
/// The version of the server is what says whether the snapshot that the client holds is
/// OUTDATED: a client whose snapshot is older than the version that the server publishes
/// shows the badge «sincronizando» (AC3, AR-5).
/// </para>
/// </param>
public sealed record CatalogSyncState(
    long CatalogVersion,
    bool Synchronized,
    DateTimeOffset? LastSynchronizedAt,
    IReadOnlyList<WithdrawnOperation> WithdrawnOperations,
    IReadOnlyList<WithdrawnInstallation> WithdrawnInstallations,
    CatalogSnapshot? Snapshot,
    long? ServerCatalogVersion)
{
    /// <summary>
    /// Builds the state of a client whose catalog is up to date.
    /// </summary>
    /// <param name="catalogVersion">Version of the catalog applied.</param>
    /// <param name="synchronizedAt">Instant of the successful synchronization.</param>
    /// <param name="withdrawnOperations">Operations that terminate with the withdrawal.</param>
    /// <param name="withdrawnInstallations">Installations that are conserved.</param>
    /// <param name="snapshot">Snapshot that was applied.</param>
    /// <param name="serverCatalogVersion">Version of the catalog that the server publishes.</param>
    /// <returns>The state of the synchronized client.</returns>
    public static CatalogSyncState UpToDate(
        long catalogVersion,
        DateTimeOffset synchronizedAt,
        IReadOnlyList<WithdrawnOperation> withdrawnOperations,
        IReadOnlyList<WithdrawnInstallation> withdrawnInstallations,
        CatalogSnapshot? snapshot,
        long? serverCatalogVersion = null) =>
        new(
            catalogVersion,
            true,
            synchronizedAt,
            withdrawnOperations,
            withdrawnInstallations,
            snapshot,
            serverCatalogVersion ?? snapshot?.CatalogVersion);

    /// <summary>
    /// Builds the state of a client whose catalog is pending of synchronization (AC3).
    /// </summary>
    /// <param name="catalogVersion">Version of the catalog that the client conserves.</param>
    /// <param name="lastSynchronizedAt">Instant of the last successful synchronization.</param>
    /// <param name="snapshot">
    /// Last snapshot that the client applied, or <c>null</c> when it never applied one.
    /// </param>
    /// <param name="serverCatalogVersion">
    /// Version of the catalog that the server publishes, or <c>null</c> when the client
    /// does not know it.
    /// </param>
    /// <returns>The state of the pending client.</returns>
    public static CatalogSyncState Pending(
        long catalogVersion,
        DateTimeOffset? lastSynchronizedAt,
        CatalogSnapshot? snapshot = null,
        long? serverCatalogVersion = null) =>
        new(catalogVersion, false, lastSynchronizedAt, [], [], snapshot, serverCatalogVersion);
}
