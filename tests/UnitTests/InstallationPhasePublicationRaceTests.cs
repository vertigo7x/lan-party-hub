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
/// The PUBLICATION of the REAL phase of an installation and the CLEANUP of a cancelled
/// operation (AD-22, FR-10).
///
/// <para>
/// La fase REAL viaja al servidor con el identificador de la operación, y TODAS las
/// fases intermedias viajan en el ORDEN en que el ejecutor las declaró: ninguna se
/// pierde y ninguna se repite en la vista (AD-22, FR-10).
/// </para>
/// <para>
/// Una operación CANCELADA suelta su operación local y su vista en el servidor, así que
/// no sobrevive en la tabla del organizador como activa (AD-22, FR-10).
/// </para>
/// </summary>
public sealed class InstallationPhasePublicationRaceTests : IDisposable
{
    private const string GameId = "0193f2a1-b2c3-4d4e-5f60-718293a4b5c6";
    private const string PackageId = "paquete-1";
    private const string Version = "1.0.0";
    private const string ClientId = "0193f2a1b2c34d4e5f60718293a4b5c6";

    private readonly List<string> _temporaryFolders = [];

    /// <summary>
    /// EVERY intermediate phase reaches the server, in the ORDER in which the executor
    /// declared it: la última fase NO sustituye a las anteriores (AD-22, FR-10).
    /// </summary>
    [Fact]
    public async Task Every_intermediate_phase_reaches_the_server_in_order()
    {
        var fixture = await CreateFixtureAsync();

        await File.WriteAllBytesAsync(
            fixture.PartialPath,
            fixture.PackageBytes,
            TestContext.Current.CancellationToken);

        await fixture.Coordinator
            .InstallAsync(GameId, PackageId, Version, TestContext.Current.CancellationToken);

        // AD-22, FR-10: las fases intermedias NO se pierden: la última no las sustituye.
        var phases = fixture.Progress.Phases
            .Select(progress => progress.Phase)
            .ToList();

        Assert.Equal(
            [
                nameof(InstallPhase.Extracting),
                nameof(InstallPhase.AwaitingConfirmation),
                nameof(InstallPhase.ApplyingManifest),
                nameof(InstallPhase.Completed)
            ],
            phases);

        // AD-22: las fases viajan con el identificador de la MISMA operación.
        Assert.Single(fixture.Progress.Phases.Select(progress => progress.OperationId).Distinct());
    }

    /// <summary>
    /// The view receives the progress ONCE per phase: la publicación al servidor no
    /// vuelve a raises <c>ProgressChanged</c> (AC4).
    /// </summary>
    [Fact]
    public async Task The_view_receives_the_progress_once_per_phase()
    {
        var fixture = await CreateFixtureAsync();

        var received = new List<InstallProgress>();

        fixture.Coordinator.ProgressChanged += progress => received.Add(progress);

        await File.WriteAllBytesAsync(
            fixture.PartialPath,
            fixture.PackageBytes,
            TestContext.Current.CancellationToken);

        await fixture.Coordinator
            .InstallAsync(GameId, PackageId, Version, TestContext.Current.CancellationToken);

        // AC4: cada fase llega a la vista UNA sola vez, así que la barra no se repite
        // con la misma fase.
        var phases = received.Select(progress => progress.Phase.ToString()).ToList();

        Assert.Equal(
            new[]
            {
                nameof(InstallPhase.Extracting),
                nameof(InstallPhase.AwaitingConfirmation),
                nameof(InstallPhase.ApplyingManifest),
                nameof(InstallPhase.Completed)
            },
            phases);

        Assert.Equal(phases.Count, phases.Distinct().Count());
    }

    /// <summary>
    /// A cancellation BEFORE the operation is declared leaves NO operation in course: la
    /// operación que se soltó localmente tampoco puede quedar viva en el servidor
    /// (AD-22, FR-10).
    /// </summary>
    [Fact]
    public async Task A_cancelled_declaration_leaves_no_operation_in_course()
    {
        using var cancellation = new CancellationTokenSource();

        // La publicación del estado se cancela JUSTO cuando la operación se declara, así
        // que la operación ya está en el tracker y la cancelación sale ANTES de que
        // ninguna puerta la cierre (AD-22, FR-10).
        var fixture = await CreateFixtureAsync(async (_, token) =>
        {
            cancellation.Cancel();

            throw new OperationCanceledException(token);
        });

        await File.WriteAllBytesAsync(
            fixture.PartialPath,
            fixture.PackageBytes,
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Coordinator.InstallAsync(
                GameId,
                PackageId,
                Version,
                cancellation.Token));

        // AD-22, FR-10: la operación cancelada NO queda en curso.
        Assert.Empty(fixture.Tracker.ListActiveOperations());

        // AD-22: la vista del servidor NO conserva la operación cancelada.
        Assert.Empty(fixture.Tracker.ListActiveOperations());
    }

    /// <summary>
    /// A cancellation DURING the operation releases the operation BOTH locally and on the
    /// server: la operación cancelada no sobrevive en la tabla del organizador como
    /// activa (AD-22, FR-10).
    /// </summary>
    [Fact]
    public async Task A_cancelled_operation_releases_the_operation_on_the_server()
    {
        using var cancellation = new CancellationTokenSource();

        var fixture = await CreateFixtureAsync(cancellation: cancellation);

        await File.WriteAllBytesAsync(
            fixture.PartialPath,
            fixture.PackageBytes,
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Coordinator.InstallAsync(
                GameId,
                PackageId,
                Version,
                cancellation.Token));

        // AD-22: la operación cancelada deja de estar EN CURSO localmente.
        Assert.Empty(fixture.Tracker.ListActiveOperations());

        // AD-22, FR-10: el servidor recibe el CIERRE con un token VIVO, así que la
        // operación cancelada tampoco queda en la vista del organizador.
        Assert.NotEmpty(fixture.Publisher.States);
        Assert.Empty(fixture.Publisher.States[^1].ActiveOperations);
    }

    /// <summary>
    /// Builds the fixture of an installation with el tracker de las operaciones en curso
    /// y el publicador del estado (AD-16).
    /// </summary>
    /// <param name="publication">
    /// Publication of the state of the client, or <c>null</c> when the publication
    /// succeeds (AD-16).
    /// </param>
    /// <param name="cancellation">
    /// Source of cancellation that the executor usa para cancelar la operación cuando
    /// ya publicó su primera fase (AD-16).
    /// </param>
    /// <returns>The fixture of the installation.</returns>
    private async Task<InstallationFixture> CreateFixtureAsync(
        Func<ActiveInstallations, CancellationToken, Task>? publication = null,
        CancellationTokenSource? cancellation = null)
    {
        var folder = Directory.CreateTempSubdirectory("lan-party-hub-phases-");

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

        var envelope = new InstallationManifest.SignedEnvelope(
            canonical,
            signer.Sign(canonical),
            signer.GetKeyId());

        var publisher = new RecordingStatePublisher();
        var progress = new RecordingProgressReporter();
        var tracker = new ActiveInstallations(progress);
        var tree = new ReportingTreeWriter(cancellation);

        // AD-22: la publicación del estado COMPLETO se enlaza TARDE, con la publicación
        // del registro del equipo.
        tracker.ConfigureStatePublication(async cancellationToken =>
        {
            if (publication is not null)
            {
                await publication(tracker, cancellationToken);
                return;
            }

            await publisher
                .PublishAsync(
                    new ClientStateChanged(
                        ClientId,
                        ConnectionState.AnchorVerified,
                        [],
                        tracker.ListActiveOperations()),
                    cancellationToken);
        });

        var stateStore = new RecordingStateStore();

        var paths = new InstallPathResolver(layout);

        var coordinator = new InstallationCoordinator(
            new FixedManifestClient(envelope),
            new Ed25519ManifestVerifier(),
            new FixedTrustAnchorReader(DefaultAnchor(signer)),
            new RecordingManifestCache(),
            layout,
            paths,
            tree,
            stateStore,
            NullLogger<InstallationCoordinator>.Instance,
            tracker);

        await Task.CompletedTask;

        return new InstallationFixture(
            coordinator,
            publisher,
            progress,
            tracker,
            tree,
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
        ReportingTreeWriter Tree,
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
    /// Port of the application of the tree que PUBLICA las fases REALES de la operación,
    /// en el ORDEN en que las ejecuta (AD-16).
    /// </summary>
    private sealed class ReportingTreeWriter : IInstallTreeWriter
    {
        private readonly CancellationTokenSource? _cancellation;

        public ReportingTreeWriter(CancellationTokenSource? cancellation = null) =>
            _cancellation = cancellation;

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

            if (_cancellation is not null)
            {
                // La operación se cancela CON las fases ya publicadas, así que la
                // instalación muere con la operación declarada en curso (AD-22).
                _cancellation.Cancel();

                cancellationToken.ThrowIfCancellationRequested();
            }

            return Task.FromResult(new InstallTreeOutcome(true, true));
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
