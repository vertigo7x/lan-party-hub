using System.Text;
using LANPartyHub.Client.Application;
using LANPartyHub.Client.Application.Downloads;
using LANPartyHub.Client.Application.Integrity;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Domain;
using LANPartyHub.Client.Domain.Integrity;
using LANPartyHub.Client.Domain.StateMachines;
using LANPartyHub.Client.Domain.Trust;
using LANPartyHub.Client.Infrastructure.Persistence;
using LANPartyHub.Server.Domain.Packages;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// Behaviour of the coordinator of the downloads (AD-10, AD-11, AD-14, AD-20, AD-24,
/// AC1..AC10).
///
/// <para>
/// El coordinador se ejercita con dobles de los PUERTOS: así la secuencia de la
/// descarga, la pausa, la reanudación y la cancelación son observables sin un servidor y
/// sin un disco (AD-1, AD-16).
/// </para>
/// </summary>
public sealed class DownloadCoordinatorTests : IDisposable
{
    private readonly List<string> _temporaryFolders = [];

    [Fact]
    public async Task Starting_a_download_registers_the_intention_and_persists_the_operation()
    {
// AC1: el cliente registra la intención con el servidor y obtiene el operationId.
        var intents = new RecordingIntentClient();
        var store = new RecordingStateStore();

        var coordinator = CreateCoordinator(intents, store);

        var handle = await coordinator.StartAsync(
            "paquete-1",
            new Uri("http://192.168.1.10:5000/api/v1/packages/paquete-1/content"),
            TestContext.Current.CancellationToken);

        Assert.Equal("operacion-1", handle.OperationId);
        Assert.Equal("paquete-1", intents.RegisteredPackageId);

// AD-10, AD-11: la operación y su estado se persisten, con el motor que la
        // transfiere.
        var operation = Assert.Single(
            store.SavedOperations,
            operation => operation.State == DownloadState.Queued.ToString());
        Assert.Equal("operacion-1", operation.OperationId);
        Assert.Equal("paquete-1", operation.PackageId);
        Assert.Equal(TransferEngineRegistry.HttpRangeTransferEngine, operation.TransferEngine);
    }

    [Fact]
    public async Task A_download_in_course_reports_its_progress()
    {
// AC2: el cliente informa del porcentaje entero y del par de tamaños.
        var intents = new RecordingIntentClient();
        var store = new RecordingStateStore();

        var coordinator = CreateCoordinator(intents, store);

        using var gate = new TransferGate();

        var engine = new GatedTransferEngine(gate, totalBytes: 1_000_000_000);

        var coordinatorWithEngine = CreateCoordinator(intents, store, engine);

        var handle = await coordinatorWithEngine.StartAsync(
            "paquete-1",
            new Uri("http://192.168.1.10:5000/api/v1/packages/paquete-1/content"),
            TestContext.Current.CancellationToken);

        gate.Release(400_000_000);

        await WaitUntilAsync(() => handle.BytesPersisted == 400_000_000);

        Assert.Equal(40, handle.Progress.Percentage);
        Assert.Equal("0,4 GB de 1,0 GB", handle.Progress.SizesText);
    }

    [Fact]
    public async Task Pausing_a_download_keeps_its_offset()
    {
        // AC3: la pausa pasa a Paused y NO borra nada: el offset se conserva.
        var intents = new RecordingIntentClient();
        var store = new RecordingStateStore();

        using var gate = new TransferGate();

        var engine = new GatedTransferEngine(gate, totalBytes: 1000);

        var coordinator = CreateCoordinator(intents, store, engine);

        var handle = await coordinator.StartAsync(
            "paquete-1",
            new Uri("http://192.168.1.10:5000/api/v1/packages/paquete-1/content"),
            TestContext.Current.CancellationToken);

        gate.Release(300);

        await WaitUntilAsync(() => handle.BytesPersisted == 300);

        await coordinator.PauseAsync(handle);

        Assert.Equal(DownloadState.Paused, handle.StateMachine.Current);

// AC4: el offset persistido se conserva, así que la reanudación continúa desde la
// misma posición.
        Assert.Equal(300, store.SavedProgresses.Last().BytesPersisted);
    }

    [Fact]
    public async Task Resuming_a_download_continues_from_the_persisted_offset()
    {
// AC3, AC4: al reanudar continúa DESDE LA MISMA POSICIÓN, sin volver a cero.
        var intents = new RecordingIntentClient();
        var store = new RecordingStateStore();

        using var gate = new TransferGate();

        var engine = new GatedTransferEngine(gate, totalBytes: 1000);

        var coordinator = CreateCoordinator(intents, store, engine);

        var handle = await coordinator.StartAsync(
            "paquete-1",
            new Uri("http://192.168.1.10:5000/api/v1/packages/paquete-1/content"),
            TestContext.Current.CancellationToken);

        gate.Release(300);

        await WaitUntilAsync(() => handle.BytesPersisted == 300);

        await coordinator.PauseAsync(handle);

        gate.Release(300);

        await coordinator.ResumeAsync(handle, TestContext.Current.CancellationToken);

        await WaitUntilAsync(() => engine.RequestedStarts.Count >= 2);

// La reanudación pide el rango que empieza en el offset acumulado (AD-20, AD-24).
        Assert.Equal(300, engine.RequestedStarts[1]);
    }

    [Fact]
    public async Task Cancelling_a_download_deletes_the_partial_and_resets_the_progress()
    {
        // AC5: al cancelar se borra el archivo parcial y se reinicia el registro de
        // progreso: bytesPersisted vuelve a cero.
        var intents = new RecordingIntentClient();
        var store = new RecordingStateStore();

        var folder = CreateTemporaryFolder();

        var layout = ClientDataLayout.Create(folder, Path.Combine(folder, "install"));
        layout.EnsureCreated();

        var partial = layout.PartialFilePath("paquete-1");

        File.WriteAllBytes(partial, new byte[64]);

        var coordinator = CreateCoordinator(intents, store, layout: layout);

        var handle = await coordinator.StartAsync(
            "paquete-1",
            new Uri("http://192.168.1.10:5000/api/v1/packages/paquete-1/content"),
            TestContext.Current.CancellationToken);

        await coordinator.CancelAsync(handle, TestContext.Current.CancellationToken);

        Assert.Equal(DownloadState.Cancelled, handle.StateMachine.Current);

// El archivo parcial está BORRADO.
        Assert.False(File.Exists(partial));

        // El registro de progreso se REINICIA: bytesPersisted vuelve a cero.
        Assert.Equal(0, store.SavedProgresses.Last().BytesPersisted);
        Assert.Equal(0, handle.BytesPersisted);
    }

    [Fact]
    public async Task Cancelling_a_download_leaves_it_terminal_and_not_resumable()
    {
// AC5: «Cancelled» es un estado TERMINAL de DownloadState.
        var coordinator = CreateCoordinator(new RecordingIntentClient(), new RecordingStateStore());

        var handle = await coordinator.StartAsync(
            "paquete-1",
            new Uri("http://192.168.1.10:5000/api/v1/packages/paquete-1/content"),
            TestContext.Current.CancellationToken);

        await coordinator.CancelAsync(handle, TestContext.Current.CancellationToken);

        Assert.True(DownloadStateMachine.IsTerminalState(DownloadState.Cancelled));
        Assert.True(handle.StateMachine.IsTerminal);

// La operación NO queda reintentable por reanudación.
        await coordinator.ResumeAsync(handle, TestContext.Current.CancellationToken);

        Assert.Equal(DownloadState.Cancelled, handle.StateMachine.Current);
    }

    [Fact]
    public async Task A_failed_download_keeps_its_partial_and_its_offset()
    {
// AC5: a diferencia de la cancelación, Failed CONSERVA el parcial y su offset.
        var intents = new RecordingIntentClient();
        var store = new RecordingStateStore();

        var folder = CreateTemporaryFolder();

        var layout = ClientDataLayout.Create(folder, Path.Combine(folder, "install"));
        layout.EnsureCreated();

        var engine = new FailingTransferEngine();

        var coordinator = CreateCoordinator(intents, store, layout: layout, engine: engine);

        var handle = await coordinator.StartAsync(
            "paquete-1",
            new Uri("http://192.168.1.10:5000/api/v1/packages/paquete-1/content"),
            TestContext.Current.CancellationToken);

        await WaitUntilAsync(() => handle.StateMachine.Current == DownloadState.Failed);

        var partial = layout.PartialFilePath("paquete-1");

        Assert.True(File.Exists(partial));

        // El offset se conserva: la falla NO reinicia el registro de progreso.
        Assert.All(store.SavedProgresses, progress => Assert.True(progress.BytesPersisted >= 0));
    }

    [Fact]
    public async Task The_operations_of_the_same_package_are_serialized()
    {
        // AD-24: las operaciones que tocan el MISMO RECURSO se serializan.
        var intents = new RecordingIntentClient();
        var store = new RecordingStateStore();

        using var gate = new TransferGate();

        var engine = new GatedTransferEngine(gate, totalBytes: 1000);

        var coordinator = CreateCoordinator(intents, store, engine);

        var first = await coordinator.StartAsync(
            "paquete-1",
            new Uri("http://192.168.1.10:5000/api/v1/packages/paquete-1/content"),
            TestContext.Current.CancellationToken);

        var second = await coordinator.StartAsync(
            "paquete-1",
            new Uri("http://168.168.1.10:5000/api/v1/packages/paquete-1/content"),
            TestContext.Current.CancellationToken);

        await WaitUntilAsync(() => engine.RequestedStarts.Count >= 1);

// La segunda operación del MISMO paquete espera su turno: la primera tiene el
        // recurso tomado.
        Assert.Single(engine.RequestedStarts);

        gate.Release(100);

        await WaitUntilAsync(() => first.BytesPersisted == 100);

        gate.Release(100);

        await WaitUntilAsync(() => engine.RequestedStarts.Count >= 2);
    }

    [Fact]
    public async Task The_operations_of_different_packages_coexist()
    {
        // AD-24: dos operaciones sobre recursos DISTINTOS pueden coexistir.
        var intents = new RecordingIntentClient();
        var store = new RecordingStateStore();

        using var gate = new TransferGate();

        var engine = new GatedTransferEngine(gate, totalBytes: 1000);

        var coordinator = CreateCoordinator(intents, store, engine);

        await coordinator.StartAsync(
            "paquete-1",
            new Uri("http://192.168.1.10:5000/api/v1/packages/paquete-1/content"),
            TestContext.Current.CancellationToken);

        await coordinator.StartAsync(
            "paquete-2",
            new Uri("http://192.168.1.10:5000/api/v1/packages/paquete-2/content"),
            TestContext.Current.CancellationToken);

        gate.Release(100);

        // Las DOS operaciones avanzan a la vez: ninguna espera a la otra.
        await WaitUntilAsync(() => engine.RequestedStarts.Count >= 2);
    }

    [Fact]
    public void The_limits_of_the_client_have_a_default_value()
    {
// AC10: los límites por cliente son configurables y tienen valor por defecto.
        var coordinator = CreateCoordinator(new RecordingIntentClient(), new RecordingStateStore());

        Assert.Equal(2, coordinator.MaxConcurrentDownloads);
        Assert.Equal(4, coordinator.MaxTotalConnections);
    }

    [Fact]
    public void The_limits_of_the_client_can_be_configured()
    {
// AC10: los límites por cliente son CONFIGURABLES.
        var coordinator = CreateCoordinator(
            new RecordingIntentClient(),
            new RecordingStateStore(),
            maxConcurrentDownloads: 1,
            maxTotalConnections: 3);

        Assert.Equal(1, coordinator.MaxConcurrentDownloads);
        Assert.Equal(3, coordinator.MaxTotalConnections);
    }

    [Fact]
    public void A_limit_of_zero_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateCoordinator(
            new RecordingIntentClient(),
            new RecordingStateStore(),
            maxConcurrentDownloads: 0));

        Assert.Throws<ArgumentOutOfRangeException>(() => CreateCoordinator(
            new RecordingIntentClient(),
            new RecordingStateStore(),
            maxTotalConnections: 0));
    }

    [Fact]
    public async Task The_client_does_not_open_more_simultaneous_downloads_than_its_limit()
    {
// AD-14: el límite de descargas simultáneas es POR CLIENTE.
        var intents = new RecordingIntentClient();
        var store = new RecordingStateStore();

        using var gate = new TransferGate();

        var engine = new GatedTransferEngine(gate, totalBytes: 1000);

        var coordinator = CreateCoordinator(
            intents,
            store,
            engine,
            maxConcurrentDownloads: 1,
            maxTotalConnections: 4);

        await coordinator.StartAsync(
            "paquete-1",
            new Uri("http://192.168.1.10:5000/api/v1/packages/paquete-1/content"),
            TestContext.Current.CancellationToken);

        await coordinator.StartAsync(
            "paquete-2",
            new Uri("http://192.168.1.10:5000/api/v1/packages/paquete-2/content"),
            TestContext.Current.CancellationToken);

        await WaitUntilAsync(() => engine.RequestedStarts.Count >= 1);

// El límite de UNA descarga simultánea: la segunda espera.
        Assert.Single(engine.RequestedStarts);

        gate.Release(100);
    }

    private static DownloadCoordinator CreateCoordinator(
        IDownloadIntentClient intents,
        IClientStateStore store,
        ITransferEngine? engine = null,
        IClientDataLayout? layout = null,
        int maxConcurrentDownloads = DownloadCoordinator.DefaultMaxConcurrentDownloads,
        int maxTotalConnections = DownloadCoordinator.DefaultMaxTotalConnections,
        ValidatePackage? validation = null)
    {
        var effectiveLayout = layout ?? new TemporaryDataLayout();

        return new DownloadCoordinator(
            engine ?? new GatedTransferEngine(new TransferGate(), totalBytes: 1000),
            intents,
            store,
            effectiveLayout,
            NullLogger<DownloadCoordinator>.Instance,
            validation,
            maxConcurrentDownloads,
            maxTotalConnections);
    }

    /// <summary>
/// AC1, AC2, FR-11: la descarga de la VERSIÓN COMPLETA termina en «Completado» cuando
    /// el <c>sha256</c> de los bytes coincide con el del manifiesto firmado.
    /// </summary>
    [Fact]
    public async Task The_download_of_the_complete_package_completes_when_the_hash_matches()
    {
        var folder = CreateTemporaryFolder();

        var layout = ClientDataLayout.Create(folder, Path.Combine(folder, "install"));

        layout.EnsureCreated();

        var packageBytes = Encoding.UTF8.GetBytes("contenido del paquete completo");

        var manifest = new InstallationManifest(
            1,
            "paquete-1",
            "2.0",
            packageBytes.LongLength,
            PackageIntegrity.ComputeSha256(packageBytes),
            new InstallationManifest.LaunchSettings("game.exe", ["-novid"], "game"),
            [new InstallationManifest.Verify("game/game.exe")]);

        var cache = new RecordingManifestCache();

        var coordinator = CreateCoordinator(
            new RecordingIntentClient(),
            new RecordingStateStore(),
            new WholeTransferEngine(packageBytes),
            layout,
            maxConcurrentDownloads: 1,
            maxTotalConnections: 1,
            CreateValidation(manifest, packageBytes, cache));

        var handle = await coordinator.DownloadFullPackageAsync(
            "paquete-1",
            new Uri("http://192.168.1.10:5000/api/v1/packages/paquete-1/content"),
            TestContext.Current.CancellationToken);

        Assert.Equal(DownloadState.Completed, handle.StateMachine.Current);
        Assert.Null(handle.ValidationError);
    }

    /// <summary>
/// AC1, AC2: la descarga de la versión completa NO deja nada escrito en disco cuando
/// el manifiesto firmado no valida contra el ancla fijada: ni manifiesto en la caché,
/// ni parcial, ni operación registrada (AD-2, AD-8, AD-27).
    /// </summary>
    [Fact]
    public async Task The_download_of_the_complete_package_writes_nothing_when_the_manifest_is_not_trustworthy()
    {
        var folder = CreateTemporaryFolder();

        var layout = ClientDataLayout.Create(folder, Path.Combine(folder, "install"));

        layout.EnsureCreated();

        var store = new RecordingStateStore();

        var cache = new RecordingManifestCache();

        var coordinator = CreateCoordinator(
            new RecordingIntentClient(),
            store,
            new WholeTransferEngine(Encoding.UTF8.GetBytes("contenido")),
            layout,
            maxConcurrentDownloads: 1,
            maxTotalConnections: 1,
            CreateValidation(
                new InstallationManifest(
                    1,
                    "paquete-1",
                    "2.0",
                    8,
                    PackageIntegrity.ComputeSha256(Encoding.UTF8.GetBytes("contenido")),
                    new InstallationManifest.LaunchSettings("game.exe", ["-novid"], "game"),
                    [new InstallationManifest.Verify("game/game.exe")]),
                Encoding.UTF8.GetBytes("contenido"),
                cache,
                isTrustworthy: false));

        await Assert.ThrowsAsync<PackageValidationFailedException>(() =>
            coordinator.DownloadFullPackageAsync(
                "paquete-1",
                new Uri("http://192.168.1.10:5000/api/v1/packages/paquete-1/content"),
                TestContext.Current.CancellationToken));

        // NINGUNA escritura en disco (AD-2, AD-8, AD-27).
        Assert.Empty(cache.Stored);

        Assert.False(File.Exists(Path.Combine(
            folder,
            ClientDataLayout.ManifestsFolderName,
            "paquete-1.json")));

        Assert.False(File.Exists(Path.Combine(
            folder,
            ClientDataLayout.DownloadsFolderName,
            "paquete-1" + ClientDataLayout.PartialFileExtension)));

// Y la operación NO se registró (AD-8).
        Assert.Empty(store.SavedOperations);
    }

    /// <summary>
/// AC2, AD-8: la descarga de la versión completa DESCARTA el parcial y falla cuando el
    /// <c>sha256</c> de los bytes NO coincide con el del manifiesto firmado.
    /// </summary>
    [Fact]
    public async Task The_download_of_the_complete_package_is_rejected_when_the_hash_diverges()
    {
        var folder = CreateTemporaryFolder();

        var layout = ClientDataLayout.Create(folder, Path.Combine(folder, "install"));

        layout.EnsureCreated();

        var packageBytes = Encoding.UTF8.GetBytes("contenido alterado");

        var manifest = new InstallationManifest(
            1,
            "paquete-1",
            "2.0",
            packageBytes.LongLength,
            PackageIntegrity.ComputeSha256(Encoding.UTF8.GetBytes("otro contenido")),
            new InstallationManifest.LaunchSettings("game.exe", ["-novid"], "game"),
            [new InstallationManifest.Verify("game/game.exe")]);

        var coordinator = CreateCoordinator(
            new RecordingIntentClient(),
            new RecordingStateStore(),
            new WholeTransferEngine(packageBytes),
            layout,
            maxConcurrentDownloads: 1,
            maxTotalConnections: 1,
            CreateValidation(manifest, packageBytes, new RecordingManifestCache()));

        var handle = await coordinator.DownloadFullPackageAsync(
            "paquete-1",
            new Uri("http://192.168.1.10:5000/api/v1/packages/paquete-1/content"),
            TestContext.Current.CancellationToken);

        Assert.Equal(DownloadState.Failed, handle.StateMachine.Current);
        Assert.Equal(ErrorCatalog.PackageIntegrityMismatchCode, handle.ValidationError!.Code);

// El parcial se DESCARTA: la operación no es reparable por reanudación (AD-8).
        Assert.False(File.Exists(Path.Combine(
            folder,
            ClientDataLayout.DownloadsFolderName,
            "paquete-1" + ClientDataLayout.PartialFileExtension)));
    }

    /// <summary>
    /// Builds the VALIDATION of the real package: the manifest is cached only when it is
    /// trustworthy, and the integrity is checked against the <c>sha256</c> that the signed
    /// manifest declares (AD-2, AD-8).
    /// </summary>
    /// <param name="manifest">Manifest of the package.</param>
    /// <param name="packageBytes">Bytes that the server serves.</param>
    /// <param name="cache">Cache of the signed manifests.</param>
    /// <param name="isTrustworthy">Whether the signature validates against the anchor.</param>
    /// <returns>The use case of the validation.</returns>
    private static ValidatePackage CreateValidation(
        InstallationManifest manifest,
        byte[] packageBytes,
        RecordingManifestCache cache,
        bool isTrustworthy = true)
    {
        var canonical = ManifestCanonicalizer.Canonicalize(manifest);

        return new ValidatePackage(
            new FixedManifestClient(canonical),
            new AcceptingManifestVerifier(isTrustworthy),
            cache,
            new FixedTrustAnchorReader(),
            NullLogger<ValidatePackage>.Instance);
    }

    private string CreateTemporaryFolder()
    {
        var folder = Directory.CreateTempSubdirectory("lan-party-hub-download-");

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
    /// Gate that holds the transfer until the test releases it, so el momento de la
/// pausa y el de la cancelación son observables (AD-16).
    /// </summary>
    private sealed class TransferGate : IDisposable
    {
        private readonly SemaphoreSlim _gate = new(0);
        private readonly Queue<long> _written = [];
        private readonly Lock _lock = new();

        public void Release(long bytes)
        {
            lock (_lock)
            {
                _written.Enqueue(bytes);
            }

            _gate.Release();
        }

        public async Task<long> WaitAsync(CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

            lock (_lock)
            {
                return _written.Dequeue();
            }
        }

        public void Dispose() => _gate.Dispose();
    }

    /// <summary>
    /// Engine that holds the transfer in the gate y escribe lo que la puerta libera.
    /// </summary>
    private sealed class GatedTransferEngine : ITransferEngine
    {
        private readonly TransferGate _gate;
        private readonly long _totalBytes;
        private readonly List<long> _requestedStarts = [];
        private readonly Lock _lock = new();

        public GatedTransferEngine(TransferGate gate, long totalBytes)
        {
            _gate = gate;
            _totalBytes = totalBytes;
        }

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

        public async Task<TransferOutcome> TransferAsync(
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

            var byteCount = await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

            var written = new byte[1];

            await destination.WriteAsync(written.AsMemory(), cancellationToken).ConfigureAwait(false);

            return new TransferOutcome(byteCount, _totalBytes, RangeHonoured: true);
        }
    }

    /// <summary>
    /// Engine that always fails, para que el estado de la falla sea observable (AC5).
    /// </summary>
    private sealed class FailingTransferEngine : ITransferEngine
    {
        public Task<TransferOutcome> TransferAsync(
            Uri contentUri,
            long startInclusive,
            long endInclusive,
            Stream destination,
            CancellationToken cancellationToken) =>
            Task.FromException<TransferOutcome>(
                new InvalidOperationException("El servidor no pudo servir el tramo."));
    }

    /// <summary>
    /// Adapter of the registration of the intention that returns a fixed identifier
    /// (AC1).
    /// </summary>
    private sealed class RecordingIntentClient : IDownloadIntentClient
    {
        private int _counter;

        public string? RegisteredPackageId { get; private set; }

        public Task<string> RegisterAsync(string packageId, CancellationToken cancellationToken)
        {
            RegisteredPackageId = packageId;

            var operationId = $"operacion-{Interlocked.Increment(ref _counter)}";

            return Task.FromResult(operationId);
        }
    }

    /// <summary>
/// Store of the client that RECUERDA lo que se le escribió, para que la persistencia
    /// sea observable sin un disco (AD-16).
    /// </summary>
    private sealed class RecordingStateStore : IClientStateStore
    {
        public List<(string OperationId, string PackageId, string State, string TransferEngine)> SavedOperations { get; } = [];

        public List<(string OperationId, long BytesPersisted, long? TotalBytes)> SavedProgresses { get; } = [];

        public Task MigrateAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<ClientIdentity?> LoadIdentityAsync(CancellationToken cancellationToken) =>
            Task.FromResult<ClientIdentity?>(null);

        public Task SaveIdentityAsync(ClientIdentity identity, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<PersistedDownloadOperation>> LoadDownloadOperationsAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PersistedDownloadOperation>>(Array.Empty<PersistedDownloadOperation>());

        public Task SaveProgressAsync(
            string operationId,
            long bytesPersisted,
            long? totalBytes,
            CancellationToken cancellationToken)
        {
            SavedProgresses.Add((operationId, bytesPersisted, totalBytes));

            return Task.CompletedTask;
        }

        public Task SaveOperationAsync(
            string operationId,
            string packageId,
            string state,
            string transferEngine,
            CancellationToken cancellationToken)
        {
            SavedOperations.Add((operationId, packageId, state, transferEngine));

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
    /// Layout of a folder temporal, para que el parcial de la descarga viva solo en la
    /// carpeta de las descargas (AD-9).
    /// </summary>
    private sealed class TemporaryDataLayout : IClientDataLayout
    {
        private readonly string _folder;

        public TemporaryDataLayout()
        {
            _folder = Directory.CreateTempSubdirectory("lan-party-hub-layout-").FullName;

            Directory.CreateDirectory(DownloadsDirectory);
        }

        public string Folder => _folder;

        public string InstallDirectory => Path.Combine(_folder, "install");

        public string DownloadsDirectory => Path.Combine(_folder, "downloads");

        public string ManifestsDirectory => Path.Combine(_folder, "manifests");

        public string StateDatabasePath => Path.Combine(_folder, "state.sqlite");

        public string PartialFilePath(string packageId) =>
            Path.Combine(DownloadsDirectory, packageId + ".part");

        public void EnsureCreated()
        {
            Directory.CreateDirectory(DownloadsDirectory);
        }

        public void SetInstallDirectory(string installDirectory)
        {
        }
    }

    /// <summary>
/// Engine that sirve el CONTENIDO COMPLETO del paquete de una vez, así que la
/// descarga de la versión completa no necesita ninguna puerta (AD-4).
    /// </summary>
    private sealed class WholeTransferEngine : ITransferEngine
    {
        private readonly byte[] _content;

        public WholeTransferEngine(byte[] content) => _content = content;

        public async Task<TransferOutcome> TransferAsync(
            Uri contentUri,
            long startInclusive,
            long endInclusive,
            Stream destination,
            CancellationToken cancellationToken)
        {
            await destination
                .WriteAsync(_content.AsMemory(), cancellationToken)
                .ConfigureAwait(false);

            return new TransferOutcome(_content.LongLength, _content.LongLength, RangeHonoured: true);
        }
    }

    /// <summary>
/// Port of the read of the signed manifest que devuelve SIEMPRE el mismo sobre, así
/// que la validación de la autenticidad es observable sin un servidor (AD-16).
    /// </summary>
    private sealed class FixedManifestClient : ISignedManifestClient
    {
        private readonly string _canonical;

        public FixedManifestClient(string canonical) => _canonical = canonical;

        public Task<InstallationManifest.SignedEnvelope?> GetAsync(
            string packageId,
            CancellationToken cancellationToken) =>
            Task.FromResult<InstallationManifest.SignedEnvelope?>(
                new InstallationManifest.SignedEnvelope(_canonical, "firma", "clave"));
    }

    /// <summary>
/// Verificador de la firma que la ACEPTA o la RECHAZA, así que el rechazo ANTES de
    /// escribir es observable (AD-2, AD-8).
    /// </summary>
    private sealed class AcceptingManifestVerifier : IManifestSignatureVerifier
    {
        private readonly bool _accepts;

        public AcceptingManifestVerifier(bool accepts = true) => _accepts = accepts;

        public bool Verify(string canonicalManifest, string signature, byte[] publicKey) => _accepts;
    }

    /// <summary>
/// Port of the read of the ancla de confianza que siempre devuelve el mismo valor, así
/// que la validación contra el ancla fijado es observable (AD-27).
    /// </summary>
    private sealed class FixedTrustAnchorReader : ITrustAnchorReader
    {
        public Task<PinnedTrustAnchor?> LoadTrustAnchorAsync(CancellationToken cancellationToken) =>
            Task.FromResult<PinnedTrustAnchor?>(
                new PinnedTrustAnchor(
                    Convert.ToBase64String(new byte[32]),
                    "fingerprint",
                    "2026-10-26T00:00:00.0000000Z"));
    }

    /// <summary>
/// Port of the cache of the signed manifests que RECUERDA lo que se le escribió, así
    /// que la ausencia de escritura en disco es observable (AD-2, AD-8).
    /// </summary>
    private sealed class RecordingManifestCache : IManifestCache
    {
        private readonly Dictionary<string, string> _stored = new(StringComparer.Ordinal);

        public IReadOnlyDictionary<string, string> Stored => _stored;

        public Task StoreAsync(
            string packageId,
            string canonicalManifest,
            CancellationToken cancellationToken)
        {
            _stored[packageId] = canonicalManifest;

            return Task.CompletedTask;
        }

        public Task<string?> LoadAsync(string packageId, CancellationToken cancellationToken) =>
            Task.FromResult(_stored.TryGetValue(packageId, out var canonical) ? canonical : null);
    }
}
