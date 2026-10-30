using LANPartyHub.Client.Application.Catalog;
using LANPartyHub.Client.Application.CatalogSync;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The synchronization of the catalog of the client (AC1, AC2, AC3, AR-5, AD-5, AD-26).
///
/// <para>
/// The tests fix the behaviour that hace posible el estado «sincronizando»: la versión que
/// el servidor anuncia por el hub es la ÚNICA señal que dice que el catálogo del cliente
/// está desactualizado, y el servicio la CONSERVA y la propaga al estado que la superficie
/// consume (AC3, AR-5).
/// </para>
/// </summary>
public sealed class CatalogSyncServiceTests
{
    [Fact]
    public async Task The_version_that_the_server_announces_reaches_the_state()
    {
        var client = new FakeSnapshotClient(new CatalogSnapshot(6, [], []));
        var store = new FakeSnapshotStore();
        var notifier = new FakeChangeNotifier();

        var service = CreateService(client, store, notifier);

        await service.StartAsync(TestContext.Current.CancellationToken);

        await notifier.RaiseAsync(new CatalogChanged(6), TestContext.Current.CancellationToken);

        var state = await service.SynchronizeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(6, state.ServerCatalogVersion);
    }

    [Fact]
    public async Task A_client_whose_snapshot_is_older_than_the_announced_version_is_synchronizing()
    {
        // El servidor publica la v6 y el trigger del hub llega, pero la lectura del snapshot
        // falla: el cliente conserva su v5 y sabe que el servidor publica la v6, de modo que
        // su catálogo está DESACTUALIZADO y la insignia dice «sincronizando» (AC3, AR-5).
        var client = new FakeSnapshotClient(new CatalogSnapshot(5, [], []))
        {
            Fails = true,
        };

        var store = new FakeSnapshotStore
        {
            AppliedVersion = 5,
            Snapshot = new CatalogSnapshot(5, [], []),
        };

        var notifier = new FakeChangeNotifier();

        var service = CreateService(client, store, notifier);

        await service.StartAsync(TestContext.Current.CancellationToken);

        await notifier.RaiseAsync(new CatalogChanged(6), TestContext.Current.CancellationToken);

        var state = await service.SynchronizeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(6, state.ServerCatalogVersion);
        Assert.Equal(5, state.Snapshot?.CatalogVersion);
        Assert.False(state.Synchronized);

        // La superficie compara la versión del servidor con la del snapshot del cliente: el
        // estado derivado es «sincronizando» (AC3, AR-5).
        Assert.Equal(
            CatalogViewState.Synchronizing,
            CatalogViewStateResolver.Resolve(
                game: null,
                snapshotCatalogVersion: state.Snapshot?.CatalogVersion,
                serverCatalogVersion: state.ServerCatalogVersion,
                installation: null));
    }

    [Fact]
    public async Task The_pending_state_carries_the_version_that_the_server_announces()
    {
        // La ruta «Pending» es la que se recorre cuando el servidor no responde: la versión
        // que el servidor anunció tiene que viajar en el estado, porque es lo que dice que
        // el catálogo del cliente está desactualizado (AC3, AR-5).
        var client = new FakeSnapshotClient(new CatalogSnapshot(5, [], []))
        {
            Fails = true,
        };

        var store = new FakeSnapshotStore
        {
            AppliedVersion = 5,
            Snapshot = new CatalogSnapshot(5, [], []),
        };

        var notifier = new FakeChangeNotifier();

        var service = CreateService(client, store, notifier);

        await service.StartAsync(TestContext.Current.CancellationToken);

        await notifier.RaiseAsync(new CatalogChanged(6), TestContext.Current.CancellationToken);

        var state = await service.SynchronizeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(6, state.ServerCatalogVersion);
    }

    [Fact]
    public async Task A_client_that_never_received_a_trigger_uses_the_version_of_its_snapshot()
    {
        // Sin trigger del hub el cliente no conoce otra versión que la de su propio snapshot:
        // el estado no puede afirmar que su catálogo está desactualizado (AC3, UX-DR-32).
        var client = new FakeSnapshotClient(new CatalogSnapshot(5, [], []));
        var store = new FakeSnapshotStore();
        var notifier = new FakeChangeNotifier();

        var service = CreateService(client, store, notifier);

        var state = await service.SynchronizeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(5, state.ServerCatalogVersion);
    }

    [Fact]
    public async Task The_announced_version_never_decreases()
    {
        // La versión del catálogo nunca decrece: un trigger atrasado no puede hacer que el
        // cliente olvide la versión más alta que ya conoce (AC1, AR-5).
        var client = new FakeSnapshotClient(new CatalogSnapshot(6, [], []));
        var store = new FakeSnapshotStore();
        var notifier = new FakeChangeNotifier();

        var service = CreateService(client, store, notifier);

        await service.StartAsync(TestContext.Current.CancellationToken);

        await notifier.RaiseAsync(new CatalogChanged(9), TestContext.Current.CancellationToken);

        await notifier.RaiseAsync(new CatalogChanged(7), TestContext.Current.CancellationToken);

        var state = await service.SynchronizeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(9, state.ServerCatalogVersion);
    }

    [Fact]
    public async Task The_state_of_a_synchronized_client_carries_the_announced_version()
    {
        // Cuando la lectura funciona, la versión del servidor es la MAYOR entre la que
        // anunció el hub y la del snapshot leído: nunca es menor que la del snapshot, de
        // modo que la comparación de la superficie sigue siendo correcta (AC3, AR-5).
        var client = new FakeSnapshotClient(new CatalogSnapshot(4, [], []));
        var store = new FakeSnapshotStore();
        var notifier = new FakeChangeNotifier();

        var service = CreateService(client, store, notifier);

        await service.StartAsync(TestContext.Current.CancellationToken);

        await notifier.RaiseAsync(new CatalogChanged(8), TestContext.Current.CancellationToken);

        var state = await service.SynchronizeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(8, state.ServerCatalogVersion);
        Assert.True(state.Synchronized);
    }

    /// <summary>
    /// Builds the use case of the synchronization with the doubles of the ports.
    /// </summary>
    /// <param name="client">Double of the reading of the snapshot.</param>
    /// <param name="store">Double of the persistence of the snapshot.</param>
    /// <param name="notifier">Double of the reception of the trigger.</param>
    /// <returns>The use case of the synchronization.</returns>
    private static CatalogSyncService CreateService(
        FakeSnapshotClient client,
        FakeSnapshotStore store,
        FakeChangeNotifier notifier) =>
        new(
            client,
            store,
            notifier,
            new CatalogSnapshotApplier(store, NullLogger<CatalogSnapshotApplier>.Instance),
            NullLogger<CatalogSyncService>.Instance);

    /// <summary>
    /// Double of the reading of the snapshot of the server (AD-1).
    /// </summary>
    private sealed class FakeSnapshotClient : ICatalogSnapshotClient
    {
        private readonly CatalogSnapshot _snapshot;

        public FakeSnapshotClient(CatalogSnapshot snapshot) => _snapshot = snapshot;

        public bool Fails { get; init; }

        public Task<CatalogSnapshot> GetSnapshotAsync(CancellationToken cancellationToken) =>
            Fails
                ? throw new HttpRequestException("El servidor no responde.")
                : Task.FromResult(_snapshot);
    }

    /// <summary>
    /// Double of the persistence of the snapshot of the client (AD-1).
    /// </summary>
    private sealed class FakeSnapshotStore : ICatalogSnapshotStore
    {
        public long AppliedVersion { get; init; }

        public CatalogSnapshot? Snapshot { get; init; }

        public Task<CatalogSnapshot?> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Snapshot);

        public Task<long> GetAppliedVersionAsync(CancellationToken cancellationToken) =>
            Task.FromResult(AppliedVersion);

        public Task<DateTimeOffset?> GetLastSynchronizedAtAsync(CancellationToken cancellationToken) =>
            Task.FromResult<DateTimeOffset?>(null);

        public Task SaveAsync(CatalogSnapshot snapshot, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<InstalledGame>> LoadInstalledGamesAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<InstalledGame>>([]);

        public Task<IReadOnlyList<ActiveOperation>> LoadActiveOperationsAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ActiveOperation>>([]);
    }

    /// <summary>
    /// Double of the reception of the trigger de un cambio del catálogo (AD-5).
    /// </summary>
    private sealed class FakeChangeNotifier : ICatalogChangeNotifier
    {
        private Func<CatalogChanged, CancellationToken, Task>? _handler;

        public Task SubscribeAsync(
            Func<CatalogChanged, CancellationToken, Task> handler,
            CancellationToken cancellationToken)
        {
            _handler = handler;

            return Task.CompletedTask;
        }

        public async Task RaiseAsync(CatalogChanged changed, CancellationToken cancellationToken)
        {
            Assert.NotNull(_handler);

            await _handler!(changed, cancellationToken);
        }
    }
}
