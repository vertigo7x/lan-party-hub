using System.Text;
using LANPartyHub.Client.Application;
using LANPartyHub.Client.Application.Install;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Domain;
using LANPartyHub.Client.Domain.Install;
using LANPartyHub.Client.Domain.Trust;
using LANPartyHub.Client.Infrastructure.Filesystem;
using LANPartyHub.Client.Infrastructure.Trust;
using LANPartyHub.Server.Infrastructure.Signing;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The installation that FAILS publishes the REAL phase of the failure, UNA sola vez
/// (AD-22, FR-10).
///
/// <para>
/// Una instalación cuyo árbol falla deja la operación TERMINADA en fallo, así que el
/// organizador ve que la operación terminó en fallo y no solo que desapareció: la fase
/// <c>Failed</c> llega al servidor con el identificador de la operación (AD-22).
/// </para>
/// <para>
/// El aviso a la vista local es ÚNICO por fase: la barra no se repite con la misma fase
/// (AC4).
/// </para>
/// <para>
/// Un rechazo TEMPRANO —manifiesto no servido, firma inválida o integridad rota— publica
/// ALSO la fase de fallo antes de terminar, así que el servidor nunca se queda con una
/// operación que desapareció sin más (AD-22, FR-10).
/// </para>
/// </summary>
public sealed class InstallationFailurePhasePublicationTests : IDisposable
{
    private const string GameId = "0193f2a1-b2c3-4d4e-5f60-718293a4b5c6";
    private const string PackageId = "paquete-1";
    private const string Version = "1.0.0";
    private const string ClientId = "0193f2a1b2c34d4e5f60718293a4b5c6";

    private readonly List<string> _temporaryFolders = [];

    /// <summary>
    /// An installation whose tree FAILS publishes the phase <c>Failed</c> to the server and
    /// raises <c>ProgressChanged</c> with <c>Failed</c> UNA sola vez (AD-22, FR-10, AC4).
    /// </summary>
    [Fact]
    public async Task A_failing_tree_publishes_the_failed_phase_once()
    {
        var fixture = await CreateFixtureAsync(new FailingTreeWriter());

        await File.WriteAllBytesAsync(
            fixture.PartialPath,
            fixture.PackageBytes,
            TestContext.Current.CancellationToken);

        var received = new List<InstallProgress>();

        fixture.Coordinator.ProgressChanged += progress => received.Add(progress);

        var result = await fixture.Coordinator
            .InstallAsync(GameId, PackageId, Version, TestContext.Current.CancellationToken);

        // AC5: la instalación que falla al aplicar el manifiesto queda RECHAZADA.
        Assert.True(result.IsRejected);

        // AD-22, FR-10: la fase REAL de la FALLA llega al servidor con el identificador
        // de la operación, así que el organizador ve que la operación terminó en fallo.
        var failed = Assert.Single(
            fixture.Progress.Phases,
            phase => phase.Phase == nameof(InstallPhase.Failed));

        Assert.Equal(InstallationState.Failed, failed.State);
        Assert.NotEmpty(failed.OperationId);

        // AC4: la vista local recibe el aviso de fallo UNA sola vez, así que la barra no
        // se repite con la misma fase.
        Assert.Single(
            received,
            progress => progress.Phase == InstallPhase.Failed);
    }

    /// <summary>
    /// The failure phase reaches the server BEFORE the operation leaves the tracker, así
    /// que el servidor sabe que la operación terminó (AD-22, FR-10).
    /// </summary>
    [Fact]
    public async Task A_failing_tree_closes_the_operation_after_publishing_the_failure()
    {
        var fixture = await CreateFixtureAsync(new FailingTreeWriter());

        await File.WriteAllBytesAsync(
            fixture.PartialPath,
            fixture.PackageBytes,
            TestContext.Current.CancellationToken);

        await fixture.Coordinator
            .InstallAsync(GameId, PackageId, Version, TestContext.Current.CancellationToken);

        // AD-22: la operación deja de estar EN CURSO.
        Assert.Empty(fixture.Tracker.ListActiveOperations());

        // AD-22, FR-10: la vista del servidor NO conserva la operación fallida como
        // activa.
        Assert.NotEmpty(fixture.Publisher.States);
        Assert.Empty(fixture.Publisher.States[^1].ActiveOperations);
    }

    /// <summary>
    /// A rejection EARLY —the server did not serve a signed manifest— publishes the phase
    /// <c>Failed</c> before the operation finishes (AD-22, FR-10).
    /// </summary>
    [Fact]
    public async Task A_manifest_that_the_server_does_not_serve_publishes_the_failed_phase()
    {
        var fixture = await CreateFixtureAsync(envelope: null);

        var received = new List<InstallProgress>();

        fixture.Coordinator.ProgressChanged += progress => received.Add(progress);

        var result = await fixture.Coordinator
            .InstallAsync(GameId, PackageId, Version, TestContext.Current.CancellationToken);

        Assert.True(result.IsRejected);

        // AD-22, FR-10: el rechazo TEMPRANO publica ALSO la fase de fallo, así que el
        // servidor no se queda con una operación que desapareció sin más.
        var failed = Assert.Single(
            fixture.Progress.Phases,
            phase => phase.Phase == nameof(InstallPhase.Failed));

        Assert.Equal(InstallationState.Failed, failed.State);

        // AC4: el aviso local de la fase de fallo es ÚNICO.
        Assert.Single(received, progress => progress.Phase == InstallPhase.Failed);
    }

    /// <summary>
    /// A rejection EARLY by an INVALID SIGNATURE publishes the phase <c>Failed</c> before
    /// the operation finishes (AD-22, FR-10).
    /// </summary>
    [Fact]
    public async Task An_invalid_signature_publishes_the_failed_phase()
    {
        var fixture = await CreateFixtureAsync(tamperSignature: true);

        var result = await fixture.Coordinator
            .InstallAsync(GameId, PackageId, Version, TestContext.Current.CancellationToken);

        Assert.True(result.IsRejected);

        // AD-22, FR-10: la fase de fallo se publica ANTES de terminar.
        var failed = Assert.Single(
            fixture.Progress.Phases,
            phase => phase.Phase == nameof(InstallPhase.Failed));

        Assert.Equal(InstallationState.Failed, failed.State);
    }

    /// <summary>
    /// A rejection EARLY by BROKEN INTEGRITY publishes the phase <c>Failed</c> before the
    /// operation finishes (AD-22, FR-10).
    /// </summary>
    [Fact]
    public async Task A_broken_integrity_publishes_the_failed_phase()
    {
        var fixture = await CreateFixtureAsync();

        // Los bytes del parcial NO coinciden con el hash que declara el manifiesto
        // firmado, así que la operación se rechaza por integridad (AD-8).
        await File.WriteAllBytesAsync(
            fixture.PartialPath,
            Encoding.UTF8.GetBytes("contenido alterado"),
            TestContext.Current.CancellationToken);

        var result = await fixture.Coordinator
            .InstallAsync(GameId, PackageId, Version, TestContext.Current.CancellationToken);

        Assert.True(result.IsRejected);
        Assert.Equal(ErrorCatalog.PackageIntegrityMismatchCode, result.Error!.Code);

        // AD-22, FR-10: la fase de fallo se publica ANTES de terminar.
        var failed = Assert.Single(
            fixture.Progress.Phases,
            phase => phase.Phase == nameof(InstallPhase.Failed));

        Assert.Equal(InstallationState.Failed, failed.State);
    }

    /// <summary>
    /// Builds the fixture of an installation con el tracker de las operaciones en curso y
    /// el publicador del estado (AD-16).
    /// </summary>
    /// <param name="tree">
    /// Port of the application of the tree, or <c>null</c> when the installation applies
    /// the tree without falla (AD-16).
    /// </param>
    /// <param name="envelope">
    /// Signed envelope that the server serves, or <c>null</c> when the server did not
    /// serve one (AD-16).
    /// </param>
    /// <param name="tamperSignature">
    /// Whether the signature of the envelope is altered, así que no valida contra el
    /// ancla fijada (AD-27).
    /// </param>
    /// <returns>The fixture of the installation.</returns>
    private async Task<InstallationFixture> CreateFixtureAsync(
        IInstallTreeWriter? tree = null,
        InstallationManifest.SignedEnvelope? envelope = null,
        bool tamperSignature = false)
    {
        var folder = Directory.CreateTempSubdirectory("lan-party-hub-failure-");

        _temporaryFolders.Add(folder.FullName);

        var layout = new TemporaryDataLayout(folder.FullName);

        var signer = new Ed25519ManifestSigner();
        var packageBytes = Encoding.UTF8.GetBytes("contenido del paquete");

        var manifest = new InstallationManifest(
            1,
            PackageId,
            Version,
            packageBytes.LongLength,
            LANPartyHub.Client.Domain.Integrity.PackageIntegrity.ComputeSha256(packageBytes),
            new InstallationManifest.LaunchSettings("game.exe", ["-novid"], "game"),
            [new InstallationManifest.Verify("game/game.exe")]);

        var canonical = LANPartyHub.Server.Domain.Packages.ManifestCanonicalizer.Canonicalize(manifest);

        var signature = tamperSignature
            ? new string(signer.Sign(canonical).Select(character => character == 'A' ? 'B' : 'A').ToArray())
            : signer.Sign(canonical);

        var signedEnvelope = envelope ?? new InstallationManifest.SignedEnvelope(
            canonical,
            signature,
            signer.GetKeyId());

        var publisher = new RecordingStatePublisher();
        var progress = new RecordingProgressReporter();
        var tracker = new ActiveInstallations(progress);

        // AD-22: la publicación del estado COMPLETO se enlaza TARDE, con la publicación
        // del registro del equipo.
        tracker.ConfigureStatePublication(cancellationToken =>
            publisher.PublishAsync(
                new ClientStateChanged(
                    ClientId,
                    ConnectionState.AnchorVerified,
                    [],
                    tracker.ListActiveOperations()),
                cancellationToken));

        var stateStore = new RecordingStateStore();

        var paths = new InstallPathResolver(layout);

        var coordinator = new InstallationCoordinator(
            new FixedManifestClient(signedEnvelope),
            new Ed25519ManifestVerifier(),
            new FixedTrustAnchorReader(DefaultAnchor(signer)),
            new RecordingManifestCache(),
            layout,
            paths,
            tree ?? new ReportingTreeWriter(),
            stateStore,
            NullLogger<InstallationCoordinator>.Instance,
            tracker);

        await Task.CompletedTask;

        return new InstallationFixture(
            coordinator,
            publisher,
            progress,
            tracker,
            layout.PartialFilePath(PackageId),
            packageBytes);
    }

    /// <summary>
    /// Builds the ancla de confianza que el cliente tiene fijada (AD-27).
    /// </summary>
    /// <param name="signer">Signer of the event.</param>
    /// <returns>The fixed anchor.</returns>
    private static PinnedTrustAnchor DefaultAnchor(Ed25519ManifestSigner signer) =>
        new(
            Convert.ToBase64String(Ed25519ManifestSigner.ComputePublicKey(Environment.MachineName)),
            "fingerprint",
            "2026-10-26T00:00:00.0000000Z");

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
            catch (UnauthorizedAccessException)
            {
                // A file that the operating system still holds does not fail the test.
            }
        }
    }

    /// <summary>
    /// Fixture of an installation: el caso de uso y los dobles que hacen las
    /// publicaciones observables (AD-16).
    /// </summary>
    private sealed record InstallationFixture(
        InstallationCoordinator Coordinator,
        RecordingStatePublisher Publisher,
        RecordingProgressReporter Progress,
        ActiveInstallations Tracker,
        string PartialPath,
        byte[] PackageBytes);

    /// <summary>
    /// Port of the read of the signed manifest that always answers the same envelope
    /// (AD-16).
    /// </summary>
    private sealed class FixedManifestClient : ISignedManifestClient
    {
        private readonly InstallationManifest.SignedEnvelope? _envelope;

        public FixedManifestClient(InstallationManifest.SignedEnvelope? envelope) => _envelope = envelope;

        public Task<InstallationManifest.SignedEnvelope?> GetAsync(string packageId, CancellationToken cancellationToken) =>
            Task.FromResult(_envelope);
    }

    /// <summary>
    /// Port of the cache of the signed manifests que RECUERDA lo que se le escribió
    /// (AD-16).
    /// </summary>
    private sealed class RecordingManifestCache : IManifestCache
    {
        public List<string> StoredManifests { get; } = [];

        public Task StoreAsync(
            string packageId,
            string canonicalManifest,
            CancellationToken cancellationToken)
        {
            StoredManifests.Add(canonicalManifest);

            return Task.CompletedTask;
        }

        public Task<string?> LoadAsync(string packageId, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(StoredManifests.Count == 0 ? null : StoredManifests[^1]);
    }

    /// <summary>
    /// Port of the read la ancla de confianza que siempre devuelve el mismo valor
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
    /// Port of the application of the tree that PUBLICA las fases REALES de la operación,
    /// en el ORDEN en que las ejecuta (AD-16).
    /// </summary>
    private sealed class ReportingTreeWriter : IInstallTreeWriter
    {
        public Task<InstallTreeOutcome> ApplyAsync(
            InstallationManifest manifest,
            string rootPath,
            string? packagePath,
            CancellationToken cancellationToken) =>
            Task.FromResult(new InstallTreeOutcome(true, true));

        public Task<InstallTreeOutcome> ApplyAsync(
            InstallationManifest manifest,
            string rootPath,
            string? packagePath,
            IInstallProgressSink progress,
            CancellationToken cancellationToken)
        {
            // AD-22: las fases intermedias de la operación, en su ORDEN real.
            progress.Report(InstallPhase.Extracting, 512, 2048);
            progress.Report(InstallPhase.AwaitingConfirmation, 1024, 2048);
            progress.Report(InstallPhase.ApplyingManifest, 1536, 2048);

            return Task.FromResult(new InstallTreeOutcome(true, true));
        }
    }

    /// <summary>
    /// Port of the application of the tree that FAILS al aplicar el manifiesto, así que
    /// la operación queda FALLIDA y reintentable (AD-8, AC5).
    /// </summary>
    private sealed class FailingTreeWriter : IInstallTreeWriter
    {
        public Task<InstallTreeOutcome> ApplyAsync(
            InstallationManifest manifest,
            string rootPath,
            string? packagePath,
            CancellationToken cancellationToken) =>
            Task.FromResult(new InstallTreeOutcome(true, true));

        public Task<InstallTreeOutcome> ApplyAsync(
            InstallationManifest manifest,
            string rootPath,
            string? packagePath,
            IInstallProgressSink progress,
            CancellationToken cancellationToken)
        {
            // La operación falla DESPUÉS de publicar su primera fase, así que la fase
            // intermedia no se pierde y la de fallo llega después (AD-22).
            progress.Report(InstallPhase.Extracting, 512, 2048);

            throw new InvalidOperationException("No se pudo aplicar el manifiesto.");
        }
    }

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
    /// Reporter of the progress de la instalación que RECUERDA las fases que se
    /// publicaron (AD-16).
    /// </summary>
    private sealed class RecordingProgressReporter : IInstallationProgressReporter
    {
        public List<InstallationProgressUpdated> Phases { get; } = [];

        public Task ReportAsync(InstallationProgressUpdated progress, CancellationToken cancellationToken)
        {
            Phases.Add(progress);

            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Store of the client que RECUERDA las instalaciones que se le escribieron (AD-16).
    /// </summary>
    private sealed class RecordingStateStore : IClientStateStore
    {
        public List<ClientInstallation> SavedInstallations { get; } = [];

        public List<string> SavedFailureCodes { get; } = [];

        public Task MigrateAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<ClientIdentity?> LoadIdentityAsync(CancellationToken cancellationToken) =>
            Task.FromResult<ClientIdentity?>(ClientIdentity.FromCanonical(ClientId));

        public Task SaveIdentityAsync(ClientIdentity identity, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<PersistedDownloadOperation>> LoadDownloadOperationsAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PersistedDownloadOperation>>(Array.Empty<PersistedDownloadOperation>());

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

        public Task SaveInstallationAsync(ClientInstallation installation, CancellationToken cancellationToken)
        {
            SavedInstallations.Add(installation);

            return Task.CompletedTask;
        }

        public Task<ClientInstallation?> LoadInstallationAsync(string gameId, CancellationToken cancellationToken) =>
            Task.FromResult<ClientInstallation?>(SavedInstallations.Count == 0 ? null : SavedInstallations[^1]);

        public Task<IReadOnlyList<ClientInstallation>> LoadInstallationsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ClientInstallation>>(SavedInstallations);

        public Task SaveInstallationFailureAsync(
            ClientInstallation installation,
            string errorCode,
            CancellationToken cancellationToken)
        {
            SavedInstallations.Add(installation);
            SavedFailureCodes.Add(errorCode);

            return Task.CompletedTask;
        }

        public Task<string?> LoadInstallationFailureAsync(string gameId, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(SavedFailureCodes.Count == 0 ? null : SavedFailureCodes[^1]);
    }

    /// <summary>
    /// Layout of a folder temporal, para que la raíz de instalación, el parcial y la
    /// caché vivan en el sitio que declara el layout (AD-9).
    /// </summary>
    private sealed class TemporaryDataLayout : IClientDataLayout
    {
        private readonly string _folder;
        private string _installDirectory;

        public TemporaryDataLayout(string folder)
        {
            _folder = folder;
            _installDirectory = Path.Combine(folder, "install");

            Directory.CreateDirectory(DownloadsDirectory);
            Directory.CreateDirectory(ManifestsDirectory);
        }

        public string Folder => _folder;

        public string InstallDirectory => _installDirectory;

        public string DownloadsDirectory => Path.Combine(_folder, "downloads");

        public string ManifestsDirectory => Path.Combine(_folder, "manifests");

        public string StateDatabasePath => Path.Combine(_folder, "state.sqlite");

        public string PartialFilePath(string packageId) =>
            Path.Combine(DownloadsDirectory, packageId + ".part");

        public void EnsureCreated()
        {
            Directory.CreateDirectory(DownloadsDirectory);
            Directory.CreateDirectory(ManifestsDirectory);
        }

        public void SetInstallDirectory(string installDirectory) =>
            _installDirectory = installDirectory;
    }
}
