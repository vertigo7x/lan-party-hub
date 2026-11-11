using LANPartyHub.Client.Application;
using LANPartyHub.Client.Application.CatalogSync;
using LANPartyHub.Client.Application.Downloads;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Domain;
using LANPartyHub.Client.Domain.StateMachines;
using LANPartyHub.Client.Domain.Trust;
using LANPartyHub.Client.Infrastructure.Networking;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// Recovery of the download operations after a restart of the machine
/// (AC1, AC2, AC3, AC5, AD-6, AD-11, AD-20, AD-22, AD-24, SM-2).
///
/// <para>
/// La recuperación se ejercita con dobles de los PUERTOS: así el offset sembrado, el
/// <c>Range</c> de la reanudación, la forma exacta del evento y el diagnóstico de
/// incompatibilidad son observables sin un servidor y sin un disco (AD-1, AD-16).
/// </para>
/// </summary>
public sealed class DownloadRecoveryTests : IDisposable
{
    private const string ContentUri = "http://192.168.1.10:5000/api/v1/packages/paquete-1/content";

    private readonly List<string> _temporaryFolders = [];

    [Fact]
    public async Task A_persisted_operation_is_rehydrated_with_its_persisted_offset()
    {
        // AC2, AD-11: la operación recuperada conserva su operationId y su packageId, y
        // su offset efectivo es el que la reconciliación YA corrigió.
        var store = new RecordingStateStore();
        store.PersistedOperations.Add(
            Persisted("operacion-1", "paquete-1", DownloadState.Downloading, 3_200_000_000, 7_600_000_000));

        var coordinator = CreateCoordinator(
            new RecordingStateStoreWithOperations(store),
            new RecordingIntentClient(),
            new RecordingTransferEngine(totalBytes: 7_600_000_000));

        var recovered = await coordinator.RecoverAsync(
            _ => new Uri(ContentUri),
            TestContext.Current.CancellationToken);

        var handle = Assert.Single(recovered);

        Assert.Equal("operacion-1", handle.OperationId);
        Assert.Equal("paquete-1", handle.PackageId);
        Assert.Equal(3_200_000_000, handle.BytesPersisted);
        Assert.Equal(7_600_000_000, handle.TotalBytes);
    }

    [Fact]
    public async Task A_recovered_operation_never_registers_a_new_intention()
    {
        // AC1: la intención ya está registrada, así que la recuperación NO la duplica.
        var store = new RecordingStateStore();
        store.PersistedOperations.Add(
            Persisted("operacion-1", "paquete-1", DownloadState.Downloading, 100, 1000));

        var intents = new RecordingIntentClient();

        var coordinator = CreateCoordinator(new RecordingStateStoreWithOperations(store), intents);

        await coordinator.RecoverAsync(_ => new Uri(ContentUri), TestContext.Current.CancellationToken);

        Assert.Null(intents.RegisteredPackageId);
    }

    [Fact]
    public async Task The_resume_asks_for_the_range_of_the_persisted_offset()
    {
        // AC2, AD-20: la reanudación parte del offset persistido con Range, no de cero.
        var store = new RecordingStateStore();
        store.PersistedOperations.Add(
            Persisted("operacion-1", "paquete-1", DownloadState.Downloading, 3_200_000_000, 7_600_000_000));

        var engine = new RecordingTransferEngine(totalBytes: 7_600_000_000);

        var coordinator = CreateCoordinator(
            new RecordingStateStoreWithOperations(store),
            new RecordingIntentClient(),
            engine);

        await coordinator.RecoverAsync(_ => new Uri(ContentUri), TestContext.Current.CancellationToken);

        await WaitUntilAsync(() => engine.RequestedStarts.Count > 0);

        // El rango empieza en el offset persistido: NUNCA en cero (AC2, AD-20).
        Assert.Equal(3_200_000_000, engine.RequestedStarts[0]);
    }

    [Fact]
    public async Task The_persisted_offset_greater_than_the_partial_is_the_length_of_the_partial()
    {
        // AC2, AD-11: el offset efectivo es la INTERSECCIÓN de lo persistido y lo que el
        // archivo parcial contiene. Un registro divergente se descarta en vez de asumir
        // que el archivo está completo.
        var store = new RecordingStateStore();
        store.PersistedOperations.Add(
            Persisted("operacion-1", "paquete-1", DownloadState.Downloading, 900, 1000));

        var engine = new RecordingTransferEngine(totalBytes: 1000);

        var coordinator = CreateCoordinator(
            new RecordingStateStoreWithOperations(store),
            new RecordingIntentClient(),
            engine);

        var recovered = await coordinator.RecoverAsync(
            _ => new Uri(ContentUri),
            TestContext.Current.CancellationToken);

        var handle = Assert.Single(recovered);

        // El offset corregido es el que la reconciliación escribió, así que la
        // rehidratación lo LEE y no lo recalcula (AD-11).
        Assert.Equal(900, handle.BytesPersisted);
    }

    [Fact]
    public async Task A_terminal_operation_is_not_recovered()
    {
        // AD-10, AC5: una descarga completada, cancelada o fallida no se reanuda.
        var store = new RecordingStateStore();
        store.PersistedOperations.Add(
            Persisted("operacion-1", "paquete-1", DownloadState.Completed, 1000, 1000));
        store.PersistedOperations.Add(
            Persisted("operacion-2", "paquete-2", DownloadState.Cancelled, 500, 1000));
        store.PersistedOperations.Add(
            Persisted("operacion-3", "paquete-3", DownloadState.Failed, 500, 1000));

        var engine = new RecordingTransferEngine(totalBytes: 1000);

        var coordinator = CreateCoordinator(
            new RecordingStateStoreWithOperations(store),
            new RecordingIntentClient(),
            engine);

        var recovered = await coordinator.RecoverAsync(
            _ => new Uri(ContentUri),
            TestContext.Current.CancellationToken);

        Assert.Empty(recovered);
        Assert.Empty(engine.RequestedStarts);
    }

    [Fact]
    public async Task An_operation_of_an_unknown_engine_is_not_recovered()
    {
        // AD-24: la reconciliación ya rechazó las operaciones de un motor que no existe
        // en la versión activa, y la recuperación no las rehidrata.
        var store = new RecordingStateStore();
        store.PersistedOperations.Add(
            Persisted("operacion-1", "paquete-1", DownloadState.Downloading, 100, 1000, "Torrent"));

        var coordinator = CreateCoordinator(
            new RecordingStateStoreWithOperations(store),
            new RecordingIntentClient());

        var recovered = await coordinator.RecoverAsync(
            _ => new Uri(ContentUri),
            TestContext.Current.CancellationToken);

        Assert.Empty(recovered);
    }

    [Fact]
    public async Task The_progress_is_reported_with_the_exact_shape_of_the_contract()
    {
        // AC5, AD-22, AR-3: `DownloadProgressUpdated` lleva el clientId, el operationId,
        // los bytes transferidos, el total y el estado, con la forma EXACTA del contrato.
        var store = new RecordingStateStore();
        store.PersistedOperations.Add(
            Persisted("operacion-1", "paquete-1", DownloadState.Downloading, 3_200_000_000, 7_600_000_000));

        var reporter = new RecordingProgressReporter();

        var coordinator = CreateCoordinator(
            new RecordingStateStoreWithOperations(store),
            new RecordingIntentClient(),
            new RecordingTransferEngine(totalBytes: 7_600_000_000),
            reporter: reporter);

        await coordinator.RecoverAsync(_ => new Uri(ContentUri), TestContext.Current.CancellationToken);

        await coordinator.WaitForPendingReportsAsync();

        Assert.NotEmpty(reporter.Reports);

        // La forma del contrato es EXACTA: los cinco campos y ni uno más (AD-18, AR-3).
        Assert.Equal(
            new[] { "ClientId", "OperationId", "BytesTransferred", "TotalBytes", "State" },
            typeof(DownloadProgressUpdated).GetProperties().Select(property => property.Name));

        // TODOS los eventos publicados llevan los mismos valores, porque el estado y el
        // offset de la operación son los mismos (AC5).
        Assert.All(reporter.Reports, report =>
        {
            Assert.Equal(ClientIdentity.Emit().CanonicalId.Length, report.ClientId.Length);
            Assert.Equal("operacion-1", report.OperationId);
            Assert.Equal(3_200_000_000, report.BytesTransferred);
            Assert.Equal(7_600_000_000, report.TotalBytes);
        });

        Assert.All(
            reporter.Reports,
            report => Assert.True(
                report.State is DownloadState.Paused or DownloadState.Downloading));
    }

    [Fact]
    public async Task A_recovered_operation_that_was_downloading_resumes_from_the_persisted_offset()
    {
        // AD-10, AD-20: una operación que estaba DESCARGANDO fue interrumpida por el
        // reinicio. La recuperación la lleva al estado real («Pausada») con la
        // transición DECLARADA de la tabla de la máquina, y la reanudación sigue desde el
        // offset persistido.
        var store = new RecordingStateStore();
        store.PersistedOperations.Add(
            Persisted("operacion-1", "paquete-1", DownloadState.Downloading, 100, 1000));

        var engine = new RecordingTransferEngine(totalBytes: 1000);

        var coordinator = CreateCoordinator(
            new RecordingStateStoreWithOperations(store),
            new RecordingIntentClient(),
            engine);

        var recovered = await coordinator.RecoverAsync(
            _ => new Uri(ContentUri),
            TestContext.Current.CancellationToken);

        var handle = Assert.Single(recovered);

        Assert.Equal(DownloadState.Downloading, handle.StateMachine.Current);

        await WaitUntilAsync(() => engine.RequestedStarts.Count > 0);

        // La reanudación NUNCA vuelve a cero (AC2, AD-20).
        Assert.Equal(100, engine.RequestedStarts[0]);
    }

    [Fact]
    public async Task The_resume_of_a_paused_operation_keeps_the_offset()
    {
        // AC3: una operación que estaba PAUSADA se reanuda desde el offset persistido.
        var store = new RecordingStateStore();
        store.PersistedOperations.Add(
            Persisted("operacion-1", "paquete-1", DownloadState.Paused, 250, 1000));

        var engine = new RecordingTransferEngine(totalBytes: 1000);

        var coordinator = CreateCoordinator(
            new RecordingStateStoreWithOperations(store),
            new RecordingIntentClient(),
            engine);

        await coordinator.RecoverAsync(_ => new Uri(ContentUri), TestContext.Current.CancellationToken);

        await WaitUntilAsync(() => engine.RequestedStarts.Count > 0);

        Assert.Equal(250, engine.RequestedStarts[0]);
    }

    [Fact]
    public async Task An_incompatible_client_does_not_resume_the_download()
    {
        // AD-6, AC5: un cliente incompatible NO reanuda y recibe el diagnóstico de
        // incompatibilidad de versión.
        var store = new RecordingStateStore();
        store.PersistedOperations.Add(
            Persisted("operacion-1", "paquete-1", DownloadState.Downloading, 100, 1000));

        var engine = new RecordingTransferEngine(totalBytes: 1000);

        var coordinator = CreateCoordinator(
            new RecordingStateStoreWithOperations(store),
            new RecordingIntentClient(),
            engine,
            versionProbe: new FixedVersionProbe(compatible: false, serverVersion: "9.9.9"));

        var gate = await coordinator.CheckCompatibilityAsync(
            new ServerEndpoint("192.168.1.10", 5000),
            TestContext.Current.CancellationToken);

        Assert.True(gate.IsIncompatible);
        Assert.False(gate.MayDownload);
        Assert.Equal("9.9.9", gate.ServerVersion);

        // El diagnóstico sale del catálogo de errores y nombra el siguiente paso (AD-15).
        var error = ErrorCatalog.Resolve(ErrorCatalog.ClientVersionIncompatibleCode);

        Assert.Equal("CLIENT_VERSION_INCOMPATIBLE", error.Code);
        Assert.NotEmpty(error.Message);
        Assert.NotEmpty(error.NextStep);
    }

    [Fact]
    public async Task An_unreachable_server_is_not_an_incompatibility()
    {
        // AC3, UX-DR-36: un servidor que no responde NO es un rechazo, así que el jugador
        // tiene que poder reintentar.
        var coordinator = CreateCoordinator(
            new RecordingStateStore(),
            new RecordingIntentClient(),
            versionProbe: new FixedVersionProbe(compatible: false, serverVersion: null, reachable: false));

        var gate = await coordinator.CheckCompatibilityAsync(
            new ServerEndpoint("192.168.1.10", 5000),
            TestContext.Current.CancellationToken);

        Assert.False(gate.IsIncompatible);
        Assert.False(gate.IsReachable);
        Assert.True(gate.MayDownload);
    }

    [Fact]
    public async Task The_rejection_of_the_server_is_a_typed_incompatibility()
    {
        // AD-6, AC5: un 426 lanza la excepción TIPADA y no un error genérico de la red, y
        // la descarga NO se inicia.
        var intents = new RecordingIntentClient();

        using var httpClient = new HttpClient(new RejectingHandler(HttpStatusCode.UpgradeRequired, "9.9.9"))
        {
            BaseAddress = new Uri("http://192.168.1.10:5000"),
        };

        var adapter = new DownloadIntentHttpClient(httpClient, new FixedVersionProvider("1.2.3"));

        var failure = await Assert.ThrowsAsync<ClientVersionIncompatibleException>(
            () => adapter.RegisterAsync("paquete-1", TestContext.Current.CancellationToken));

        Assert.Equal("1.2.3", failure.ClientVersion);
        Assert.Equal("9.9.9", failure.ServerVersion);
        Assert.Equal(ErrorCatalog.ClientVersionIncompatibleCode, failure.Error.Code);
        Assert.Null(intents.RegisteredPackageId);
    }

    [Fact]
    public void The_resuming_microcopy_is_not_a_new_state()
    {
        // AD-10, AD-25: «Reanudando desde …» es MICROCOPY, no un estado. La tabla de
        // estados de Shared no crece.
        Assert.Equal("Reanudando desde ", LANPartyHub.Client.Presentation.Downloads.DownloadProgressViewModel.ResumingPrefixText);

        Assert.Equal(
            ["Queued", "Downloading", "Paused", "Validating", "Completed", "Cancelled", "Failed"],
            Enum.GetNames<DownloadState>());
    }

    [Fact]
    public void The_resumed_operation_is_presented_with_the_offset_it_continues_from()
    {
        // AC2, UX-DR-45: la operación se presenta como «Reanudando desde 42 % · 3,2 GB de
        // 7,6 GB».
        var viewModel = new LANPartyHub.Client.Presentation.Downloads.DownloadProgressViewModel();

        var handle = DownloadRecovery.Rehydrate(
            Persisted("operacion-1", "paquete-1", DownloadState.Paused, 3_200_000_000, 7_600_000_000),
            new Uri(ContentUri),
            DownloadState.Paused);

        viewModel.Apply(handle);

        viewModel.ApplyResuming(true);

        Assert.True(viewModel.IsResuming);
        Assert.Equal("Reanudando desde 42 % · 3,2 GB de 7,6 GB", viewModel.ProgressTextWithResume);

        // El porcentaje y el par de tamaños siguen saliendo de UNA SOLA fuente (UX-DR-5).
        Assert.Equal("42 % · 3,2 GB de 7,6 GB", viewModel.ProgressText);
    }

    [Fact]
    public async Task The_offset_is_persisted_while_the_download_is_in_course()
    {
        // AD-11, SM-2: el offset se persiste MIENTRAS la descarga está en curso, así que
        // una descarga que se interrumpe a mitad conserva lo que ya está en el disco.
        var persisted = new List<long>();

        await using var counting = new ProgressCountingStream(
            new MemoryStream(),
            (accumulated, _) =>
            {
                persisted.Add(accumulated);

                return Task.CompletedTask;
            });

        var chunk = new byte[ProgressCountingStream.PersistenceInterval];

        await counting.WriteAsync(chunk.AsMemory(), TestContext.Current.CancellationToken);

        // El offset persistido es ACUMULADO y NUNCA es cero: es lo que hace que la
        // reanudación no vuelva a cero (SM-2).
        Assert.NotEmpty(persisted);
        Assert.Equal(ProgressCountingStream.PersistenceInterval, persisted[0]);
        Assert.Equal(ProgressCountingStream.PersistenceInterval, counting.Written);
    }

    [Fact]
    public async Task The_counter_of_the_progress_starts_from_the_initial_offset()
    {
        // AD-24: el contador es ACUMULADO, así que una reanudación no vuelve a contar
        // desde cero lo que ya estaba en el parcial.
        var persisted = new List<long>();

        await using var counting = new ProgressCountingStream(
            new MemoryStream(),
            (accumulated, _) =>
            {
                persisted.Add(accumulated);

                return Task.CompletedTask;
            },
            initialOffset: 3_000);

        var chunk = new byte[ProgressCountingStream.PersistenceInterval];

        await counting.WriteAsync(chunk.AsMemory(), TestContext.Current.CancellationToken);

        Assert.Equal(3_000 + ProgressCountingStream.PersistenceInterval, persisted[0]);
    }

    [Fact]
    public async Task A_transport_failure_leaves_the_operation_resumable()
    {
        // AD-11, AD-24, SM-2: si el servidor NO está, el fallo NO es una falla de la
        // operación: la operación conserva su estado REANUDABLE, así que el siguiente
        // arranque la recupera y reintenta el Range desde el offset persistido.
        var store = new RecordingStateStore();

        var coordinator = CreateCoordinator(
            store,
            new RecordingIntentClient(),
            new FailingTransferEngine(new TransferTransportException("El servidor no está.")));

        var handle = await coordinator.StartAsync(
            "paquete-1",
            new Uri(ContentUri),
            TestContext.Current.CancellationToken);

        await WaitUntilAsync(() => handle.StateMachine.Current != DownloadState.Downloading);

        // El estado NO es terminal: <c>Failed</c> no se recuperaria nunca más.
        Assert.Equal(DownloadState.Paused, handle.StateMachine.Current);
        Assert.False(handle.StateMachine.IsTerminal);

        // El estado persistido es REANUDABLE, así que la recuperación lo recoge.
        Assert.True(DownloadRecovery.IsResumableState(DownloadState.Paused));
    }

    [Fact]
    public async Task A_transport_failure_persists_a_resumable_state_and_not_a_failed_one()
    {
        // AD-11, AD-24: lo que se persiste decide si la operación se recupera. Un
        // <c>Failed</c> escrito en la fila deja el parcial huérfano, así que un fallo de
        // transporte NO puede persistir ese estado.
        var store = new RecordingStateStore();

        var coordinator = CreateCoordinator(
            store,
            new RecordingIntentClient(),
            new FailingTransferEngine(new TransferTransportException("El servidor no está.")));

        var handle = await coordinator.StartAsync(
            "paquete-1",
            new Uri(ContentUri),
            TestContext.Current.CancellationToken);

        await WaitUntilAsync(() => store.SavedOperations.Any(saved => saved.State == DownloadState.Paused.ToString()));

        Assert.DoesNotContain(store.SavedOperations, saved => saved.State == DownloadState.Failed.ToString());
    }

    [Fact]
    public async Task A_transport_failure_leaves_the_operation_recoverable_after_a_restart()
    {
        // AC2, SM-2: el ciclo completo. La descarga falla por transporte, el estado
        // persistido es reanudable, y el siguiente arranque RECUPERA la operación y pide
        // el Range desde el offset persistido en vez de dejar el parcial huérfano.
        var store = new RecordingStateStore();

        var coordinator = CreateCoordinator(
            store,
            new RecordingIntentClient(),
            new FailingTransferEngine(new TransferTransportException("El servidor no está.")));

        var handle = await coordinator.StartAsync(
            "paquete-1",
            new Uri(ContentUri),
            TestContext.Current.CancellationToken);

        await WaitUntilAsync(() => store.SavedOperations.Any(saved => saved.State == DownloadState.Paused.ToString()));

        var persisted = store.SavedOperations.Last(saved => saved.State == DownloadState.Paused.ToString());

        // El SIGUIENTE arranque: la operación persistida se recupera porque su estado es
        // reanudable (AC2, AD-10).
        var nextBoot = new RecordingStateStore();
        nextBoot.PersistedOperations.Add(
            new PersistedDownloadOperation(
                persisted.OperationId,
                persisted.PackageId,
                persisted.State,
                TransferEngineRegistry.HttpRangeTransferEngine,
                300,
                1000));

        var engine = new RecordingTransferEngine(totalBytes: 1000);

        var recoveredCoordinator = CreateCoordinator(
            new RecordingStateStoreWithOperations(nextBoot),
            new RecordingIntentClient(),
            engine);

        var recovered = await recoveredCoordinator.RecoverAsync(
            _ => new Uri(ContentUri),
            TestContext.Current.CancellationToken);

        Assert.Single(recovered);

        await WaitUntilAsync(() => engine.RequestedStarts.Count > 0);

        // La reanudación pide el rango desde el offset persistido: NUNCA desde cero.
        Assert.Equal(300, engine.RequestedStarts[0]);
    }

    [Fact]
    public async Task A_failure_that_is_not_a_transport_failure_still_fails_the_operation()
    {
        // AC5: un fallo que NO es de transporte (un 416, un estado inválido, un bug) sí es
        // una falla de la operación: pasa a <c>Failed</c>, que es terminal.
        var store = new RecordingStateStore();

        var coordinator = CreateCoordinator(
            store,
            new RecordingIntentClient(),
            new FailingTransferEngine(new InvalidOperationException("El servidor no pudo servir el tramo.")));

        var handle = await coordinator.StartAsync(
            "paquete-1",
            new Uri(ContentUri),
            TestContext.Current.CancellationToken);

        await WaitUntilAsync(() => handle.StateMachine.Current == DownloadState.Failed);

        Assert.True(handle.StateMachine.IsTerminal);
        Assert.Contains(store.SavedOperations, saved => saved.State == DownloadState.Failed.ToString());
    }

    [Fact]
    public void The_transport_failures_are_the_ones_of_the_network()
    {
        // AD-11, AD-24: la clasificación de un fallo de TRANSPORTE es explícita, así que
        // un fallo de la operación no se confunde con una red caída.
        Assert.True(DownloadRecovery.IsTransportFailure(
            new TransferTransportException("El transporte no pudo traer los bytes.")));

        Assert.False(DownloadRecovery.IsTransportFailure(new InvalidOperationException("El estado no es válido.")));
        Assert.False(DownloadRecovery.IsTransportFailure(new TransferRangeNotSatisfiableException(0, 10)));
    }

    private static PersistedDownloadOperation Persisted(
        string operationId,
        string packageId,
        DownloadState state,
        long bytesPersisted,
        long totalBytes,
        string transferEngine = TransferEngineRegistry.HttpRangeTransferEngine) =>
        new(operationId, packageId, state.ToString(), transferEngine, bytesPersisted, totalBytes);

    private static DownloadCoordinator CreateCoordinator(
        IClientStateStore store,
        IDownloadIntentClient intents,
        ITransferEngine? engine = null,
        IDownloadProgressReporter? reporter = null,
        IServerVersionProbe? versionProbe = null) =>
        new(
            engine ?? new RecordingTransferEngine(totalBytes: 1000),
            intents,
            store,
            new TemporaryDataLayout(),
            NullLogger<DownloadCoordinator>.Instance,
            reporter: reporter,
            versionProbe: versionProbe);

    private string CreateTemporaryFolder()
    {
        var folder = Directory.CreateTempSubdirectory("lan-party-hub-recovery-");

        _temporaryFolders.Add(folder.FullName);

        return folder.FullName;
    }

    /// <summary>
    /// Waits until the condition holds, so the test no tiene que dormir un tiempo fijo
    /// (AD-16).
    /// </summary>
    /// <param name="condition">Condition that has to become true.</param>
    /// <returns>A task that completes when the condition holds.</returns>
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);

        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("La condición no se cumplió dentro del plazo.");
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var folder in _temporaryFolders)
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
                // A file that the operating system still holds does not fail the test.
            }
        }
    }

    /// <summary>
    /// Engine that RECUERDA los inicios de rango que le pidieron, para que el <c>Range</c>
    /// de la reanudación sea observable (AD-20).
    /// </summary>
    private sealed class RecordingTransferEngine : ITransferEngine
    {
        private readonly long _totalBytes;
        private readonly List<long> _requestedStarts = [];
        private readonly Lock _lock = new();

        public RecordingTransferEngine(long totalBytes) => _totalBytes = totalBytes;

        public IReadOnlyList<long> RequestedStarts
        {
            get
            {
                lock (_lock)
                {
                    return _requestedStarts.ToArray();
                }
            }
        }

        public Task<TransferOutcome> TransferAsync(
            Uri contentUri,
            long startInclusive,
            long endInclusive,
            Stream destination,
            CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                _requestedStarts.Add(startInclusive);
            }

            return Task.FromResult(new TransferOutcome(0L, _totalBytes, RangeHonoured: true));
        }
    }

    /// <summary>
    /// Engine that siempre falla con la excepción que le fijaron, para que el efecto de
    /// un fallo de TRANSPORTE y el de un fallo de la operación sean observables por
    /// separado (AD-11, AD-24).
    /// </summary>
    private sealed class FailingTransferEngine : ITransferEngine
    {
        private readonly Exception _failure;

        public FailingTransferEngine(Exception failure) => _failure = failure;

        public Task<TransferOutcome> TransferAsync(
            Uri contentUri,
            long startInclusive,
            long endInclusive,
            Stream destination,
            CancellationToken cancellationToken) =>
            Task.FromException<TransferOutcome>(_failure);
    }

    /// <summary>
    /// Adapter of the registration of the intention that RECUERDA lo que se le pidió
    /// (AC1).
    /// </summary>
    private sealed class RecordingIntentClient : IDownloadIntentClient
    {
        public string? RegisteredPackageId { get; private set; }

        public Task<string> RegisterAsync(string packageId, CancellationToken cancellationToken)
        {
            RegisteredPackageId = packageId;

            return Task.FromResult("operacion-nueva");
        }
    }

    /// <summary>
    /// Reporter of the progress that RECUERDA los eventos que publicó, para que la forma
    /// exacta del contrato sea observable (AC5, AR-3).
    /// </summary>
    private sealed class RecordingProgressReporter : IDownloadProgressReporter
    {
        private readonly List<DownloadProgressUpdated> _reports = [];
        private readonly Lock _lock = new();

        public IReadOnlyList<DownloadProgressUpdated> Reports
        {
            get
            {
                lock (_lock)
                {
                    return _reports.ToArray();
                }
            }
        }

        public Task ReportAsync(DownloadProgressUpdated progress, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                _reports.Add(progress);
            }

            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Sonda de las versiones que siempre devuelve el mismo veredicto (AD-6).
    /// </summary>
    private sealed class FixedVersionProbe : IServerVersionProbe
    {
        private readonly bool _compatible;
        private readonly string? _serverVersion;
        private readonly bool _reachable;

        public FixedVersionProbe(bool compatible, string? serverVersion, bool reachable = true)
        {
            _compatible = compatible;
            _serverVersion = serverVersion;
            _reachable = reachable;
        }

        public Task<ServerVersionProbeResult> ProbeAsync(
            ServerEndpoint endpoint,
            TimeSpan timeout,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ServerVersionProbeResult(
                _compatible,
                _serverVersion,
                1,
                "servidor",
                _reachable));
    }

    /// <summary>
    /// Proveedor de la versión del cliente que devuelve un valor fijo (AD-6).
    /// </summary>
    private sealed class FixedVersionProvider : IClientVersionProvider
    {
        public FixedVersionProvider(string clientVersion) => ClientVersion = clientVersion;

        public string ClientVersion { get; }

        public int ProtocolVersion => 1;
    }

    /// <summary>
    /// Manejador que responde con un 426 y con la versión del servidor (AD-6).
    /// </summary>
    private sealed class RejectingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string? _serverVersion;

        public RejectingHandler(HttpStatusCode statusCode, string? serverVersion)
        {
            _statusCode = statusCode;
            _serverVersion = serverVersion;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(
                    "{\"code\":\"CLIENT_VERSION_INCOMPATIBLE\",\"message\":\"x\",\"cause\":\"y\",\"nextStep\":\"z\"}"),
            };

            if (_serverVersion is not null)
            {
                response.Headers.TryAddWithoutValidation("X-Server-Version", _serverVersion);
            }

            return Task.FromResult(response);
        }
    }

    /// <summary>
    /// Store of the client que RECUERDA lo que se le escribió y lo que se le pidió (AD-16).
    /// </summary>
    private class RecordingStateStore : IClientStateStore
    {
        public List<PersistedDownloadOperation> PersistedOperations { get; } = [];

        public List<(string OperationId, string PackageId, string State)> SavedOperations { get; } = [];

        public Task MigrateAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<ClientIdentity?> LoadIdentityAsync(CancellationToken cancellationToken) =>
            Task.FromResult<ClientIdentity?>(ClientIdentity.Emit());

        public Task SaveIdentityAsync(ClientIdentity identity, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<PersistedDownloadOperation>> LoadDownloadOperationsAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PersistedDownloadOperation>>(PersistedOperations.ToArray());

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
            CancellationToken cancellationToken)
        {
            SavedOperations.Add((operationId, packageId, state));

            return Task.CompletedTask;
        }

        public Task<PinnedTrustAnchor?> LoadTrustAnchorAsync(CancellationToken cancellationToken) =>
            Task.FromResult<PinnedTrustAnchor?>(null);

        public Task SaveTrustAnchorAsync(PinnedTrustAnchor anchor, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task SaveInstallationAsync(ClientInstallation installation, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<ClientInstallation?> LoadInstallationAsync(string gameId, CancellationToken cancellationToken) =>

            Task.FromResult<ClientInstallation?>(null);


        public Task<IReadOnlyList<ClientInstallation>> LoadInstallationsAsync(CancellationToken cancellationToken) =>

            Task.FromResult<IReadOnlyList<ClientInstallation>>([]);

        public Task SaveInstallationFailureAsync(
            ClientInstallation installation,
            string errorCode,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<string?> LoadInstallationFailureAsync(string gameId, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);
    }

    /// <summary>
    /// Store that devuelve las operaciones que otro store recuerda (AD-16).
    /// </summary>
    private sealed class RecordingStateStoreWithOperations : IClientStateStore
    {
        private readonly RecordingStateStore _inner;

        public RecordingStateStoreWithOperations(RecordingStateStore inner) => _inner = inner;

        public Task MigrateAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<ClientIdentity?> LoadIdentityAsync(CancellationToken cancellationToken) =>
            Task.FromResult<ClientIdentity?>(ClientIdentity.Emit());

        public Task SaveIdentityAsync(ClientIdentity identity, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<PersistedDownloadOperation>> LoadDownloadOperationsAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PersistedDownloadOperation>>(_inner.PersistedOperations.ToArray());

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

        public Task SaveInstallationAsync(ClientInstallation installation, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<ClientInstallation?> LoadInstallationAsync(string gameId, CancellationToken cancellationToken) =>

            Task.FromResult<ClientInstallation?>(null);


        public Task<IReadOnlyList<ClientInstallation>> LoadInstallationsAsync(CancellationToken cancellationToken) =>

            Task.FromResult<IReadOnlyList<ClientInstallation>>([]);

        public Task SaveInstallationFailureAsync(
            ClientInstallation installation,
            string errorCode,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<string?> LoadInstallationFailureAsync(string gameId, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);
    }

    /// <summary>
    /// Layout of a folder temporal, para que el parcial de la descarga viva solo en la
    /// carpeta de las descargas (AD-9).
    /// </summary>
    private sealed class TemporaryDataLayout : IClientDataLayout
    {
        private readonly string _folder;

        public TemporaryDataLayout()
        {
            _folder = Directory.CreateTempSubdirectory("lan-party-hub-recovery-layout-").FullName;

            Directory.CreateDirectory(DownloadsDirectory);
        }

        public string Folder => _folder;

        public string InstallDirectory => Path.Combine(_folder, "install");

        public string DownloadsDirectory => Path.Combine(_folder, "downloads");

        public string ManifestsDirectory => Path.Combine(_folder, "manifests");

        public string StateDatabasePath => Path.Combine(_folder, "state.sqlite");

        public string PartialFilePath(string packageId) =>
            Path.Combine(DownloadsDirectory, packageId + ".part");

        public void EnsureCreated() => Directory.CreateDirectory(DownloadsDirectory);

        public void SetInstallDirectory(string installDirectory)
        {
        }
    }
}
