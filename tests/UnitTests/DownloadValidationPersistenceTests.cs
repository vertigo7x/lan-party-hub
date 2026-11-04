using System.Text;
using LANPartyHub.Client.Application;
using LANPartyHub.Client.Application.Downloads;
using LANPartyHub.Client.Application.Integrity;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Domain;
using LANPartyHub.Client.Domain.StateMachines;
using LANPartyHub.Client.Domain.Trust;
using LANPartyHub.Server.Infrastructure.Signing;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The persistence of the state of a download while it is validated (AD-8, AD-10, AD-11,
/// AD-20, AD-24, AC2).
///
/// <para>
/// La validación del hash de un paquete de GB tarda DECENAS DE SEGUNDOS, así que el
/// cliente se puede cerrar a mitad. Lo que se persiste durante esa ventana decide si la
/// operación se recupera o si su parcial queda huérfano en «downloads/» para siempre
/// (AD-11, AD-24).
/// </para>
/// <para>
/// El estado «Validating» NO es reanudable ni terminal, así que NO puede persistirse: la
/// operación tiene que quedarse persistida como «Downloading», que ES reanudable.
/// </para>
/// </summary>
public sealed class DownloadValidationPersistenceTests : IDisposable
{
    private const string PackageId = "paquete-1";

    private const string ContentUri = "http://192.168.1.10:5000/api/v1/packages/paquete-1/content";

    private readonly List<string> _temporaryFolders = [];

    /// <summary>
    /// The intermediate state «Validating» is NOT persisted, porque no es reanudable ni
    /// terminal: un cierre del cliente durante el hash dejaría la operación en un limbo
    /// (AD-10, AD-11, AD-24).
    /// </summary>
    [Fact]
    public async Task The_intermediate_validating_state_is_not_persisted()
    {
        const string content = "contenido del paquete";

        var store = new RecordingStateStore();

        var coordinator = CreateCoordinator(
            store,
            new RecordingTransferEngine(content.Length, Encoding.UTF8.GetBytes(content)),
            CreateValidation(content));

        var handle = await coordinator.StartAsync(
            PackageId,
            new Uri(ContentUri),
            TestContext.Current.CancellationToken);

        await WaitUntilAsync(() => handle.StateMachine.Current == DownloadState.Completed);

        // «Validating» NUNCA se persiste: no es reanudable ni terminal, así que
        // persistirlo abandonaba la operación para siempre (AD-11, AD-24).
        Assert.DoesNotContain(store.SavedOperations, saved => saved.State == DownloadState.Validating.ToString());

        // Lo que se persiste durante la validación es un estado REANUDABLE, así que el
        // siguiente arranque recupera la operación y su parcial no queda huérfano (AD-11).
        Assert.Contains(store.SavedOperations, saved => saved.State == DownloadState.Downloading.ToString());
    }

    /// <summary>
    /// The state that the validation leaves persisted is the TERMINAL one: «Completed» (or
    /// «Failed»), so the next boot does not resume a finished download (AD-10, AC5).
    /// </summary>
    [Fact]
    public async Task The_terminal_state_of_the_validation_is_persisted()
    {
        const string content = "contenido del paquete";

        var store = new RecordingStateStore();

        var coordinator = CreateCoordinator(
            store,
            new RecordingTransferEngine(content.Length, Encoding.UTF8.GetBytes(content)),
            CreateValidation(content));

        var handle = await coordinator.StartAsync(
            PackageId,
            new Uri(ContentUri),
            TestContext.Current.CancellationToken);

        await WaitUntilAsync(() => handle.StateMachine.Current == DownloadState.Completed);

        Assert.Contains(store.SavedOperations, saved => saved.State == DownloadState.Completed.ToString());
    }

    /// <summary>
    /// An operation that was persisted while it was validating is RECOVERABLE: the state
    /// that the client persists during the hash is «Downloading», que la recuperación
    /// admite (AD-10, AD-11).
    /// </summary>
    [Fact]
    public void The_state_persisted_during_the_validation_is_recoverable()
    {
        // El estado que la persistencia escribe durante el hash es «Downloading», y ese
        // estado ES reanudable: por eso la operación no se abandona (AD-10, AD-11).
        Assert.True(DownloadRecovery.IsResumableState(DownloadState.Downloading));

        // Y «Validating» NO lo es: por eso no puede persistirse (AD-11, AD-24).
        Assert.False(DownloadRecovery.IsResumableState(DownloadState.Validating));
    }

    /// <summary>
    /// A download whose bytes are ALREADY complete is validated WITHOUT transferring
    /// again: asking for «bytes=&lt;total&gt;-» would make the server answer 416, and that
    /// failure is not a transport failure, so the operation would end in «Failed» and the
    /// COMPLETE download would be lost (AD-20, AD-24).
    /// </summary>
    [Fact]
    public async Task A_complete_download_is_validated_without_transferring_again()
    {
        const string content = "contenido del paquete";

        var store = new RecordingStateStore();

        var layout = new TemporaryDataLayout(_temporaryFolders);

        // Los bytes ya están completos en el parcial del disco: la operación quedó
        // persistida con el offset igual al total (AD-11).
        var partial = layout.PartialFilePath(PackageId);

        File.WriteAllBytes(partial, Encoding.UTF8.GetBytes(content));

        store.PersistedOperations.Add(
            new PersistedDownloadOperation(
                "operacion-1",
                PackageId,
                DownloadState.Downloading.ToString(),
                TransferEngineRegistry.HttpRangeTransferEngine,
                content.Length,
                content.Length));

        var engine = new RecordingTransferEngine(totalBytes: content.Length);

        var coordinator = CreateCoordinator(
            new RecordingStateStoreWithOperations(store),
            engine,
            CreateValidation(content),
            layout);

        var recovered = await coordinator.RecoverAsync(
            _ => new Uri(ContentUri),
            TestContext.Current.CancellationToken);

        var handle = Assert.Single(recovered);

        await WaitUntilAsync(() => handle.StateMachine.Current == DownloadState.Completed);

        // NO se pide ningún tramo: la descarga completa NO se pierde por un 416
        // (AD-20, AD-24).
        Assert.Empty(engine.RequestedStarts);

        // Y la operación termina como completada, con su parcial intacto (AD-8).
        Assert.Equal(DownloadState.Completed, handle.StateMachine.Current);
    }

    /// <summary>
    /// A download that is NOT complete is transferred again from its persisted offset, así
    /// que la validación no cambia el camino de la reanudación (AD-20).
    /// </summary>
    [Fact]
    public async Task An_incomplete_download_is_still_transferred_from_its_offset()
    {
        var store = new RecordingStateStore();

        store.PersistedOperations.Add(
            new PersistedDownloadOperation(
                "operacion-1",
                PackageId,
                DownloadState.Downloading.ToString(),
                TransferEngineRegistry.HttpRangeTransferEngine,
                400,
                1000));

        var engine = new RecordingTransferEngine(totalBytes: 1000);

        var coordinator = CreateCoordinator(
            new RecordingStateStoreWithOperations(store),
            engine,
            CreateValidation("contenido del paquete"));

        await coordinator.RecoverAsync(
            _ => new Uri(ContentUri),
            TestContext.Current.CancellationToken);

        await WaitUntilAsync(() => engine.RequestedStarts.Count > 0);

        // La reanudación sigue pidiendo el rango desde el offset persistido (AD-20).
        Assert.Equal(400, engine.RequestedStarts[0]);
    }

    /// <summary>
    /// Builds the use case of the validation over the doubles of the ports, con un
    /// manifiesto que declara el hash del contenido dado (AD-2, AD-8).
    /// </summary>
    /// <param name="content">Content that the manifest declares.</param>
    /// <returns>The use case of the validation.</returns>
    private static ValidatePackage CreateValidation(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);

        var signer = new Ed25519ManifestSigner();

        var manifest = new InstallationManifest(
            ValidatePackage.SupportedManifestSchemaVersion,
            PackageId,
            "1.0.0",
            bytes.LongLength,
            LANPartyHub.Client.Domain.Integrity.PackageIntegrity.ComputeSha256(bytes),
            new InstallationManifest.LaunchSettings("g.exe", ["-novid"], "g"),
            []);

        var canonical = LANPartyHub.Server.Domain.Packages.ManifestCanonicalizer.Canonicalize(manifest);

        var cache = new FixedManifestCache(canonical);

        // El manifiesto FIRMADO por la clave del ancla fijada es el que el servidor
        // sirve, así que la autenticidad valida y la fase de la integridad lo lee de la
        // caché (AD-2, AD-8).
        var envelope = new InstallationManifest.SignedEnvelope(
            canonical,
            signer.Sign(canonical),
            signer.GetKeyId());

        return new ValidatePackage(
            new FixedManifestClient(envelope),
            new LANPartyHub.Client.Infrastructure.Trust.Ed25519ManifestVerifier(),
            cache,
            new FixedTrustAnchorReader(
                new PinnedTrustAnchor(
                    Convert.ToBase64String(Ed25519ManifestSigner.ComputePublicKey(Environment.MachineName)),
                    "fingerprint",
                    "2026-10-26T00:00:00.0000000Z")),
            NullLogger<ValidatePackage>.Instance);
    }

    /// <summary>
    /// Builds the coordinator of the downloads over the doubles of the ports (AD-1, AD-16).
    /// </summary>
    /// <param name="store">Port of the persistence of the state.</param>
    /// <param name="engine">Port of the transfer of the bytes.</param>
    /// <param name="validation">Use case of the validation of the package.</param>
    /// <param name="layout">Layout of the data of the client.</param>
    /// <returns>The coordinator of the downloads.</returns>
    private DownloadCoordinator CreateCoordinator(
        IClientStateStore store,
        ITransferEngine engine,
        ValidatePackage validation,
        IClientDataLayout? layout = null)
    {
        var effectiveLayout = layout ?? new TemporaryDataLayout(_temporaryFolders);

        return new DownloadCoordinator(
            engine,
            new RecordingIntentClient(),
            store,
            effectiveLayout,
            NullLogger<DownloadCoordinator>.Instance,
            validation);
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
    /// Engine que ESCRIBE los bytes del contenido en el destino y RECUERDA los inicios de
    /// rango que le pidieron, para que la ausencia de la transferencia sea observable
    /// (AD-20).
    /// </summary>
    private sealed class RecordingTransferEngine : ITransferEngine
    {
        private readonly long _totalBytes;
        private readonly byte[] _content;
        private readonly List<long> _requestedStarts = [];
        private readonly Lock _lock = new();

        public RecordingTransferEngine(long totalBytes, byte[]? content = null)
        {
            _totalBytes = totalBytes;
            _content = content ?? [];
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

            if (_content.Length > 0)
            {
                await destination.WriteAsync(_content.AsMemory(), cancellationToken).ConfigureAwait(false);
            }

            return new TransferOutcome(_content.LongLength, _totalBytes, RangeHonoured: true);
        }
    }

    /// <summary>
    /// Adapter of the registration of the intention that devuelve un identificador fijo
    /// (AC1).
    /// </summary>
    private sealed class RecordingIntentClient : IDownloadIntentClient
    {
        public Task<string> RegisterAsync(string packageId, CancellationToken cancellationToken) =>
            Task.FromResult("operacion-1");
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
    /// Port of the read of la ancla de confianza que siempre devuelve el mismo valor
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
    }

    /// <summary>
    /// Layout of a folder temporal, para que el parcial de la descarga viva solo en la
    /// carpeta de las descargas (AD-9).
    /// </summary>
    private sealed class TemporaryDataLayout : IClientDataLayout
    {
        private readonly string _folder;

        public TemporaryDataLayout(List<string> temporaryFolders)
        {
            _folder = Directory.CreateTempSubdirectory("lan-party-hub-validation-persistence-").FullName;

            temporaryFolders.Add(_folder);

            Directory.CreateDirectory(DownloadsDirectory);
        }        public string Folder => _folder;

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
