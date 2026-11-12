using System.IO.Compression;
using System.Text;
using LANPartyHub.Client.Application;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Domain.Trust;
using LANPartyHub.Client.Host;
using LANPartyHub.Client.Infrastructure.Filesystem;
using LANPartyHub.Client.Infrastructure.Persistence;
using LANPartyHub.Client.Infrastructure.Trust;
using LANPartyHub.Server.Domain.Packages;
using LANPartyHub.Server.Infrastructure.Signing;
using LANPartyHub.Shared.Contracts;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LANPartyHub.IntegrationTests;

/// <summary>
/// Integration level (AD-16): the REPAIR of an invalid installation from the package that
/// was already VALIDATED, over a REAL package, a REAL install root and a REAL SQLite
/// file (AC1, AC2, AC3, UX-DR-20).
///
/// <para>
/// El test NO usa dobles del disco: el paquete es un <c>zip</c> de verdad, el manifiesto
/// lo firma la clave del evento, la raíz de instalación es una carpeta real y el estado
/// se persiste en un archivo SQLite real. Así que lo que se observa es lo que de verdad
/// pasa (AD-16).
/// </para>
/// <para>
/// La reparación es de CERO RED, así que el test NO levanta ningún servidor: el paquete
/// validado y el manifiesto firmado en caché son LOCALES (AC1, UX-DR-20).
/// </para>
/// </summary>
public sealed class InstallationRepairTests : IDisposable
{
    private const string GameId = "0193f2a1-b2c3-4d4e-5f60-718293a4b5c6";
    private const string PackageId = "paquete-reparacion-1";
    private const string Version = "1.2";

    private readonly string _workingDirectory;
    private readonly string _databasePath;
    private readonly string _installDirectory;

    /// <summary>Raíz de instalación de ESTE juego: la carpeta que lo confine (FR-8).</summary>
    private readonly string _gameRoot;
    private readonly string _downloadsDirectory;
    private readonly string _manifestsDirectory;

    /// <summary>Creates an isolated working directory for the test.</summary>
    public InstallationRepairTests()
    {
        _workingDirectory = Path.Combine(
            Path.GetTempPath(),
            "lanpartyhub-integration",
            Guid.NewGuid().ToString("N"));

        _databasePath = Path.Combine(_workingDirectory, "state.sqlite");
        _installDirectory = Path.Combine(_workingDirectory, "install");
        _gameRoot = Path.Combine(_installDirectory, GameId);
        _downloadsDirectory = Path.Combine(_workingDirectory, "downloads");
        _manifestsDirectory = Path.Combine(_workingDirectory, "manifests");

        Directory.CreateDirectory(_workingDirectory);
        Directory.CreateDirectory(_downloadsDirectory);
        Directory.CreateDirectory(_manifestsDirectory);
    }

    /// <summary>
    /// AC1: the repair rebuilds the DELETED executable of the installed tree from the
    /// package that was already validated, WITHOUT downloading it again (AC1, UX-DR-20).
    /// </summary>
    [Fact]
    public async Task The_repair_rebuilds_the_deleted_file_from_the_validated_package()
    {
        var fixture = await CreateFixtureAsync();

        // La instalación EXISTE y es válida: el archivo del juego está en la raíz.
        var installed = Path.Combine(_gameRoot, "game", "data", "level1.bin");

        Assert.True(File.Exists(installed));

        // El archivo se BORRA: la instalación queda inválida pero REPARABLE, porque el
        // paquete ya validado SIGUE en el disco (AC1, AC6).
        File.Delete(installed);

        Assert.False(File.Exists(installed));

        var outcome = await fixture.Composition.RepairService
            .RepairAsync(GameId, Version, TestContext.Current.CancellationToken);

        // AC1: la reparación lo REHIZO desde el paquete ya validado.
        Assert.True(outcome.IsRepaired);
        Assert.True(outcome.Verdict!.IsInstalled);
        Assert.Equal(InstallationState.Installed, outcome.Verdict.State);

        // El archivo vuelve a estar en la raíz, con el contenido EXACTO del paquete.
        Assert.True(File.Exists(installed));
        Assert.Equal(
            Fixture.PackageContent,
            await File.ReadAllTextAsync(installed, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// AC1, AD-22: la fila de la instalación queda en «Installed» y su
    /// <c>installDirectoryId</c> NO cambia ante la reparación.
    /// </summary>
    [Fact]
    public async Task The_repair_persists_the_installed_state_and_keeps_the_root_identifier()
    {
        var fixture = await CreateFixtureAsync();

        var installed = Path.Combine(_gameRoot, "game", "data", "level1.bin");

        File.Delete(installed);

        var outcome = await fixture.Composition.RepairService
            .RepairAsync(GameId, Version, TestContext.Current.CancellationToken);

        await using var context = new ClientStateDbContext(BuildOptions(_databasePath));

        var store = new ClientStateStore(context, fixture.Layout);

        var installation = await store.LoadInstallationAsync(
            GameId,
            TestContext.Current.CancellationToken);

        Assert.NotNull(installation);
        Assert.Equal(InstallationState.Installed, installation!.ValidationState);
        Assert.Equal(Version, installation.InstalledVersion);

        // AC1, AD-22: el identificador de la raíz NO cambia ante la reparación.
        Assert.Equal(
            outcome.InstallDirectoryId,
            installation.InstallDirectoryId);
    }

    /// <summary>
    /// AC1, UX-DR-20: la reparación NO vuelve a descargar: el paquete validado conserva
    /// su contenido y su <c>sha256</c> no cambia.
    /// </summary>
    [Fact]
    public async Task The_repair_does_not_download_the_package_again()
    {
        var fixture = await CreateFixtureAsync();

        var installed = Path.Combine(_gameRoot, "game", "data", "level1.bin");

        File.Delete(installed);

        var packagePath = fixture.Layout.PartialFilePath(PackageId);

        var before = await File.ReadAllBytesAsync(packagePath, TestContext.Current.CancellationToken);

        var outcome = await fixture.Composition.RepairService
            .RepairAsync(GameId, Version, TestContext.Current.CancellationToken);

        Assert.True(outcome.IsRepaired);

        // El paquete sigue en su sitio, con los MISMOS bytes: nadie lo volvió a bajar.
        Assert.True(File.Exists(packagePath));

        var after = await File.ReadAllBytesAsync(packagePath, TestContext.Current.CancellationToken);

        Assert.Equal(before, after);
    }

    /// <summary>
    /// AC1, AC2: un <c>sha256</c> que ya NO coincide deja de ser reparable: la
    /// reparación se rechaza y el archivo NO se reconstruye.
    /// </summary>
    [Fact]
    public async Task A_package_whose_hash_diverged_is_not_repairable()
    {
        var fixture = await CreateFixtureAsync();

        var installed = Path.Combine(_gameRoot, "game", "data", "level1.bin");

        File.Delete(installed);

        // Los bytes del paquete YA NO son los que declara el manifiesto firmado.
        await File.WriteAllBytesAsync(
            fixture.Layout.PartialFilePath(PackageId),
            Encoding.UTF8.GetBytes("contenido alterado"),
            TestContext.Current.CancellationToken);

        var outcome = await fixture.Composition.RepairService
            .RepairAsync(GameId, Version, TestContext.Current.CancellationToken);

        Assert.True(outcome.IsRejected);
        Assert.Equal(ErrorCatalog.PackageIntegrityMismatchCode, outcome.Error!.Code);

        // AC1: el archivo NO se reconstruyó, porque la reparación se rechazó ANTES de
        // extraer.
        Assert.False(File.Exists(installed));
    }

    /// <summary>
    /// AC1, AC2: sin el paquete validado en disco no hay reparación, y el motivo visible
    /// es el de la RE-DESCARGA.
    /// </summary>
    [Fact]
    public async Task Without_the_validated_package_there_is_no_repair()
    {
        var fixture = await CreateFixtureAsync();

        var installed = Path.Combine(_gameRoot, "game", "data", "level1.bin");

        File.Delete(installed);

        File.Delete(fixture.Layout.PartialFilePath(PackageId));

        var outcome = await fixture.Composition.RepairService
            .RepairAsync(GameId, Version, TestContext.Current.CancellationToken);

        Assert.True(outcome.IsRejected);
        Assert.Equal(ErrorCatalog.InstallationPackageMismatchCode, outcome.Error!.Code);
        Assert.False(File.Exists(installed));
    }

    /// <summary>
    /// AC1, AD-9: la reparación escribe DENTRO de la raíz autorizada y NADA fuera de
    /// ella (AD-9, AC1).
    /// </summary>
    [Fact]
    public async Task The_repair_writes_nothing_outside_the_authorized_directory()
    {
        var fixture = await CreateFixtureAsync();

        var installed = Path.Combine(_gameRoot, "game", "data", "level1.bin");

        File.Delete(installed);

        await fixture.Composition.RepairService
            .RepairAsync(GameId, Version, TestContext.Current.CancellationToken);

        // NADA se escribió fuera de la raíz autorizada (AD-9, AC1).
        Assert.False(Directory.Exists(Path.Combine(_workingDirectory, "game")));
        Assert.False(File.Exists(Path.Combine(_workingDirectory, "escape.txt")));
    }

    /// <summary>
    /// AC1, AC5: la reparación REUTILIZA la certificación: un árbol que NO verifica deja
    /// la instalación reparable, con su motivo visible.
    /// </summary>
    [Fact]
    public async Task A_repaired_tree_that_does_not_verify_stays_repairable()
    {
        // El manifiesto VERIFICA un archivo que el paquete NO trae, así que el árbol NUNCA
        /// verifica y la instalación queda REPARABLE con su motivo visible (AC5, AC6).
        var fixture = await CreateFixtureAsync(verifyMissingFile: true);

        var outcome = await fixture.Composition.RepairService
            .RepairAsync(GameId, Version, TestContext.Current.CancellationToken);

        Assert.True(outcome.IsRepaired);
        Assert.True(outcome.Verdict!.IsRepairable);
        Assert.Equal(InstallationState.Repairable, outcome.Verdict.State);
        Assert.NotNull(outcome.Verdict.ReasonText);
        Assert.NotNull(outcome.Verdict.RepairAction);
    }

    /// <summary>
    /// Builds the fixture of the repair: un paquete REAL, un manifiesto REAL firmado y una
    /// instalación REAL ya aplicada (AD-16).
    /// </summary>
    /// <param name="verifyMissingFile">
    /// Whether the manifest VERIFICA un archivo que el paquete NO trae, así que el árbol
    /// no verifica (AC5).
    /// </param>
    /// <returns>The fixture of the repair.</returns>
    private async Task<Fixture> CreateFixtureAsync(bool verifyMissingFile = false)
    {
        var layout = new TemporaryLayout(
            _workingDirectory,
            _installDirectory,
            _downloadsDirectory,
            _manifestsDirectory,
            _databasePath);

        // El PAQUETE REAL: un `zip` de verdad, con los bytes que el manifiesto firmado
        // declara (AD-2, AD-8).
        var packageBytes = BuildPackage();

        var packagePath = layout.PartialFilePath(PackageId);

        await File.WriteAllBytesAsync(
            packagePath,
            packageBytes,
            TestContext.Current.CancellationToken);

        var signer = new Ed25519ManifestSigner();

        // AD-11: las migraciones de la base de datos del cliente se aplican antes de
        // escribir, así que la fila de la instalación tiene su tabla.
        await using (var context = new ClientStateDbContext(BuildOptions(_databasePath)))
        {
            await new ClientStateStore(context, layout).MigrateAsync(
                TestContext.Current.CancellationToken);
        }

        var manifest = new InstallationManifest(
            1,
            PackageId,
            Version,
            packageBytes.LongLength,
            LANPartyHub.Client.Domain.Integrity.PackageIntegrity.ComputeSha256(packageBytes),
            new InstallationManifest.LaunchSettings("game/game.exe", ["-novid"], "game"),
            [
                new InstallationManifest.CreateDirectory("game/data"),
                new InstallationManifest.Extract("data/level1.bin", "game/data"),
                new InstallationManifest.CopyFile("readme.txt", "game/readme.txt"),
                new InstallationManifest.SetLaunchConfig(
                    new InstallationManifest.LaunchSettings("game/game.exe", ["-novid"], "game")),
                new InstallationManifest.Verify(
                    verifyMissingFile ? "game/ausente.txt" : "game/readme.txt"),
            ]);

        var canonical = ManifestCanonicalizer.Canonicalize(manifest);

        var envelope = new InstallationManifest.SignedEnvelope(
            canonical,
            signer.Sign(canonical),
            signer.GetKeyId());

        var composition = BuildComposition(layout, envelope, signer);

        // La instalación REAL: el coordinador aplica el manifiesto dentro de la raíz, así
        // que el árbol que la reparación va a rehacer EXISTE de verdad (AC1).
        var installed = await composition.Coordinator
            .InstallAsync(GameId, PackageId, Version, TestContext.Current.CancellationToken);

        Assert.True(installed.IsCertified);

        // Con un archivo que el manifiesto NO verifica, el árbol NO verifica y la
        // instalación queda REPARABLE (AC5, AC6).
        Assert.Equal(
            verifyMissingFile ? InstallationState.Repairable : InstallationState.Installed,
            installed.Verdict!.State);

        // El snapshot del catálogo declara el paquete de la VERSIÓN INSTALADA, así que la
        // reparación puede resolverlo (AC1).
        var snapshot = new CatalogSnapshot(
            1,
            [],
            [
                new PackageDescriptor(
                    PackageId,
                    GameId,
                    Version,
                    packageBytes.LongLength,
                    0,
                    manifest.Sha256,
                    $"{PackageId}.json"),
            ]);

        await using (var context = new ClientStateDbContext(BuildOptions(_databasePath)))
        {
            await new CatalogSnapshotStore(context)
                .SaveAsync(snapshot, TestContext.Current.CancellationToken);
        }

        // El manifiesto firmado queda en la CACHÉ local, que es de donde la reparación lo
        // lee: la reparación es de CERO RED (AC1, UX-DR-20).
        var manifestCache = new ManifestCache(layout);

        await manifestCache.StoreAsync(
            PackageId,
            canonical,
            TestContext.Current.CancellationToken);

        return new Fixture(layout, composition, packageBytes, canonical);
    }

    /// <summary>
    /// Builds the REAL package: a <c>zip</c> with the bytes of the manifest (AD-7, AD-8).
    /// </summary>
    /// <returns>The bytes of the package.</returns>
    private static byte[] BuildPackage()
    {
        using var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "data/level1.bin", Fixture.PackageContent);

            WriteEntry(archive, "readme.txt", "El juego de prueba del evento.");
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Writes one entry in the archive of the package (AD-7).
    /// </summary>
    /// <param name="archive">Archive of the package.</param>
    /// <param name="name">Name of the entry.</param>
    /// <param name="content">Content of the entry.</param>
    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);

        using var stream = entry.Open();

        stream.Write(Encoding.UTF8.GetBytes(content));
    }

    /// <summary>
    /// Builds the composition over the REAL adapters (AD-16).
    /// </summary>
    /// <param name="layout">Layout of the data of the client.</param>
    /// <param name="envelope">Signed envelope that the server serves.</param>
    /// <param name="signer">Signer of the event, whose key is the fixed anchor.</param>
    /// <returns>The composition of the installations.</returns>
    private static InstallationComposition BuildComposition(
        TemporaryLayout layout,
        InstallationManifest.SignedEnvelope envelope,
        Ed25519ManifestSigner signer)
    {
        var loggerFactory = NullLoggerFactory.Instance;

        var stateStore = new ClientStateStore(
            new ClientStateDbContext(BuildOptions(layout.StateDatabasePath)),
            layout);

        var snapshotStore = new CatalogSnapshotStore(
            new ClientStateDbContext(BuildOptions(layout.StateDatabasePath)));

        return new InstallationComposition(
            new FixedManifestClient(envelope),
            new Ed25519ManifestVerifier(),
            new FixedTrustAnchorReader(
                new PinnedTrustAnchor(
                    Convert.ToBase64String(Ed25519ManifestSigner.ComputePublicKey(Environment.MachineName)),
                    "fingerprint",
                    "2026-10-26T00:00:00.0000000Z")),
            new ManifestCache(layout),
            layout,
            stateStore,
            new InstallPathResolver(layout),
            loggerFactory,
            new ConfirmingPrivilegedOperationConfirmation(),
            snapshotStore);
    }

    /// <summary>
    /// Port de la confirmación de la operación privilegiada que CONFIRMA, así que la
    /// instalación real se aplica sin que el test tenga que responder (AC2, AD-16).
    /// </summary>
    private sealed class ConfirmingPrivilegedOperationConfirmation : IPrivilegedOperationConfirmation
    {
        public Task<bool> ConfirmAsync(
            string operation,
            string detail,
            CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    /// <summary>
    /// Builds the options of the context of the client over the REAL SQLite file
    /// (AD-11).
    /// </summary>
    /// <param name="databasePath">Absolute path of the SQLite file.</param>
    /// <returns>The options of the context.</returns>
    private static DbContextOptions<ClientStateDbContext> BuildOptions(string databasePath)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false,
        }.ToString();

        return new DbContextOptionsBuilder<ClientStateDbContext>()
            .UseSqlite(
                connectionString,
                sqlite => sqlite.MigrationsAssembly(typeof(ClientStateDbContext).Assembly.GetName().Name))
            .Options;
    }

    /// <summary>Removes the working directory of the test.</summary>
    public void Dispose()
    {
        if (!Directory.Exists(_workingDirectory))
        {
            return;
        }

        try
        {
            Directory.Delete(_workingDirectory, recursive: true);
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

    /// <summary>
    /// Fixture of the repair: el layout, la composición y los bytes del paquete (AD-16).
    /// </summary>
    private sealed class Fixture
    {
        public Fixture(TemporaryLayout layout, InstallationComposition composition, byte[] packageBytes, string canonical)
        {
            Layout = layout;
            Composition = composition;
            PackageBytes = packageBytes;
            Canonical = canonical;
        }

        /// <summary>Layout of the data folder of the client.</summary>
        public TemporaryLayout Layout { get; }

        /// <summary>The composition of the installations.</summary>
        public InstallationComposition Composition { get; set; }

        /// <summary>The bytes of the real package.</summary>
        public byte[] PackageBytes { get; }

        /// <summary>The canonical text of the signed manifest (AD-16).</summary>
        public string Canonical { get; }

        /// <summary>Content of the file that the package carries (AD-7).</summary>
        public const string PackageContent = "contenido del nivel uno del juego de prueba";
    }

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
    /// Layout of the data folder of the client, sobre la carpeta temporal del test
    /// (AD-9).
    /// </summary>
    private sealed class TemporaryLayout : IClientDataLayout
    {
        private readonly string _folder;
        private readonly string _installDirectory;

        public TemporaryLayout(
            string folder,
            string installDirectory,
            string downloadsDirectory,
            string manifestsDirectory,
            string stateDatabasePath)
        {
            _folder = folder;
            _installDirectory = installDirectory;
            DownloadsDirectory = downloadsDirectory;
            ManifestsDirectory = manifestsDirectory;
            StateDatabasePath = stateDatabasePath;
        }

        public string Folder => _folder;

        public string InstallDirectory => _installDirectory;

        public string GameInstallDirectory(string gameId) =>
            LANPartyHub.Client.Domain.Install.InstallPathConfine.GameRoot(_installDirectory, gameId);

        public string DownloadsDirectory { get; }

        public string ManifestsDirectory { get; }

        public string StateDatabasePath { get; }

        public string PartialFilePath(string packageId) =>
            Path.Combine(DownloadsDirectory, packageId + ".part");

        public void EnsureCreated()
        {
            Directory.CreateDirectory(DownloadsDirectory);
            Directory.CreateDirectory(ManifestsDirectory);
        }

        public void SetInstallDirectory(string installDirectory) =>
            throw new NotSupportedException("La prueba no mueve la raíz de instalación.");
    }
}
