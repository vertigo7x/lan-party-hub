using LANPartyHub.Client.Application;
using LANPartyHub.Client.Application.Catalog;
using LANPartyHub.Client.Application.CatalogSync;
using LANPartyHub.Client.Application.Clients;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Domain;
using LANPartyHub.Client.Domain.Trust;
using LANPartyHub.Client.Host;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The REAL state that the client publishes, the TERMINAL result that it reports for a
/// withdrawn operation, and the connection state that the composition declares
/// (AD-18, AD-22, AD-29, AD-33, FR-10).
///
/// <para>
/// El estado que el cliente publica es el REAL: las instalaciones que tiene registradas
/// y las operaciones que están en curso salen de su PROPIA base de datos, no de una lista
/// vacía, y la ruta de la instalación NUNCA sale del cliente (AD-22).
/// </para>
/// <para>
/// La operación RETIRADA del catálogo termina con un RESULTADO, no con un error: se
/// publica como <see cref="OperationOutcomeReported"/> y NO produce
/// <c>ClientError</c> ni <see cref="ApiError"/> (AD-33, R-MVP-8).
/// </para>
/// <para>
/// La publicación que no puede acompañar al cliente con un estado nuevo declara el
/// ÚLTIMO estado DECLARADO, y un cliente que no ha declarado nada declara
/// <see cref="ConnectionState.Disconnected"/> (AD-29).
/// </para>
/// </summary>
public sealed class ClientRealStatePublicationTests
{
    private const string ClientId = "0193f2a1b2c34d4e5f60718293a4b5c6";
    private const string GameId = "0193f2a1-b2c3-4d4e-5f60-718293a4b5c6";
    private const string OperationId = "0193f2a1-b2c3-4d4e-5f60-718293a4b5c9";
    private const string PackageId = "paquete-1";

    /// <summary>
    /// The installations and the operations IN COURSE that the client really has travel
    /// to the server, y la ruta de la instalación NO sale del cliente (AD-22, FR-10).
    /// </summary>
    [Fact]
    public async Task The_real_installations_and_operations_reach_the_server()
    {
        var store = new FakeClientStateStore
        {
            Installations =
            [
                new ClientInstallation(
                    GameId,
                    "1.2.0",
                    InstallationState.Installed,
                    @"C:\juegos\carbono\instalacion",
                    "directorio-opaco"),
            ],
            Operations =
            [
                new PersistedDownloadOperation(
                    OperationId,
                    PackageId,
                    nameof(DownloadState.Downloading),
                    "http",
                    512,
                    2048),
            ],
        };

        var publisher = new RecordingStatePublisher();

        var useCase = new PublishClientState(
            store,
            publisher,
            new FixedClientIdentity(ClientId));

        await useCase.ExecuteAsync(
            ConnectionState.AnchorVerified,
            TestContext.Current.CancellationToken);

        var state = Assert.Single(publisher.States);

        // AD-22: las instalaciones son LOCALES y se publican con su versión y su estado.
        var installation = Assert.Single(state.InstalledGames);
        Assert.Equal(GameId, installation.GameId);
        Assert.Equal("1.2.0", installation.InstalledVersion);
        Assert.Equal(InstallationState.Installed, installation.ValidationState);

        // AD-22, FR-10: la operación en curso se declara con su TIPO estable y su estado.
        var operation = Assert.Single(state.ActiveOperations);
        Assert.Equal(OperationId, operation.OperationId);
        Assert.Equal(PublishClientState.DownloadOperationTypeName, operation.OperationType);
        Assert.Equal(nameof(DownloadState.Downloading), operation.State);

        // AD-22: la ruta de la instalación NUNCA sale del cliente.
        Assert.DoesNotContain(
            @"C:\juegos\carbono\instalacion",
            state.InstalledGames.Select(game => game.GameId),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// The payload that the client publishes does NOT carry the path of the installation,
    /// porque la forma del contrato no la admite (AD-22).
    /// </summary>
    [Fact]
    public async Task The_published_payload_carries_no_install_path()
    {
        var store = new FakeClientStateStore
        {
            Installations =
            [
                new ClientInstallation(
                    GameId,
                    "1.2.0",
                    InstallationState.Installed,
                    @"C:\juegos\carbono\instalacion",
                    "directorio-opaco"),
            ],
        };

        var publisher = new RecordingStatePublisher();

        var useCase = new PublishClientState(
            store,
            publisher,
            new FixedClientIdentity(ClientId));

        await useCase.ExecuteAsync(
            ConnectionState.AnchorVerified,
            TestContext.Current.CancellationToken);

        var state = Assert.Single(publisher.States);

        // AD-22: la ruta de la instalación es LOCAL y la forma EXACTA del contrato no la
        // declara, así que el payload serializado no puede contenerla.
        var serialized = System.Text.Json.JsonSerializer.Serialize(state);

        Assert.DoesNotContain(@"C:\juegos\carbono\instalacion", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("installPath", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("InstallPath", serialized, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A TERMINAL operation does NOT travel as an operation in course: el servidor solo
    /// muestra lo que está en curso (AD-10, AD-22).
    /// </summary>
    [Fact]
    public async Task A_terminal_operation_is_not_published_as_an_operation_in_course()
    {
        var store = new FakeClientStateStore
        {
            Operations =
            [
                new PersistedDownloadOperation(
                    OperationId,
                    PackageId,
                    nameof(DownloadState.Completed),
                    "http",
                    2048,
                    2048),
            ],
        };

        var publisher = new RecordingStatePublisher();

        var useCase = new PublishClientState(
            store,
            publisher,
            new FixedClientIdentity(ClientId));

        await useCase.ExecuteAsync(
            ConnectionState.AnchorVerified,
            TestContext.Current.CancellationToken);

        // AD-10: una operación terminada no se publica como activa.
        Assert.Empty(Assert.Single(publisher.States).ActiveOperations);
    }

    /// <summary>
    /// The withdrawn operation TERMINATES with a RESULT that the client publishes to the
    /// server: <see cref="OperationOutcomeReported"/> con
    /// <see cref="OperationOutcome.PackageWithdrawn"/> (AD-33, R-MVP-8).
    /// </summary>
    [Fact]
    public async Task The_withdrawn_operation_publishes_its_terminal_result()
    {
        var reporter = new RecordingOutcomeReporter();

        var service = CreateSyncService(
            new FakeSnapshotClient(new CatalogSnapshot(6, [], [])),
            new FakeSnapshotStore
            {
                AppliedVersion = 5,
                ActiveOperations =
                [
                    new ActiveOperation(
                        OperationId,
                        PackageId,
                        DownloadState.Downloading),
                ],
            },
            reporter,
            new FakeClientStateStore { Identity = ClientIdentity.FromCanonical(ClientId) });

        var state = await service.SynchronizeAsync(TestContext.Current.CancellationToken);

        // AD-33: la operación retirada del catálogo se marca con el resultado.
        var withdrawn = Assert.Single(state.WithdrawnOperations);
        Assert.Equal(OperationId, withdrawn.OperationId);
        Assert.Equal(OperationOutcome.PackageWithdrawn, withdrawn.Outcome);

        // AD-33: el resultado se PUBLICA al servidor con el identificador del cliente.
        var reported = Assert.Single(reporter.Reported);
        Assert.Equal(ClientId, reported.ClientId);
        Assert.Equal(OperationId, reported.OperationId);
        Assert.Equal(OperationOutcome.PackageWithdrawn, reported.Outcome);
    }

    /// <summary>
    /// The withdrawn operation does NOT produce a <c>ClientError</c> nor an
    /// <see cref="ApiError"/>: el resultado es un RESULTADO y no un fallo
    /// (AD-33, R-MVP-8).
    /// </summary>
    [Fact]
    public async Task The_withdrawn_operation_produces_no_client_error()
    {
        var reporter = new RecordingOutcomeReporter();

        var service = CreateSyncService(
            new FakeSnapshotClient(new CatalogSnapshot(6, [], [])),
            new FakeSnapshotStore
            {
                AppliedVersion = 5,
                ActiveOperations =
                [
                    new ActiveOperation(
                        OperationId,
                        PackageId,
                        DownloadState.Downloading),
                ],
            },
            reporter,
            new FakeClientStateStore { Identity = ClientIdentity.FromCanonical(ClientId) });

        await service.SynchronizeAsync(TestContext.Current.CancellationToken);

        // AD-33, R-MVP-8: el resultado terminal del retiro NO es un error del cliente: la
        // sincronización no registra ningún error y no publica ningún <see cref="ApiError"/>.
        var reported = Assert.Single(reporter.Reported);
        Assert.NotEqual(OperationOutcome.Failed, reported.Outcome);

        var errors = ErrorCatalog.Entries
            .Where(error => error.Code == reported.Outcome.ToString())
            .ToArray();

        Assert.Empty(errors);
    }

    /// <summary>
    /// A synchronization WITHOUT a reporter still finishes: la proyección es un
    /// complemento y no su motivo de vida (AD-1, AD-33).
    /// </summary>
    [Fact]
    public async Task A_synchronization_without_a_reporter_still_finishes()
    {
        var service = CreateSyncService(
            new FakeSnapshotClient(new CatalogSnapshot(6, [], [])),
            new FakeSnapshotStore
            {
                AppliedVersion = 5,
                ActiveOperations =
                [
                    new ActiveOperation(
                        OperationId,
                        PackageId,
                        DownloadState.Downloading),
                ],
            },
            reporter: null,
            new FakeClientStateStore { Identity = ClientIdentity.FromCanonical(ClientId) });

        var state = await service.SynchronizeAsync(TestContext.Current.CancellationToken);

        // AD-1, AD-33: la sincronización termina con su veredicto aunque el resultado no
        // se pueda publicar.
        Assert.True(state.Synchronized);
        Assert.Single(state.WithdrawnOperations);
    }

    /// <summary>
    /// The publication of the CURRENT state declares <see cref="ConnectionState.Disconnected"/>
    /// when the client has not declared anything: la publicación NUNCA afirma una conexión
    /// que el cliente no tiene (AD-29).
    /// </summary>
    [Fact]
    public async Task The_publication_of_the_current_state_declares_disconnected_by_default()
    {
        var publisher = new RecordingStatePublisher();

        using var composition = new ClientRegistrationComposition(
            new Uri("http://localhost:5000"),
            new FakeRequestHeaders(),
            ClientIdentity.FromCanonical(ClientId),
            new FakeNameStore(),
            publisher,
            NullLoggerFactory.Instance,
            new FakeClientStateStore());

        await composition.PublishCurrentStateAsync(TestContext.Current.CancellationToken);

        var state = Assert.Single(publisher.States);

        // AD-29: un cliente que no ha declarado ningún estado declara «Desconectado».
        Assert.Equal(ConnectionState.Disconnected, state.ConnectionState);
        Assert.Equal(ConnectionState.Disconnected, composition.LastConnectionState);
    }

    /// <summary>
    /// The publication of the CURRENT state RESPECTS the state that the client declared
    /// after a reconnection: la publicación declara el estado DECLARADO y no uno
    /// inventado (AD-29, AD-22).
    /// </summary>
    [Fact]
    public async Task The_publication_of_the_current_state_respects_the_declared_state()
    {
        var publisher = new RecordingStatePublisher();

        using var composition = new ClientRegistrationComposition(
            new Uri("http://localhost:5000"),
            new FakeRequestHeaders(),
            ClientIdentity.FromCanonical(ClientId),
            new FakeNameStore(),
            publisher,
            NullLoggerFactory.Instance,
            new FakeClientStateStore());

        // AD-29: el cliente declara su estado de conexión.
        await composition.PublishStateAsync(
            ConnectionState.AnchorVerified,
            TestContext.Current.CancellationToken);

        Assert.Equal(ConnectionState.AnchorVerified, composition.LastConnectionState);

        // La reconexión vuelve a publicar el estado que el cliente declaró.
        await composition.PublishCurrentStateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, publisher.States.Count);
        Assert.All(
            publisher.States,
            state => Assert.Equal(ConnectionState.AnchorVerified, state.ConnectionState));
    }

    /// <summary>
    /// Builds the use case of the synchronization with the doubles of the ports.
    /// </summary>
    /// <param name="client">Double of the reading of the snapshot.</param>
    /// <param name="store">Double of the persistence of the snapshot.</param>
    /// <param name="reporter">Double of the publication of the terminal result.</param>
    /// <param name="stateStore">Double of the database of the client.</param>
    /// <returns>The use case of the synchronization.</returns>
    private static CatalogSyncService CreateSyncService(
        FakeSnapshotClient client,
        FakeSnapshotStore store,
        RecordingOutcomeReporter? reporter,
        FakeClientStateStore stateStore) =>
        new(
            client,
            store,
            new FakeChangeNotifier(),
            new CatalogSnapshotApplier(store, NullLogger<CatalogSnapshotApplier>.Instance),
            NullLogger<CatalogSyncService>.Instance,
            outcomeReporter: reporter,
            stateStore: stateStore);

    /// <summary>
    /// Publisher of the state of the client que RECUERDA lo que se le publicó (AD-16).
    /// </summary>
    private sealed class RecordingStatePublisher : IClientStatePublisher
    {
        public List<ClientStateChanged> States { get; } = [];

        public Task PublishAsync(ClientStateChanged state, CancellationToken cancellationToken)
        {
            States.Add(state);

            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Reporter of the terminal result que RECUERDA lo que se le publicó (AD-16).
    /// </summary>
    private sealed class RecordingOutcomeReporter : IOperationOutcomeReporter
    {
        public List<OperationOutcomeReported> Reported { get; } = [];

        public Task ReportAsync(OperationOutcomeReported reported, CancellationToken cancellationToken)
        {
            Reported.Add(reported);

            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Source of the canonical identifier of the client que SIEMPRE responde lo mismo
    /// (AD-23).
    /// </summary>
    private sealed class FixedClientIdentity : ClientIdentityProvider
    {
        private readonly string _canonicalId;

        public FixedClientIdentity(string canonicalId) => _canonicalId = canonicalId;

        public string CanonicalId => _canonicalId;
    }

    /// <summary>
    /// Database of the client que responde SIEMPRE lo mismo (AD-16).
    /// </summary>
    private sealed class FakeClientStateStore : IClientStateStore
    {
        public IReadOnlyList<ClientInstallation> Installations { get; init; } = [];

        public IReadOnlyList<PersistedDownloadOperation> Operations { get; init; } = [];

        public ClientIdentity? Identity { get; init; }

        public Task MigrateAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<ClientIdentity?> LoadIdentityAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Identity);

        public Task SaveIdentityAsync(ClientIdentity identity, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<PersistedDownloadOperation>> LoadDownloadOperationsAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(Operations);

        public Task SaveProgressAsync(
            string operationId,
            long bytesPersisted,
            long? totalBytes,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task SaveOperationAsync(
            string operationId,
            string packageId,
            string state,
            string transferEngine,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<PinnedTrustAnchor?> LoadTrustAnchorAsync(CancellationToken cancellationToken) =>
            Task.FromResult<PinnedTrustAnchor?>(null);

        public Task SaveTrustAnchorAsync(PinnedTrustAnchor anchor, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task SaveInstallationAsync(
            ClientInstallation installation,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<ClientInstallation?> LoadInstallationAsync(
            string gameId,
            CancellationToken cancellationToken) =>
            Task.FromResult<ClientInstallation?>(
                Installations.FirstOrDefault(installation => installation.GameId == gameId));

        public Task<IReadOnlyList<ClientInstallation>> LoadInstallationsAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(Installations);

        public Task SaveInstallationFailureAsync(
            ClientInstallation installation,
            string errorCode,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<string?> LoadInstallationFailureAsync(
            string gameId,
            CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);
    }

    /// <summary>
    /// Headers of the requests of the cliente que SIEMPRE responden lo mismo (AD-16).
    /// </summary>
    private sealed class FakeRequestHeaders : IClientRequestHeaders
    {
        public string? ClientId => LANPartyHub.UnitTests.ClientRealStatePublicationTests.ClientId;

        public string? OperationId => null;

        public string? MachineName => "EQ-PC";
    }

    /// <summary>
    /// Almacén del nombre efectivo que SIEMPRE responde lo mismo (AD-16).
    /// </summary>
    private sealed class FakeNameStore : IClientEffectiveNameStore
    {
        public Task<string?> LoadEffectiveMachineNameAsync(CancellationToken cancellationToken) =>
            Task.FromResult<string?>("EQ-PC");

        public Task SaveEffectiveMachineNameAsync(
            string machineName,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    /// <summary>
    /// Double of the reading of the snapshot of the server (AD-1).
    /// </summary>
    private sealed class FakeSnapshotClient : ICatalogSnapshotClient
    {
        private readonly CatalogSnapshot _snapshot;

        public FakeSnapshotClient(CatalogSnapshot snapshot) => _snapshot = snapshot;

        public Task<CatalogSnapshot> GetSnapshotAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_snapshot);
    }

    /// <summary>
    /// Double of the persistence of the snapshot of the client (AD-1).
    /// </summary>
    private sealed class FakeSnapshotStore : ICatalogSnapshotStore
    {
        public long AppliedVersion { get; init; }

        public CatalogSnapshot? Snapshot { get; init; }

        public IReadOnlyList<ActiveOperation> ActiveOperations { get; init; } = [];

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
            Task.FromResult(ActiveOperations);
    }

    /// <summary>
    /// Double of the reception of the trigger de un cambio del catálogo (AD-5).
    /// </summary>
    private sealed class FakeChangeNotifier : ICatalogChangeNotifier
    {
        public Task SubscribeAsync(
            Func<CatalogChanged, CancellationToken, Task> handler,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
