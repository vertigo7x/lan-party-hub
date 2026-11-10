using System.Text;
using LANPartyHub.Client.Application;
using LANPartyHub.Client.Application.Downloads;
using LANPartyHub.Client.Application.Integrity;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Domain;
using LANPartyHub.Client.Domain.StateMachines;
using LANPartyHub.Client.Domain.Trust;
using LANPartyHub.Client.Infrastructure.Persistence;
using LANPartyHub.Server.Infrastructure.Signing;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The NOTIFICATIONS of the change of state of a download (AD-22, FR-10).
///
/// <para>
/// La lista de operaciones en curso del cliente cambia cuando una descarga EMPIEZA y
/// cuando TERMINA, así que el servidor tiene que recibir el estado COMPLETO en esos dos
/// momentos: si no, la tabla del organizador no muestra la descarga que acaba de empezar
/// o sigue mostrando la que ya terminó (AD-22).
/// </para>
/// <para>
/// El aviso NUNCA es una condición para que la descarga siga: un servidor que no recibe
/// el estado no puede impedir que los bytes se descarguen y se persistan (AD-11, AD-22).
/// </para>
/// </summary>
public sealed class DownloadStateChangeNotificationTests : IDisposable
{
    private readonly List<string> _temporaryFolders = [];

    /// <summary>
    /// A download that STARTS notifies the change of state, así que el servidor recibe el
    /// estado COMPLETO y la tabla del organizador ve la descarga que acaba de empezar
    /// (AD-22, FR-10).
    /// </summary>
    [Fact]
    public async Task A_download_that_starts_notifies_the_change_of_state()
    {
        var notifier = new RecordingStateNotifier();
        var intents = new RecordingIntentClient();
        var store = new RecordingStateStore();

        using var gate = new TransferGate();

        var coordinator = CreateCoordinator(
            intents,
            store,
            new GatedTransferEngine(gate, totalBytes: 1000),
            notifier);

        await coordinator.StartAsync(
            "paquete-1",
            new Uri("http://192.168.1.10:5000/api/v1/packages/paquete-1/content"),
            TestContext.Current.CancellationToken);

        await WaitUntilAsync(() => notifier.Notifications >= 1);

        // AD-22: el aviso de ARRANQUE llega al servidor.
        Assert.True(notifier.Notifications >= 1);
    }

    /// <summary>
    /// A download that ENDS notifies the change of state, así que el servidor deja de
    /// mostrar una descarga que ya terminó (AD-22, FR-10).
    /// </summary>
    [Fact]
    public async Task A_download_that_ends_notifies_the_change_of_state()
    {
        var notifier = new RecordingStateNotifier();
        var intents = new RecordingIntentClient();
        var store = new RecordingStateStore();

        var coordinator = CreateCoordinator(
            intents,
            store,
            new FailingTransferEngine(),
            notifier);

        var handle = await coordinator.StartAsync(
            "paquete-1",
            new Uri("http://192.168.1.10:5000/api/v1/packages/paquete-1/content"),
            TestContext.Current.CancellationToken);

        await WaitUntilAsync(() => handle.StateMachine.Current == DownloadState.Failed);

        await coordinator.WaitForPendingReportsAsync();

        // AD-22: el aviso de ARRANQUE y el de CIERRE llegan al servidor: son DOS avisos,
        // uno por cada momento en que la lista de operaciones en curso cambia (AD-22).
        Assert.True(notifier.Notifications >= 2);
    }

    /// <summary>
    /// The download STARTS the bytes WITHOUT waiting for the network: el aviso es de
    /// MEJOR ESFUERZO y no bloquea el comienzo de la transferencia (AD-1, AD-11, AD-22).
    /// </summary>
    [Fact]
    public async Task The_bytes_start_without_waiting_for_the_notification()
    {
        var notifier = new BlockingStateNotifier();
        var intents = new RecordingIntentClient();
        var store = new RecordingStateStore();

        using var gate = new TransferGate();

        var engine = new GatedTransferEngine(gate, totalBytes: 1000);

        var coordinator = CreateCoordinator(intents, store, engine, notifier);

        await coordinator.StartAsync(
            "paquete-1",
            new Uri("http://192.168.1.10:5000/api/v1/packages/paquete-1/content"),
            TestContext.Current.CancellationToken);

        // AD-22: el aviso se queda ESPERANDO al servidor, y la descarga sigue: los bytes
        // ya viajan aunque la red no responda (AD-11, AD-22).
        await WaitUntilAsync(() => engine.RequestedStarts.Count >= 1);

        Assert.NotEmpty(engine.RequestedStarts);
        Assert.True(notifier.Notifications >= 1);

        // La prueba no deja la notificación colgada: el aviso se suelta al final.
        notifier.Release();
    }

    /// <summary>
    /// The download COMPLETES notifies the change of state, así que el servidor deja de
    /// mostrar una descarga que ya terminó (AD-22, FR-10).
    /// </summary>
    [Fact]
    public async Task A_completed_download_notifies_the_change_of_state()
    {
        const string content = "contenido del paquete que se descarga por completo";

        var notifier = new RecordingStateNotifier();
        var intents = new RecordingIntentClient();
        var store = new RecordingStateStore();

        var coordinator = CreateCoordinator(
            intents,
            store,
            new ContentTransferEngine(content),
            notifier,
            CreateValidation(content));

        var handle = await coordinator.StartAsync(
            "paquete-1",
            new Uri("http://192.168.1.10:5000/api/v1/packages/paquete-1/content"),
            TestContext.Current.CancellationToken);

        await WaitUntilAsync(() => handle.StateMachine.Current == DownloadState.Completed);

        await coordinator.WaitForPendingReportsAsync();

        // AD-22: el aviso de ARRANQUE y el de CIERRE llegan al servidor.
        Assert.True(notifier.Notifications >= 2);
    }

    /// <summary>
    /// A download that is PAUSED does NOT notify the change of state, así que la lista de
    /// operaciones en curso NO cambia: una descarga pausada sigue en curso (AD-22).
    /// </summary>
    [Fact]
    public async Task A_paused_download_does_not_notify_the_change_of_state()
    {
        var notifier = new RecordingStateNotifier();
        var intents = new RecordingIntentClient();
        var store = new RecordingStateStore();

        using var gate = new TransferGate();

        var coordinator = CreateCoordinator(
            intents,
            store,
            new GatedTransferEngine(gate, totalBytes: 1000),
            notifier);

        var handle = await coordinator.StartAsync(
            "paquete-1",
            new Uri("http://192.168.1.10:5000/api/v1/packages/paquete-1/content"),
            TestContext.Current.CancellationToken);

        await WaitUntilAsync(() => notifier.Notifications >= 1);

        var notificationsAfterStart = notifier.Notifications;

        // La pausa interrumpe la transferencia en curso, así que la operación queda
        // PAUSADA: no terminal, y por eso la lista de operaciones en curso NO cambia.
        await coordinator.PauseAsync(handle);

        await coordinator.WaitForPendingReportsAsync();

        Assert.Equal(DownloadState.Paused, handle.StateMachine.Current);

        // AD-22: la pausa NO es un cambio de la lista de operaciones en curso, así que no
        // hay aviso nuevo.
        Assert.Equal(notificationsAfterStart, notifier.Notifications);
    }

    private static DownloadCoordinator CreateCoordinator(
        IDownloadIntentClient intents,
        IClientStateStore store,
        ITransferEngine engine,
        IClientStateChangeNotifier notifier,
        ValidatePackage? validation = null)
    {
        var layout = new TemporaryDataLayout();

        return new DownloadCoordinator(
            engine,
            intents,
            store,
            layout,
            NullLogger<DownloadCoordinator>.Instance,
            validation: validation,
            maxConcurrentDownloads: DownloadCoordinator.DefaultMaxConcurrentDownloads,
            maxTotalConnections: DownloadCoordinator.DefaultMaxTotalConnections,
            reporter: null,
            versionProbe: null,
            stateNotifier: notifier);
    }

    /// <summary>
    /// Builds the REAL validation of the package over the signed manifest of the content,
    /// así que la prueba recorre el camino de producción: la autenticidad se valida y la
    /// integridad se comprueba contra el hash del manifiesto (AD-2, AD-8).
    /// </summary>
    /// <param name="content">Content that the manifest declares.</param>
    /// <returns>The use case of the validation.</returns>
    private static ValidatePackage CreateValidation(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);

        var signer = new Ed25519ManifestSigner();

        var manifest = new InstallationManifest(
            ValidatePackage.SupportedManifestSchemaVersion,
            "paquete-1",
            "1.0.0",
            bytes.LongLength,
            LANPartyHub.Client.Domain.Integrity.PackageIntegrity.ComputeSha256(bytes),
            new InstallationManifest.LaunchSettings("g.exe", [], "g"),
            []);

        var canonical = LANPartyHub.Server.Domain.Packages.ManifestCanonicalizer.Canonicalize(manifest);

        var envelope = new InstallationManifest.SignedEnvelope(
            canonical,
            signer.Sign(canonical),
            signer.GetKeyId());

        return new ValidatePackage(
            new FixedManifestClient(envelope),
            new LANPartyHub.Client.Infrastructure.Trust.Ed25519ManifestVerifier(),
            new FixedManifestCache(canonical),
            new FixedTrustAnchorReader(
                new PinnedTrustAnchor(
                    Convert.ToBase64String(Ed25519ManifestSigner.ComputePublicKey(Environment.MachineName)),
                    "fingerprint",
                    "2026-10-26T00:00:00.0000000Z")),
            NullLogger<ValidatePackage>.Instance);
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
    /// Notifier que CUENTA los avisos, así que la prueba ve que el servidor los recibe
    /// (AD-16).
    /// </summary>
    private sealed class RecordingStateNotifier : IClientStateChangeNotifier
    {
        private int _notifications;

        /// <summary>Number of notices that the notifier received.</summary>
        public int Notifications => Volatile.Read(ref _notifications);

        /// <inheritdoc />
        public Task NotifyAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _notifications);

            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Notifier that ESPERA al servidor, así que la prueba ve que la descarga NO espera a
    /// la red para empezar los bytes (AD-22).
    /// </summary>
    private sealed class BlockingStateNotifier : IClientStateChangeNotifier, IDisposable
    {
        private readonly SemaphoreSlim _gate = new(0);
        private int _notifications;

        /// <summary>Number of notices that the notifier received.</summary>
        public int Notifications => Volatile.Read(ref _notifications);

        /// <inheritdoc />
        public async Task NotifyAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _notifications);

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Releases the notices that were waiting for the server.</summary>
        public void Release() => _gate.Release(int.MaxValue);

        /// <inheritdoc />
        public void Dispose() => _gate.Dispose();
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
    /// Engine que ESCRIBE el contenido completo en el destino, así que la descarga
    /// termina de verdad y su validación pasa (AD-8, AD-20).
    /// </summary>
    private sealed class ContentTransferEngine : ITransferEngine
    {
        private readonly byte[] _content;

        public ContentTransferEngine(string content) => _content = Encoding.UTF8.GetBytes(content);

        public async Task<TransferOutcome> TransferAsync(
            Uri contentUri,
            long startInclusive,
            long endInclusive,
            Stream destination,
            CancellationToken cancellationToken)
        {
            await destination.WriteAsync(_content.AsMemory(), cancellationToken).ConfigureAwait(false);

            return new TransferOutcome(_content.LongLength, _content.LongLength, RangeHonoured: true);
        }
    }

    /// <summary>
    /// Port of the cache of the signed manifests que siempre devuelve el mismo texto
    /// canónico (AD-2, AD-16).
    /// </summary>
    private sealed class FixedManifestCache : IManifestCache
    {
        private readonly string _canonical;

        public FixedManifestCache(string canonical) => _canonical = canonical;

        public Task StoreAsync(
            string packageId,
            string canonicalManifest,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<string?> LoadAsync(string packageId, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(_canonical);
    }

    /// <summary>
    /// Port of the read of the signed manifest (AD-16).
    /// </summary>
    private sealed class FixedManifestClient : ISignedManifestClient
    {
        private readonly InstallationManifest.SignedEnvelope? _envelope;

        public FixedManifestClient(InstallationManifest.SignedEnvelope? envelope) => _envelope = envelope;

        public Task<InstallationManifest.SignedEnvelope?> GetAsync(string packageId, CancellationToken cancellationToken) =>
            Task.FromResult(_envelope);
    }

    /// <summary>
    /// Port of the read de la ancla de confianza que siempre devuelve el mismo valor
    /// (AD-27).
    /// </summary>
    private sealed class FixedTrustAnchorReader : ITrustAnchorReader
    {
        private readonly PinnedTrustAnchor? _anchor;

        public FixedTrustAnchorReader(PinnedTrustAnchor? anchor) => _anchor = anchor;

        public Task<PinnedTrustAnchor?> LoadTrustAnchorAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_anchor);
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
    /// Store of the client que RECUERDA lo que se le escribió, para que la persistencia
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
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

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
            _folder = Directory.CreateTempSubdirectory("lan-party-hub-notifier-").FullName;

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
}
