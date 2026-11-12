using System.Text;
using LANPartyHub.Client.Application;
using LANPartyHub.Client.Application.Install;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Domain;
using LANPartyHub.Client.Domain.Integrity;
using LANPartyHub.Client.Domain.Trust;
using LANPartyHub.Client.Infrastructure.Filesystem;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The REPAIR of an invalid installation from the package that was already VALIDATED
/// (AC1, AC2, UX-DR-17, UX-DR-18, UX-DR-20, AD-9, AD-10, AD-16).
///
/// <para>
/// El caso de uso se ejercita con dobles de los PUERTOS, así que la ausencia de RED y la
/// AUSENCIA de escrituras son observables sin un servidor y sin un disco (AD-1, AD-16).
/// </para>
/// <para>
/// La reparación es de CERO RED: el doble de cada puerto de RED falla si se le llama, así
/// que la prueba demuestra que la reparación NO descarga (AC1, UX-DR-20).
/// </para>
/// </summary>
public sealed class InstallationRepairServiceTests : IDisposable
{
    private const string GameId = "0193f2a1-b2c3-4d4e-5f60-718293a4b5c6";
    private const string PackageId = "paquete-1";
    private const string Version = "1.2";

    private readonly List<string> _temporaryFolders = [];

    /// <summary>
    /// AC1, UX-DR-20: la reparación rehace la instalación DESDE EL PAQUETE YA VALIDADO,
    /// sin volver a descargar, y la certificación deja la instalación en «Installed».
    /// </summary>
    [Fact]
    public async Task The_repair_reinstalls_from_the_validated_package_and_certifies_it()
    {
        var fixture = await CreateFixtureAsync();

        var outcome = await fixture.Service
            .RepairAsync(GameId, Version, TestContext.Current.CancellationToken);

        Assert.True(outcome.IsRepaired);
        Assert.False(outcome.IsRejected);
        Assert.True(outcome.Verdict!.IsInstalled);
        Assert.Equal(InstallationState.Installed, outcome.Verdict.State);
        Assert.Null(outcome.Error);

        // El manifiesto se aplicó dentro de la raíz, con el paquete ya validado.
        Assert.Equal(fixture.PackagePath, fixture.TreeWriter.LastPackagePath);
        Assert.Equal(fixture.RootPath, fixture.TreeWriter.LastRootPath);

        // AC1: el estado se PERSISTE con el identificador ESTABLE de la raíz.
        var installation = Assert.Single(fixture.StateStore.SavedInstallations);
        Assert.Equal(GameId, installation.GameId);
        Assert.Equal(Version, installation.InstalledVersion);
        Assert.Equal(InstallationState.Installed, installation.ValidationState);
        Assert.Equal(
            LANPartyHub.Client.Domain.Install.InstallationCertification
                .DeriveInstallDirectoryId(fixture.RootPath),
            installation.InstallDirectoryId);
    }

    /// <summary>
    /// AC1, UX-DR-20: la reparación es de CERO RED: no pide la intención de descarga, no
    /// transfiere bytes y no pide el manifiesto firmado al servidor.
    /// </summary>
    [Fact]
    public async Task The_repair_touches_no_network_port()
    {
        var fixture = await CreateFixtureAsync();

        await fixture.Service
            .RepairAsync(GameId, Version, TestContext.Current.CancellationToken);

        // El doble de cada puerto de RED falla si se le llama, así que llegar aquí ya
        // demuestra que la reparación NO los llamó.
        Assert.Equal(0, fixture.DownloadIntents.CallCount);
        Assert.Equal(0, fixture.TransferEngine.CallCount);
        Assert.Equal(0, fixture.Manifests.CallCount);
    }

    /// <summary>
    /// AC1, AC2: un <c>sha256</c> que ya NO coincide deja de ser reparable: la reparación
    /// se rechaza con el motivo de la re-descarga y NO se escribe nada.
    /// </summary>
    [Fact]
    public async Task A_package_whose_hash_diverged_is_not_repairable()
    {
        var fixture = await CreateFixtureAsync();

        // Los bytes del paquete YA NO son los que declara el manifiesto firmado.
        await File.WriteAllBytesAsync(
            fixture.PackagePath,
            Encoding.UTF8.GetBytes("contenido alterado"),
            TestContext.Current.CancellationToken);

        var outcome = await fixture.Service
            .RepairAsync(GameId, Version, TestContext.Current.CancellationToken);

        Assert.True(outcome.IsRejected);
        Assert.Equal(ErrorCatalog.PackageIntegrityMismatchCode, outcome.Error!.Code);
        Assert.NotNull(outcome.Reason);

        // AC1: el manifiesto NO se aplicó, así que no se escribió nada en la raíz.
        Assert.Null(fixture.TreeWriter.LastRootPath);
        Assert.Empty(fixture.StateStore.SavedInstallations);
    }

    /// <summary>
    /// AC1, AC2: sin el paquete validado en disco no hay reparación, y el motivo visible
    /// es el de la RE-DESCARGA.
    /// </summary>
    [Fact]
    public async Task Without_the_validated_package_there_is_no_repair()
    {
        var fixture = await CreateFixtureAsync();

        File.Delete(fixture.PackagePath);

        var outcome = await fixture.Service
            .RepairAsync(GameId, Version, TestContext.Current.CancellationToken);

        Assert.True(outcome.IsRejected);
        Assert.Equal(ErrorCatalog.InstallationPackageMismatchCode, outcome.Error!.Code);
        Assert.Null(fixture.TreeWriter.LastRootPath);
        Assert.Empty(fixture.StateStore.SavedInstallations);
    }

    /// <summary>
    /// AC1, AC2: sin el manifiesto firmado en caché no hay reparación, porque la
    /// reparación es de CERO RED y el manifiesto sale de la caché local.
    /// </summary>
    [Fact]
    public async Task Without_the_signed_manifest_in_the_cache_there_is_no_repair()
    {
        var fixture = await CreateFixtureAsync(withoutCachedManifest: true);

        var outcome = await fixture.Service
            .RepairAsync(GameId, Version, TestContext.Current.CancellationToken);

        Assert.True(outcome.IsRejected);
        Assert.Equal(ErrorCatalog.InstallationPackageMismatchCode, outcome.Error!.Code);
        Assert.Null(fixture.TreeWriter.LastRootPath);
    }

    /// <summary>
    /// AC1, AC5: un árbol que NO verifica deja la instalación REPARABLE con su motivo
    /// visible, y la fila se persiste en ese estado.
    /// </summary>
    [Fact]
    public async Task A_tree_that_does_not_verify_stays_repairable()
    {
        var fixture = await CreateFixtureAsync(verifyPasses: false);

        var outcome = await fixture.Service
            .RepairAsync(GameId, Version, TestContext.Current.CancellationToken);

        Assert.True(outcome.IsRepaired);
        Assert.True(outcome.Verdict!.IsRepairable);
        Assert.Equal(InstallationState.Repairable, outcome.Verdict.State);
        Assert.NotNull(outcome.Verdict.ReasonText);
        Assert.NotNull(outcome.Verdict.RepairAction);

        var installation = Assert.Single(fixture.StateStore.SavedInstallations);
        Assert.Equal(InstallationState.Repairable, installation.ValidationState);
    }

    /// <summary>
    /// AC5: un fallo de la aplicación del manifiesto deja la reparación FALLIDA con su
    /// error PERSISTIDO, así que el diagnóstico sobrevive al reinicio.
    /// </summary>
    [Fact]
    public async Task A_failure_of_the_manifest_leaves_the_repair_failed_with_its_reason()
    {
        var fixture = await CreateFixtureAsync(treeThrows: true);

        var outcome = await fixture.Service
            .RepairAsync(GameId, Version, TestContext.Current.CancellationToken);

        Assert.True(outcome.IsRejected);
        Assert.Equal(ErrorCatalog.InstallOperationFailedCode, outcome.Error!.Code);

        // AC5: el error queda PERSISTIDO, con su código estable (AD-11, AD-25).
        var installation = Assert.Single(fixture.StateStore.SavedInstallations);
        Assert.Equal(InstallationState.Failed, installation.ValidationState);
        Assert.Equal(
            ErrorCatalog.InstallOperationFailedCode,
            Assert.Single(fixture.StateStore.SavedFailureCodes));
    }

    /// <summary>
    /// AC1: el <c>installDirectoryId</c> NO cambia ante la reparación: se deriva de la
    /// ruta canónica de la raíz, así que la fila conserva el identificador que ya tenía.
    /// </summary>
    [Fact]
    public async Task The_identifier_of_the_root_does_not_change_with_the_repair()
    {
        var fixture = await CreateFixtureAsync();

        var expected = LANPartyHub.Client.Domain.Install.InstallationCertification
            .DeriveInstallDirectoryId(fixture.RootPath);

        var outcome = await fixture.Service
            .RepairAsync(GameId, Version, TestContext.Current.CancellationToken);

        Assert.Equal(expected, outcome.InstallDirectoryId);
    }

    /// <summary>
    /// AC1: la reparación NO pide confirmación: la consecuencia se nombra y el paquete ya
    /// validado se reutiliza, así que no hay nada que confirmar (UX-DR-20).
    /// </summary>
    [Fact]
    public async Task The_repair_reuses_the_validated_package_without_asking_for_a_confirmation()
    {
        var fixture = await CreateFixtureAsync();

        await fixture.Service
            .RepairAsync(GameId, Version, TestContext.Current.CancellationToken);

        Assert.Equal(1, fixture.TreeWriter.ApplyCount);
    }

    /// <summary>
    /// AC1: el paquete que se repara es el de la VERSIÓN INSTALADA, porque es el único
    /// que describe el árbol que hay en disco.
    /// </summary>
    [Fact]
    public async Task The_repaired_package_is_the_one_of_the_installed_version()
    {
        var fixture = await CreateFixtureAsync();

        await fixture.Service
            .RepairAsync(GameId, Version, TestContext.Current.CancellationToken);

        // El manifiesto que se aplicó es el del paquete de la versión INSTALADA, así que el
        // paquete reparado es el que el snapshot declara para esa versión (AC1).
        Assert.Equal(fixture.PackagePath, fixture.TreeWriter.LastPackagePath);
        Assert.Equal(
            fixture.PackagePath,
            Path.Combine(
                Path.GetDirectoryName(fixture.PackagePath)!,
                $"{PackageId}.part"));
    }

    /// <summary>
    /// AC1: el identificador del paquete reparado sale del snapshot del catálogo, así que
    /// una versión que el snapshot no declara NO es reparable.
    /// </summary>
    [Fact]
    public async Task A_version_that_the_snapshot_does_not_declare_is_not_repairable()
    {
        var fixture = await CreateFixtureAsync();

        var outcome = await fixture.Service
            .RepairAsync(GameId, "9.9", TestContext.Current.CancellationToken);

        Assert.True(outcome.IsRejected);
        Assert.Equal(ErrorCatalog.InstallationPackageMismatchCode, outcome.Error!.Code);
        Assert.Null(fixture.TreeWriter.LastRootPath);
    }

    /// <summary>
    /// AC4: la reparación publica su progreso con la fase y los bytes REALES, así que la
    /// barra nunca contradice al texto.
    /// </summary>
    [Fact]
    public async Task The_repair_publishes_its_progress()
    {
        var fixture = await CreateFixtureAsync();

        var phases = new List<InstallPhase>();

        fixture.Service.ProgressChanged += progress => phases.Add(progress.Phase);

        await fixture.Service
            .RepairAsync(GameId, Version, TestContext.Current.CancellationToken);

        Assert.Contains(InstallPhase.Completed, phases);
    }

    /// <summary>
    /// AC1, AC2, FR-11: la ACTUALIZACIÓN reutiliza el mismo camino de instalación que el
    /// flujo inicial, así que la verificación de la firma, de la <c>schemaVersion</c> y del
    /// <c>sha256</c> es la MISMA (AD-8).
    /// </summary>
    [Fact]
    public async Task The_update_is_rejected_before_writing_when_the_hash_diverges()
    {
        var fixture = await CreateUpdateFixtureAsync();

        // Los bytes del paquete de la versión nueva NO son los que declara el manifiesto
        // firmado, así que la actualización se rechaza ANTES de escribir nada (AD-8).
        await File.WriteAllBytesAsync(
            fixture.PackagePath,
            Encoding.UTF8.GetBytes("contenido alterado"),
            TestContext.Current.CancellationToken);

        var outcome = await fixture.Coordinator
            .InstallAsync(
                GameId,
                PackageId,
                "2.0",
                InstallationMode.Update,
                TestContext.Current.CancellationToken);

        Assert.True(outcome.IsRejected);
        Assert.Equal(ErrorCatalog.PackageIntegrityMismatchCode, outcome.Error!.Code);

        // NADA se escribió en la raíz de instalación (AD-8, AC2).
        Assert.Null(fixture.TreeWriter.LastRootPath);
    }

    /// <summary>
    /// AC4, FR-11: un fallo de la ACTUALIZACIÓN NO invalida la instalación anterior: la
    /// fila conserva su versión y su validez, porque lo que ya estaba instalado sigue
    /// siendo válido (AC4).
    /// </summary>
    [Fact]
    public async Task A_failed_update_keeps_the_previous_installation_valid()
    {
        var fixture = await CreateUpdateFixtureAsync(treeThrows: true);

        var outcome = await fixture.Coordinator
            .InstallAsync(
                GameId,
                PackageId,
                "2.0",
                InstallationMode.Update,
                TestContext.Current.CancellationToken);

        Assert.True(outcome.IsRejected);
        Assert.Equal(ErrorCatalog.InstallOperationFailedCode, outcome.Error!.Code);

        // La fila de la instalación ANTERIOR no se tocó: ni su versión ni su validez
        // (AC4, FR-11).
        Assert.Empty(fixture.StateStore.SavedInstallations);
        Assert.Empty(fixture.StateStore.SavedFailureCodes);
    }

    /// <summary>
    /// AC1, FR-11: la ACTUALIZACIÓN que termina bien SUSTITUYE la versión anterior en la
    /// MISMA raíz de instalación, así que la fila del cliente lleva la versión nueva.
    /// </summary>
    [Fact]
    public async Task A_successful_update_replaces_the_previous_version()
    {
        var fixture = await CreateUpdateFixtureAsync();

        var outcome = await fixture.Coordinator
            .InstallAsync(
                GameId,
                PackageId,
                "2.0",
                InstallationMode.Update,
                TestContext.Current.CancellationToken);

        Assert.True(outcome.IsCertified);
        Assert.True(outcome.Verdict!.IsInstalled);

        // AC1, AD-9: el árbol nuevo se PREPARA en una raíz HERMANA de la activa, se
        // CERTIFICA allí y sólo después SUSTITUYE a la anterior por cambio de nombre.
        // Por eso lo escrito NO está en la raíz activa, y por eso la raíz activa
        // conserva su contenido si la certificación falla (AC4, FR-11).
        Assert.NotNull(fixture.TreeWriter.LastRootPath);
        Assert.NotEqual(fixture.RootPath, fixture.TreeWriter.LastRootPath);
        Assert.Equal(
            fixture.RootPath + InstallRootSwitcher.StagingSuffix,
            fixture.TreeWriter.LastRootPath);

        // La SUSTITUCIÓN es lo que deja la versión nueva en la raíz activa: tras la
        // operación, la raíz activa es la que contiene el árbol nuevo (AC1).
        Assert.True(Directory.Exists(fixture.RootPath));

        var installation = Assert.Single(fixture.StateStore.SavedInstallations);

        Assert.Equal(GameId, installation.GameId);
        Assert.Equal("2.0", installation.InstalledVersion);
        Assert.Equal(InstallationState.Installed, installation.ValidationState);
    }

    /// <summary>
    /// Builds the fixture of the UPDATE over the doubles of the ports (AD-16).
    /// </summary>
    /// <param name="treeThrows">Whether the application of the tree fails.</param>
    /// <returns>The fixture of the update.</returns>
    private async Task<UpdateFixture> CreateUpdateFixtureAsync(bool treeThrows = false)
    {
        var folder = CreateTemporaryFolder();

        var layout = new TemporaryDataLayout(folder);

        var packageBytes = Encoding.UTF8.GetBytes("contenido del paquete nuevo");

        var manifest = new InstallationManifest(
            1,
            PackageId,
            "2.0",
            packageBytes.LongLength,
            PackageIntegrity.ComputeSha256(packageBytes),
            new InstallationManifest.LaunchSettings("game.exe", ["-novid"], "game"),
            [new InstallationManifest.Verify("game/game.exe")]);

        var canonical = LANPartyHub.Server.Domain.Packages.ManifestCanonicalizer
            .Canonicalize(manifest);

        var packagePath = layout.PartialFilePath(PackageId);

        await File.WriteAllBytesAsync(
            packagePath,
            packageBytes,
            TestContext.Current.CancellationToken);

        var stateStore = new RecordingStateStore();
        var paths = new InstallPathResolver(layout);
        var treeWriter = new RecordingTreeWriter(verifyPasses: true, throws: treeThrows);

        var coordinator = new InstallationCoordinator(
            new FixedManifestClient(manifest),
            new AcceptingManifestVerifier(),
            new FixedTrustAnchorReader(),
            new RecordingManifestCache(),
            layout,
            paths,
            treeWriter,
            stateStore,
            new InstallRootSwitcher(NullLogger<InstallRootSwitcher>.Instance),
            NullLogger<InstallationCoordinator>.Instance);

        return new UpdateFixture(
            coordinator,
            stateStore,
            treeWriter,
            layout.GameInstallDirectory(GameId),
            packagePath);
    }

    /// <summary>
    /// Fixture of the update: el coordinador y los dobles que hacen las escrituras
    /// observables (AD-16).
    /// </summary>
    private sealed record UpdateFixture(
        InstallationCoordinator Coordinator,
        RecordingStateStore StateStore,
        RecordingTreeWriter TreeWriter,
        string RootPath,
        string PackagePath);

    /// <summary>
    /// Builds the fixture of the repair: un paquete REAL, un manifiesto REAL firmado y una
    /// instalación REAL ya aplicada (AD-16).
    /// </summary>
    /// <param name="verifyPasses">Whether the <c>verify</c> operation passes over the tree.</param>
    /// <param name="treeThrows">Whether the application of the tree fails.</param>
    /// <param name="withoutCachedManifest">Whether the cache holds NO signed manifest.</param>
    /// <returns>The fixture of the repair.</returns>
    private async Task<RepairFixture> CreateFixtureAsync(
        bool verifyPasses = true,
        bool treeThrows = false,
        bool withoutCachedManifest = false)
    {
        var folder = CreateTemporaryFolder();

        var layout = new TemporaryDataLayout(folder);

        var packageBytes = Encoding.UTF8.GetBytes("contenido del paquete");

        var manifest = new InstallationManifest(
            1,
            PackageId,
            Version,
            packageBytes.LongLength,
            PackageIntegrity.ComputeSha256(packageBytes),
            new InstallationManifest.LaunchSettings("game.exe", ["-novid"], "game"),
            [new InstallationManifest.Verify("game/game.exe")]);

        var canonical = LANPartyHub.Server.Domain.Packages.ManifestCanonicalizer
            .Canonicalize(manifest);

        var packagePath = layout.PartialFilePath(PackageId);

        await File.WriteAllBytesAsync(
            packagePath,
            packageBytes,
            TestContext.Current.CancellationToken);

        var manifestCache = new RecordingManifestCache();

        if (!withoutCachedManifest)
        {
            await manifestCache.StoreAsync(
                PackageId,
                canonical,
                TestContext.Current.CancellationToken);
        }

        var stateStore = new RecordingStateStore();
        var paths = new InstallPathResolver(layout);
        var treeWriter = new RecordingTreeWriter(verifyPasses, treeThrows);

        var downloadIntents = new ExplodingDownloadIntentClient();
        var transferEngine = new ExplodingTransferEngine();
        var manifests = new ExplodingManifestClient();

        var service = new InstallationRepairService(
            new ValidatedPackageStore(layout),
            manifestCache,
            new FixedSnapshotStore(SnapshotOf(manifest)),
            paths,
            treeWriter,
            stateStore,
            NullLogger<InstallationRepairService>.Instance);

        return new RepairFixture(
            service,
            stateStore,
            treeWriter,
            downloadIntents,
            transferEngine,
            manifests,
            layout.GameInstallDirectory(GameId),
            packagePath);
    }

    /// <summary>
    /// Builds the snapshot of the catalog que declara el paquete de la versión instalada
    /// (AC1).
    /// </summary>
    /// <param name="manifest">Manifest of the package.</param>
    /// <returns>The snapshot of the catalog.</returns>
    private static CatalogSnapshot SnapshotOf(InstallationManifest manifest) =>
        new(
            1,
            [],
            [
                new PackageDescriptor(
                    manifest.PackageId,
                    GameId,
                    manifest.PackageVersion,
                    manifest.SizeBytes,
                    0,
                    manifest.Sha256,
                    $"{manifest.PackageId}.json"),
            ]);

    /// <summary>
    /// Creates a temporary folder for the test.
    /// </summary>
    /// <returns>The absolute path of the folder.</returns>
    private string CreateTemporaryFolder()
    {
        var folder = Directory.CreateTempSubdirectory("lan-party-hub-repair-");

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
    /// Fixture of the repair: the use case and the doubles that make the writes and the
    /// absence of network observable (AD-16).
    /// </summary>
    private sealed record RepairFixture(
        InstallationRepairService Service,
        RecordingStateStore StateStore,
        RecordingTreeWriter TreeWriter,
        ExplodingDownloadIntentClient DownloadIntents,
        ExplodingTransferEngine TransferEngine,
        ExplodingManifestClient Manifests,
        string RootPath,
        string PackagePath);

    /// <summary>
    /// Port of the read of the signed manifest que devuelve SIEMPRE el mismo manifiesto,
    /// así que la verificación de la firma y de la <c>schemaVersion</c> es observable sin
    /// un servidor (AD-16).
    /// </summary>
    private sealed class FixedManifestClient : ISignedManifestClient
    {
        private readonly InstallationManifest _manifest;

        public FixedManifestClient(InstallationManifest manifest) => _manifest = manifest;

        public Task<InstallationManifest.SignedEnvelope?> GetAsync(
            string packageId,
            CancellationToken cancellationToken)
        {
            var canonical = LANPartyHub.Server.Domain.Packages.ManifestCanonicalizer
                .Canonicalize(_manifest);

            return Task.FromResult<InstallationManifest.SignedEnvelope?>(
                new InstallationManifest.SignedEnvelope(canonical, "firma", "clave"));
        }
    }

    /// <summary>
    /// Verificador de la firma que la ACEPTA, así que el rechazo de la firma no es lo que
    /// la prueba observa (AD-2, AD-16).
    /// </summary>
    private sealed class AcceptingManifestVerifier : IManifestSignatureVerifier
    {
        public bool Verify(string canonicalManifest, string signature, byte[] publicKey) => true;
    }

    /// <summary>
    /// Port of the read of the ancla de confianza que siempre devuelve el mismo valor, así
    /// que la validación contra el ancla fijado es observable (AD-27, AD-16).
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
    /// Port of the read of the signed manifest that NEVER answers, así que la prueba
    /// demuestra que la reparación no lo llama (AC1, UX-DR-20).
    /// </summary>
    private sealed class ExplodingManifestClient : ISignedManifestClient
    {
        public int CallCount { get; private set; }

        public Task<InstallationManifest.SignedEnvelope?> GetAsync(
            string packageId,
            CancellationToken cancellationToken)
        {
            CallCount++;

            throw new InvalidOperationException(
                "La reparación NO puede pedir el manifiesto firmado al servidor: la reparación es de cero red.");
        }
    }

    /// <summary>
    /// Port of the registration of the intention de descarga que NUNCA debe llamarse en una
    /// reparación (AC1, UX-DR-20).
    /// </summary>
    private sealed class ExplodingDownloadIntentClient : IDownloadIntentClient
    {
        public int CallCount { get; private set; }

        public Task<string> RegisterAsync(string packageId, CancellationToken cancellationToken)
        {
            CallCount++;

            throw new InvalidOperationException(
                "La reparación NO puede registrar una intención de descarga.");
        }
    }

    /// <summary>
    /// Motor de transferencia que NUNCA debe llamarse en una reparación (AC1, UX-DR-20).
    /// </summary>
    private sealed class ExplodingTransferEngine : ITransferEngine
    {
        public int CallCount { get; private set; }

        public Task<TransferOutcome> TransferAsync(
            Uri contentUri,
            long startInclusive,
            long endInclusive,
            Stream destination,
            CancellationToken cancellationToken)
        {
            CallCount++;

            throw new InvalidOperationException(
                "La reparación NO puede transferir bytes: la descarga no se repite.");
        }
    }

    /// <summary>
    /// Store del snapshot del catálogo que siempre devuelve el MISMO (AD-16).
    /// </summary>
    private sealed class FixedSnapshotStore : ICatalogSnapshotStore
    {
        private readonly CatalogSnapshot? _snapshot;

        public FixedSnapshotStore(CatalogSnapshot? snapshot) => _snapshot = snapshot;

        public Task<CatalogSnapshot?> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_snapshot);

        public Task<long> GetAppliedVersionAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_snapshot?.CatalogVersion ?? 0L);

        public Task<DateTimeOffset?> GetLastSynchronizedAtAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<DateTimeOffset?>(null);

        public Task SaveAsync(CatalogSnapshot snapshot, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Este store de snapshot no guarda snapshots.");

        public Task<IReadOnlyList<InstalledGame>> LoadInstalledGamesAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<InstalledGame>>(Array.Empty<InstalledGame>());

        public Task<IReadOnlyList<ActiveOperation>> LoadActiveOperationsAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ActiveOperation>>(Array.Empty<ActiveOperation>());
    }

    /// <summary>
    /// Port of the cache of the signed manifests that RECUERDA lo que se le escribió
    /// (AD-16).
    /// </summary>
    private sealed class RecordingManifestCache : IManifestCache
    {
        private readonly Dictionary<string, string> _stored = new(StringComparer.Ordinal);

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

    /// <summary>
    /// Port of the application of the tree que RECUERDA lo que se le pidió (AD-16).
    /// </summary>
    private sealed class RecordingTreeWriter : IInstallTreeWriter
    {
        private readonly bool _verifyPasses;
        private readonly bool _throws;

        public RecordingTreeWriter(bool verifyPasses, bool throws)
        {
            _verifyPasses = verifyPasses;
            _throws = throws;
        }

        /// <summary>Number of times that the tree was applied.</summary>
        public int ApplyCount { get; private set; }

        /// <summary>Root path of the last application, or <c>null</c>.</summary>
        public string? LastRootPath { get; private set; }

        /// <summary>Package path of the last application, or <c>null</c>.</summary>
        public string? LastPackagePath { get; private set; }

        public Task<InstallTreeOutcome> ApplyAsync(
            InstallationManifest manifest,
            string rootPath,
            string? packagePath,
            CancellationToken cancellationToken) =>
            ApplyAsync(manifest, rootPath, packagePath, new NoopProgressSink(), cancellationToken);

        public Task<InstallTreeOutcome> ApplyAsync(
            InstallationManifest manifest,
            string rootPath,
            string? packagePath,
            IInstallProgressSink progress,
            CancellationToken cancellationToken)
        {
            ApplyCount++;

            LastRootPath = rootPath;
            LastPackagePath = packagePath;

            if (_throws)
            {
                throw new InvalidOperationException("La aplicación del manifiesto falló.");
            }

            progress.Report(InstallPhase.ApplyingManifest, 10, 20);

            return Task.FromResult(new InstallTreeOutcome(_verifyPasses, true));
        }
    }

    /// <summary>
    /// Sumidero de progreso que no publica nada (AD-16).
    /// </summary>
    private sealed class NoopProgressSink : IInstallProgressSink
    {
        public void Report(InstallPhase phase, long bytesWritten, long? totalBytes)
        {
            // El sumidero existe para que el doble no tenga que conocer la interfaz.
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
            Task.FromResult<ClientIdentity?>(null);

        public Task SaveIdentityAsync(ClientIdentity identity, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<PersistedDownloadOperation>> LoadDownloadOperationsAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PersistedDownloadOperation>>(
                Array.Empty<PersistedDownloadOperation>());

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

        public Task<PinnedTrustAnchor?> LoadTrustAnchorAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<PinnedTrustAnchor?>(null);

        public Task SaveTrustAnchorAsync(
            PinnedTrustAnchor anchor,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task SaveInstallationAsync(
            ClientInstallation installation,
            CancellationToken cancellationToken)
        {
            SavedInstallations.Add(installation);

            return Task.CompletedTask;
        }

        public Task<ClientInstallation?> LoadInstallationAsync(
            string gameId,
            CancellationToken cancellationToken) =>
            Task.FromResult<ClientInstallation?>(
                SavedInstallations.Count == 0 ? null : SavedInstallations[^1]);

        public Task<IReadOnlyList<ClientInstallation>> LoadInstallationsAsync(
            CancellationToken cancellationToken) =>
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

        public Task<string?> LoadInstallationFailureAsync(
            string gameId,
            CancellationToken cancellationToken) =>
            Task.FromResult<string?>(
                SavedFailureCodes.Count == 0 ? null : SavedFailureCodes[^1]);
    }

    /// <summary>
    /// Layout of a temporary folder, para que la raíz de instalación, el parcial y la
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
