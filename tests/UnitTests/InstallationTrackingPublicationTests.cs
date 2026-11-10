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
/// The TRACKING of an installation operation and the PUBLICATION of the state of the
/// client that goes with it (AD-22, FR-10).
///
/// <para>
/// El coordinador DECLARA al servidor la operación de instalación que empieza y la que
/// termina: la lista de operaciones en curso la publica el REGISTRO del equipo, así que
/// el tracker se ENLAZA TARDE a esa publicación (AD-22).
/// </para>
/// <para>
/// La publicación es de MEJOR ESFUERZO: un servidor que no recibe el estado no puede
/// impedir que el manifiesto se aplique (AD-1, AD-22).
/// </para>
/// </summary>
public sealed class InstallationTrackingPublicationTests : IDisposable
{
    private const string GameId = "0193f2a1-b2c3-4d4e-5f60-718293a4b5c6";
    private const string PackageId = "paquete-1";
    private const string Version = "1.0.0";
    private const string ClientId = "0193f2a1b2c34d4e5f60718293a4b5c6";

    private readonly List<string> _temporaryFolders = [];

    /// <summary>
    /// The installation that BEGINS is declared to the server, its REAL phase is
    /// published, and the installation that ENDS leaves the list of operations in course
    /// (AD-22, FR-10).
    /// </summary>
    [Fact]
    public async Task The_installation_declares_the_operation_that_begins_and_the_one_that_ends()
    {
        var fixture = await CreateFixtureAsync();

        // El parcial contiene EXACTAMENTE los bytes que declara el manifiesto firmado.
        await File.WriteAllBytesAsync(
            fixture.PartialPath,
            fixture.PackageBytes,
            TestContext.Current.CancellationToken);

        await fixture.Coordinator
            .InstallAsync(GameId, PackageId, Version, TestContext.Current.CancellationToken);

        // AD-22, FR-10: la operación EN CURSO se declara al empezar, así que el servidor
        // la ve desde el principio.
        var declared = Assert.Single(fixture.Publisher.States[0].ActiveOperations);
        Assert.Equal(ActiveInstallations.InstallationOperationTypeName, declared.OperationType);
        Assert.Equal(nameof(InstallationState.Pending), declared.State);

        // La fase REAL viaja al servidor con el identificador de la operación (AD-22).
        var phase = Assert.Single(fixture.Progress.Phases, item => item.Phase == nameof(InstallPhase.Extracting));
        Assert.Equal(declared.OperationId, phase.OperationId);
        Assert.Equal(nameof(InstallPhase.Extracting), phase.Phase);

        // La operación TERMINADA deja de estar en curso (AD-22, AD-26).
        Assert.Empty(fixture.Publisher.States[^1].ActiveOperations);
    }

    /// <summary>
    /// A publication that FAILS does NOT stop the installation: el manifiesto se aplica
    /// igualmente y la operación se certifica (AD-1, AD-22).
    /// </summary>
    [Fact]
    public async Task A_publication_that_fails_does_not_abort_the_installation()
    {
        var fixture = await CreateFixtureAsync((_, _) => throw new InvalidOperationException("el servidor no acepta la publicación"));

        await File.WriteAllBytesAsync(
            fixture.PartialPath,
            fixture.PackageBytes,
            TestContext.Current.CancellationToken);

        var result = await fixture.Coordinator
            .InstallAsync(GameId, PackageId, Version, TestContext.Current.CancellationToken);

        // AD-1, AD-22: la proyección es un complemento y no el motivo de vida del
        // cliente, así que la instalación termina con su veredicto.
        Assert.True(result.IsCertified);
        Assert.True(result.Verdict!.IsInstalled);
    }

    /// <summary>
    /// A cancellation is PRESERVED: la publicación cancelada no se traga la cancelación
    /// (AD-1).
    /// </summary>
    [Fact]
    public async Task A_cancelled_publication_preserves_the_cancellation()
    {
        using var cancellation = new CancellationTokenSource();

        var fixture = await CreateFixtureAsync(async (_, token) =>
        {
            cancellation.Cancel();

            throw new OperationCanceledException(token);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Coordinator.InstallAsync(
                GameId,
                PackageId,
                Version,
                cancellation.Token));
    }

    /// <summary>
    /// A CANCELLED installation leaves NO operation in course: la operación cancelada no
    /// puede quedar EN CURSO para siempre, así que el tracker la suelta (AD-22, FR-10).
    /// </summary>
    [Fact]
    public async Task A_cancelled_installation_leaves_no_operation_in_course()
    {
        using var cancellation = new CancellationTokenSource();

        var fixture = await CreateFixtureAsync(cancellation: cancellation);

        await File.WriteAllBytesAsync(
            fixture.PartialPath,
            fixture.PackageBytes,
            TestContext.Current.CancellationToken);

        // La instalación se cancela CUANDO el árbol ya está aplicando el manifiesto, así
        // que la operación ya está declarada en curso (AD-22).

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Coordinator.InstallAsync(
                GameId,
                PackageId,
                Version,
                cancellation.Token));

        // AD-22: la operación cancelada deja de estar EN CURSO, así que el servidor no
        // la muestra como activa para siempre.
        Assert.Empty(fixture.Tracker.ListActiveOperations());
    }

    /// <summary>
    /// Builds the fixture of an installation with el tracker de las operaciones en curso
    /// y el publicador del estado (AD-16).
    /// </summary>
    /// <param name="publication">
    /// Publication of the state of the client, or <c>null</c> when the publication
    /// succeeds (AD-16).
    /// </param>
    /// <returns>The fixture of the installation.</returns>
    private async Task<InstallationFixture> CreateFixtureAsync(
        Func<ActiveInstallations, CancellationToken, Task>? publication = null,
        CancellationTokenSource? cancellation = null)
    {
        var folder = Directory.CreateTempSubdirectory("lan-party-hub-tracking-");

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

    /// <summary>
    /// Creates a temporary folder for the test.
    /// </summary>
    /// <returns>The absolute path of the folder.</returns>
    private string CreateTemporaryFolder()
    {
        var folder = Directory.CreateTempSubdirectory("lan-party-hub-tracking-");

        _temporaryFolders.Add(folder.FullName);

        return folder.FullName;
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
    /// Port of the application of the tree que PUBLICA la fase REAL de la operación
    /// (AD-16).
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
            // AD-22: la fase REAL de la extracción, que es la que el tracker declara.
            progress.Report(InstallPhase.Extracting, 512, 2048);

            // La operación se cancela CON la fase ya publicada, así que la instalación
            // muere con la operación declarada en curso (AD-22).
            _cancellation?.Cancel();

            cancellationToken.ThrowIfCancellationRequested();

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
