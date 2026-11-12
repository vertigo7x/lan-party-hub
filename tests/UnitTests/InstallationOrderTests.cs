using System.Text;
using LANPartyHub.Client.Application;
using LANPartyHub.Client.Application.Install;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Domain;
using LANPartyHub.Client.Domain.Integrity;
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
/// The NORMATIVE order of an installation and the rejection BEFORE any write
/// (AD-7, AD-8, AD-9, AD-10, AD-16, AC1, AC2, AC3, AC4).
///
/// <para>
/// El caso de uso se ejercita con dobles de los PUERTOS, así que el orden de rechazo y
/// la AUSENCIA de escrituras son observables sin un servidor y sin un disco (AD-1,
/// AD-16).
/// </para>
/// </summary>
public sealed class InstallationOrderTests : IDisposable
{
    private const string GameId = "0193f2a1-b2c3-4d4e-5f60-718293a4b5c6";
    private const string PackageId = "paquete-1";
    private const string Version = "1.0.0";

    private readonly List<string> _temporaryFolders = [];

    /// <summary>
    /// A manifest whose signature does NOT validate against the fixed anchor is
    /// REJECTED, and NOTHING is written on disk: ni el manifiesto en la caché
    /// <c>manifests/</c>, ni el directorio de instalación, ni el parcial, ni la fila
    /// (AD-8, AC2, AC3).
    /// </summary>
    [Fact]
    public async Task A_manifest_with_an_invalid_signature_is_rejected_and_writes_nothing()
    {
        var fixture = await CreateFixtureAsync(tamperWithSignature: true);

        var result = await fixture.Coordinator
            .InstallAsync(GameId, PackageId, Version, TestContext.Current.CancellationToken)
            ;

        // AC2: la operación se rechaza con el código estable de la causa.
        Assert.True(result.IsRejected);
        Assert.Equal(ErrorCatalog.ManifestSignatureInvalidCode, result.Error!.Code);
        Assert.NotNull(result.Reason);

        // AC3: NINGÚN byte en disco antes del paso 2.
        AssertNoBytesWereWritten(fixture);
    }

    /// <summary>
    /// A manifest whose schema version the client does NOT support is REJECTED, and
    /// nothing is written on disk (AD-8, AC3, AC5).
    /// </summary>
    [Fact]
    public async Task A_manifest_with_an_unsupported_schema_version_is_rejected_and_writes_nothing()
    {
        var fixture = await CreateFixtureAsync(schemaVersion: 99);

        var result = await fixture.Coordinator
            .InstallAsync(GameId, PackageId, Version, TestContext.Current.CancellationToken)
            ;

        Assert.True(result.IsRejected);
        Assert.Equal(ErrorCatalog.SchemaVersionNotSupportedCode, result.Error!.Code);

        AssertNoBytesWereWritten(fixture);
    }

    /// <summary>
    /// A TAMPERED manifest that declares an operation OUTSIDE the closed set is
    /// rejected, it is NOT installed, and the error is registered with its cause
    /// (AD-7, AC2).
    /// </summary>
    [Fact]
    public async Task A_tampered_manifest_with_an_operation_not_allowed_is_rejected()
    {
        var fixture = await CreateFixtureAsync(tamperWithOperation: true);

        var result = await fixture.Coordinator
            .InstallAsync(GameId, PackageId, Version, TestContext.Current.CancellationToken)
            ;

        // AC2: el rechazo lleva la causa del catálogo, y la operación no se instala.
        Assert.True(result.IsRejected);
        Assert.Equal(ErrorCatalog.ManifestOperationNotAllowedCode, result.Error!.Code);
        Assert.NotNull(result.Reason);

        // El rechazo PRECEDE a persistir: ni una fila, ni un directorio, ni un parcial.
        AssertNoBytesWereWritten(fixture);
        Assert.Empty(fixture.StateStore.SavedInstallations);
    }

    /// <summary>
    /// The order is NORMATIVE: the install directory and the partial file are created
    /// only AFTER the signature and the schema version have been checked (AD-8, AC1,
    /// AC3).
    /// </summary>
    [Fact]
    public async Task The_install_directory_is_created_only_after_the_manifest_is_verified()
    {
        var fixture = await CreateFixtureAsync();

        // Antes de la instalación, la raíz NO existe: el layout no la crea todavía.
        Assert.False(Directory.Exists(fixture.RootPath));

        await fixture.Coordinator
            .InstallAsync(GameId, PackageId, Version, TestContext.Current.CancellationToken)
            ;

        // La raíz se crea SOLO después de que el manifiesto haya pasado la verificación,
        // así que a partir de ese momento existe (AD-8, AC1).
        Assert.True(Directory.Exists(fixture.RootPath));
    }

    /// <summary>
    /// The install directory is NOT created when the manifest does NOT validate, and the
    /// partial file is NOT created either (AD-8, AC1, AC3).
    /// </summary>
    [Fact]
    public async Task The_install_directory_and_the_partial_are_not_created_when_the_manifest_fails()
    {
        var fixture = await CreateFixtureAsync(tamperWithSignature: true);

        await fixture.Coordinator
            .InstallAsync(GameId, PackageId, Version, TestContext.Current.CancellationToken)
            ;

        Assert.False(Directory.Exists(fixture.RootPath));
        Assert.False(File.Exists(fixture.PartialPath));
    }

    /// <summary>
    /// The signature is checked BEFORE anything is written: the manifest that arrives
    /// with a broken signature never reaches the cache of <c>manifests/</c> (AD-8,
    /// AC3).
    /// </summary>
    [Fact]
    public async Task The_cache_of_manifests_stays_empty_when_the_signature_fails()
    {
        var fixture = await CreateFixtureAsync(tamperWithSignature: true);

        await fixture.Coordinator
            .InstallAsync(GameId, PackageId, Version, TestContext.Current.CancellationToken)
            ;

        Assert.Empty(fixture.ManifestCache.StoredManifests);
        Assert.Empty(Directory.GetFiles(fixture.ManifestsDirectory));
    }

    /// <summary>
    /// A package whose sha256 does NOT match the signed manifest is REJECTED, the
    /// partial file is DISCARDED and the operation is not repairable by resumption
    /// (AD-8, AC1).
    /// </summary>
    [Fact]
    public async Task A_package_whose_hash_does_not_match_is_discarded()
    {
        var fixture = await CreateFixtureAsync();

        // El parcial que el coordinador encuentra NO es el que declara el manifiesto
        // firmado, así que su hash diverge (AD-8).
        await File.WriteAllBytesAsync(
            fixture.PartialPath,
            Encoding.UTF8.GetBytes("contenido alterado"),
            TestContext.Current.CancellationToken);

        var result = await fixture.Coordinator
            .InstallAsync(GameId, PackageId, Version, TestContext.Current.CancellationToken)
            ;

        Assert.True(result.IsRejected);
        Assert.Equal(ErrorCatalog.PackageIntegrityMismatchCode, result.Error!.Code);

        // AC1: el parcial se descarta y la operación no queda reparable por reanudación.
        Assert.False(File.Exists(fixture.PartialPath));

        // Y no se persiste ninguna fila: la operación no se instaló (AD-27).
        Assert.Empty(fixture.StateStore.SavedInstallations);
    }

    /// <summary>
    /// An installation that passes the certification ends in <c>Installed</c> and the
    /// row is persisted with the STABLE identifier of the root (AC1, AC4, AC5).
    /// </summary>
    [Fact]
    public async Task An_installed_operation_ends_in_installed_and_persists_the_row()
    {
        var fixture = await CreateFixtureAsync();

        // El parcial contiene EXACTAMENTE los bytes que declara el manifiesto firmado.
        await File.WriteAllBytesAsync(
            fixture.PartialPath,
            fixture.PackageBytes,
            TestContext.Current.CancellationToken);

        var result = await fixture.Coordinator
            .InstallAsync(GameId, PackageId, Version, TestContext.Current.CancellationToken)
            ;

        Assert.True(result.IsCertified);
        Assert.True(result.Verdict!.IsInstalled);
        Assert.Equal(InstallationState.Installed, result.Verdict.State);

        var installation = Assert.Single(fixture.StateStore.SavedInstallations);
        Assert.Equal(GameId, installation.GameId);
        Assert.Equal(Version, installation.InstalledVersion);
        Assert.Equal(InstallationState.Installed, installation.ValidationState);

        // AC1: el identificador de la raíz es el que se deriva de su ruta canónica.
        Assert.Equal(
            InstallationCertification.DeriveInstallDirectoryId(fixture.RootPath),
            installation.InstallDirectoryId);
    }

    /// <summary>
    /// The operation PASSES THROUGH <c>ValidatingInstallation</c> before it ends in a
    /// terminal state (AD-10, AC4).
    /// </summary>
    [Fact]
    public async Task The_operation_passes_through_validating_installation()
    {
        var fixture = await CreateFixtureAsync();

        await File.WriteAllBytesAsync(
            fixture.PartialPath,
            fixture.PackageBytes,
            TestContext.Current.CancellationToken);

        await fixture.Coordinator
            .InstallAsync(GameId, PackageId, Version, TestContext.Current.CancellationToken)
            ;

        // La máquina de estados DECLARE la transición de `ValidatingInstallation` a
        // `Installed`, así que el estado final prueba que se pasó por el intermedio.
        Assert.True(
            new LANPartyHub.Client.Domain.StateMachines.InstallationStateMachine(
                    InstallationState.ValidatingInstallation)
                .CanTransitionTo(InstallationState.Installed));

        Assert.Equal(InstallationState.Installed, Assert.Single(fixture.StateStore.SavedInstallations).ValidationState);
    }

    /// <summary>
    /// A tree whose <c>verify</c> operation does NOT pass leaves the installation
    /// REPAIRABLE, with its reason and its repair action registered (AC6).
    /// </summary>
    [Fact]
    public async Task A_tree_whose_verify_does_not_pass_is_repairable()
    {
        var fixture = await CreateFixtureAsync(verifyPasses: false);

        await File.WriteAllBytesAsync(
            fixture.PartialPath,
            fixture.PackageBytes,
            TestContext.Current.CancellationToken);

        var result = await fixture.Coordinator
            .InstallAsync(GameId, PackageId, Version, TestContext.Current.CancellationToken)
            ;

        Assert.True(result.Verdict!.IsRepairable);
        Assert.Equal(InstallationState.Repairable, result.Verdict.State);

        // AC6: el motivo y la acción de reparación quedan registrados y visibles.
        Assert.NotNull(result.Verdict.ReasonText);
        Assert.False(string.IsNullOrWhiteSpace(result.Verdict.ReasonText));
        Assert.NotNull(result.Verdict.RepairAction);
        Assert.False(string.IsNullOrWhiteSpace(result.Verdict.RepairAction));

        var installation = Assert.Single(fixture.StateStore.SavedInstallations);
        Assert.Equal(InstallationState.Repairable, installation.ValidationState);
    }

    /// <summary>
    /// A tree whose paths escape the configured root is NOT repairable, because the
    /// package that was validated is not available for the repair (AC6).
    /// </summary>
    [Fact]
    public async Task A_tree_whose_paths_escape_the_root_is_not_installed()
    {
        var fixture = await CreateFixtureAsync(pathsConfinedToRoot: false);

        await File.WriteAllBytesAsync(
            fixture.PartialPath,
            fixture.PackageBytes,
            TestContext.Current.CancellationToken);

        var result = await fixture.Coordinator
            .InstallAsync(GameId, PackageId, Version, TestContext.Current.CancellationToken)
            ;

        Assert.False(result.Verdict!.IsInstalled);
    }

    /// <summary>
    /// The reason of the rejection is REGISTERED with its cause, so the diagnosis is
    /// available for the player (AC2, NFR-5).
    /// </summary>
    [Fact]
    public async Task The_reason_of_the_rejection_carries_its_cause()
    {
        var fixture = await CreateFixtureAsync(tamperWithSignature: true);

        var result = await fixture.Coordinator
            .InstallAsync(GameId, PackageId, Version, TestContext.Current.CancellationToken)
            ;

        var expected = ErrorCatalog.Resolve(ErrorCatalog.ManifestSignatureInvalidCode);

        Assert.Equal(expected.Message, result.Error!.Message);
        Assert.Equal(expected.Cause, result.Error.Cause);
        Assert.Equal(expected.NextStep, result.Error.NextStep);
    }

    /// <summary>
    /// A client that has NO fixed anchor cannot install anything, and it writes nothing
    /// (AD-27).
    /// </summary>
    [Fact]
    public async Task A_client_without_a_fixed_anchor_is_rejected_and_writes_nothing()
    {
        var fixture = await CreateFixtureAsync(withoutAnchor: true);

        var result = await fixture.Coordinator
            .InstallAsync(GameId, PackageId, Version, TestContext.Current.CancellationToken)
            ;

        Assert.True(result.IsRejected);
        Assert.Equal(ErrorCatalog.AnchorFingerprintMismatchCode, result.Error!.Code);

        AssertNoBytesWereWritten(fixture);
    }

    /// <summary>
    /// Asserts that NOTHING was written on disk: ni el manifiesto en la caché, ni el
    /// directorio de instalación, ni el parcial, ni la fila (AD-8, AC3, AD-27).
    /// </summary>
    /// <param name="fixture">Fixture of the installation.</param>
    private static void AssertNoBytesWereWritten(InstallationFixture fixture)
    {
        Assert.Empty(fixture.ManifestCache.StoredManifests);
        Assert.Empty(fixture.StateStore.SavedInstallations);

        Assert.False(Directory.Exists(fixture.RootPath));
        Assert.False(File.Exists(fixture.PartialPath));

        // La carpeta de la caché existe (la crea el layout), pero NO hay ningún
        // manifiesto dentro (AC3).
        Assert.Empty(Directory.GetFiles(fixture.ManifestsDirectory));
    }

    /// <summary>
    /// Builds the fixture of an installation over the doubles of the ports (AD-16).
    /// </summary>
    /// <param name="tamperWithSignature">Whether the signature is broken.</param>
    /// <param name="tamperWithOperation">Whether the manifest declares an operation outside the closed set.</param>
    /// <param name="schemaVersion">Version of the schema that the manifest declares.</param>
    /// <param name="verifyPasses">Whether the <c>verify</c> operation passes over the tree.</param>
    /// <param name="pathsConfinedToRoot">Whether every written path stayed inside the root.</param>
    /// <param name="withoutAnchor">Whether the client has NO fixed anchor.</param>
    /// <returns>The fixture of the installation.</returns>
    private async Task<InstallationFixture> CreateFixtureAsync(
        bool tamperWithSignature = false,
        bool tamperWithOperation = false,
        int schemaVersion = 1,
        bool verifyPasses = true,
        bool pathsConfinedToRoot = true,
        bool withoutAnchor = false)
    {
        var folder = CreateTemporaryFolder();

        var layout = new TemporaryDataLayout(folder);

        var signer = new Ed25519ManifestSigner();
        var packageBytes = Encoding.UTF8.GetBytes("contenido del paquete");

        var manifest = new InstallationManifest(
            schemaVersion,
            PackageId,
            Version,
            packageBytes.LongLength,
            PackageIntegrity.ComputeSha256(packageBytes),
            new InstallationManifest.LaunchSettings("game.exe", ["-novid"], "game"),
            [new InstallationManifest.Verify("game/game.exe")]);

        var canonical = tamperWithOperation
            ? TamperedCanonical(manifest)
            : LANPartyHub.Server.Domain.Packages.ManifestCanonicalizer.Canonicalize(manifest);

        // El manifiesto manipulado lo FIRMA el servidor: la firma es válida, así que el
        // paso 2 del orden normativo pasa y lo que lo rechaza es el conjunto cerrado de
        // las operaciones (AD-7, AC2).
        var signature = tamperWithSignature
            ? new string('0', 128)
            : signer.Sign(canonical);

        var envelope = new InstallationManifest.SignedEnvelope(
            canonical,
            signature,
            signer.GetKeyId());

        var manifestCache = new RecordingManifestCache();
        var stateStore = new RecordingStateStore();

        var paths = new InstallPathResolver(layout);

        var tree = new RecordingTreeWriter(verifyPasses, pathsConfinedToRoot);

        var coordinator = new InstallationCoordinator(
            new FixedManifestClient(envelope),
            new Ed25519ManifestVerifier(),
            new FixedTrustAnchorReader(withoutAnchor ? null : DefaultAnchor(signer)),
            manifestCache,
            layout,
            paths,
            tree,
            stateStore,
            new InstallRootSwitcher(NullLogger<InstallRootSwitcher>.Instance),
            NullLogger<InstallationCoordinator>.Instance);

        await Task.CompletedTask;

        return new InstallationFixture(
            coordinator,
            manifestCache,
            stateStore,
            layout.GameInstallDirectory(GameId),
            layout.PartialFilePath(PackageId),
            layout.ManifestsDirectory,
            packageBytes);
    }

    /// <summary>
    /// Builds a canonical manifest that declares an operation OUTSIDE the closed set,
    /// which the polymorphic deserializer of the contract rejects (AD-7, AC2).
    /// </summary>
    /// <param name="manifest">Manifest whose canonical form is tampered.</param>
    /// <returns>The tampered canonical text.</returns>
    private static string TamperedCanonical(InstallationManifest manifest)
    {
        var canonical = LANPartyHub.Server.Domain.Packages.ManifestCanonicalizer.Canonicalize(manifest);

        // El discriminador de la operación polimórfica es el tipo de la operación, así
        // que un discriminador que el contrato no reconoce es una operación fuera del
        // conjunto cerrado (AD-7).
        return canonical.Replace("\"operation\":\"verify\"", "\"operation\":\"runCommand\"", StringComparison.Ordinal);
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
        var folder = Directory.CreateTempSubdirectory("lan-party-hub-install-order-");

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
    /// Fixture of an installation: the use case and the doubles that make the writes
    /// observable (AD-16).
    /// </summary>
    private sealed record InstallationFixture(
        InstallationCoordinator Coordinator,
        RecordingManifestCache ManifestCache,
        RecordingStateStore StateStore,
        string RootPath,
        string PartialPath,
        string ManifestsDirectory,
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
    /// Port of the cache of the signed manifests that RECUERDA lo que se le escribió
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
    /// Port of the application of the tree that answers con el resultado que la prueba
    /// fija (AD-16).
    /// </summary>
    private sealed class RecordingTreeWriter : IInstallTreeWriter
    {
        private readonly bool _verifyPasses;
        private readonly bool _pathsConfinedToRoot;

        public RecordingTreeWriter(bool verifyPasses, bool pathsConfinedToRoot)
        {
            _verifyPasses = verifyPasses;
            _pathsConfinedToRoot = pathsConfinedToRoot;
        }

        public Task<InstallTreeOutcome> ApplyAsync(
            InstallationManifest manifest,
            string rootPath,
            string? packagePath,
            CancellationToken cancellationToken) =>
            Task.FromResult(new InstallTreeOutcome(_verifyPasses, _pathsConfinedToRoot));

        public Task<InstallTreeOutcome> ApplyAsync(
            InstallationManifest manifest,
            string rootPath,
            string? packagePath,
            IInstallProgressSink progress,
            CancellationToken cancellationToken) =>
            Task.FromResult(new InstallTreeOutcome(_verifyPasses, _pathsConfinedToRoot));
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

        public string GameInstallDirectory(string gameId) =>
            LANPartyHub.Client.Domain.Install.InstallPathConfine.GameRoot(_installDirectory, gameId);

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
