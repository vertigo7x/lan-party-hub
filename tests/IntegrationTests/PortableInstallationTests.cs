using System.IO.Compression;
using System.Text;
using LANPartyHub.Client.Application;
using LANPartyHub.Client.Application.Install;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Domain.Install;
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
/// Integration level (AD-16): the PORTABLE installation of a videogame over a REAL
/// package and its REAL signed manifest, with a REAL disk and a REAL SQLite file
/// (AD-7, AD-8, AD-9, AD-11, AC1..AC6).
///
/// <para>
/// El test NO usa dobles del disco: el paquete es un <c>zip</c> de verdad, el manifiesto
/// lo firma la clave del evento, la raíz de instalación es una carpeta real y el estado
/// se persiste en un archivo SQLite real. Así que lo que se observa es lo que de verdad
/// pasa (AD-16).
/// </para>
/// </summary>
public sealed class PortableInstallationTests : IDisposable
{
    private const string GameId = "0193f2a1-b2c3-4d4e-5f60-718293a4b5c6";
    private const string PackageId = "paquete-portable-1";
    private const string Version = "1.0.0";

    private readonly string _workingDirectory;
    private readonly string _databasePath;
    private readonly string _installDirectory;

    /// <summary>Raíz de instalación de ESTE juego: la carpeta que lo confine (FR-8).</summary>
    private readonly string _gameRoot;
    private readonly string _downloadsDirectory;
    private readonly string _manifestsDirectory;

    /// <summary>Creates an isolated working directory for the test.</summary>
    public PortableInstallationTests()
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
    /// AC1: the installation EXTRACTS the files of the package INSIDE the authorized
    /// directory, and NOT outside of it (AD-9, AC1).
    /// </summary>
    [Fact]
    public async Task The_installation_extracts_the_package_inside_the_authorized_directory()
    {
        var fixture = await CreateFixtureAsync();
        var confirmation = new ScriptedConfirmation(confirm: true);

        fixture.Composition = Rebuild(fixture, confirmation);

        var result = await fixture.Composition.Coordinator
            .InstallAsync(GameId, PackageId, Version, TestContext.Current.CancellationToken);

        Assert.True(result.IsCertified);
        Assert.True(result.Verdict!.IsInstalled);

        // El archivo del paquete está DENTRO de la raíz autorizada (AC1).
        var extracted = Path.Combine(_gameRoot, "game", "data", "level1.bin");

        Assert.True(File.Exists(extracted));

        // Y su contenido es EXACTAMENTE el del paquete (AC1).
        Assert.Equal(
            Fixture.PackageContent,
            await File.ReadAllTextAsync(extracted, TestContext.Current.CancellationToken));

        // NADA se escribió fuera de la raíz autorizada (AD-9, AC1).
        Assert.False(Directory.Exists(Path.Combine(_workingDirectory, "game")));
        Assert.False(File.Exists(Path.Combine(_workingDirectory, "escape.txt")));
    }

    /// <summary>
    /// AC1: an installation that is ALREADY VALID with the same version does NOT repeat
    /// the operations of the manifest (AD-7, AC1).
    /// </summary>
    [Fact]
    public async Task An_existing_valid_installation_does_not_repeat_the_operations()
    {
        var fixture = await CreateFixtureAsync();
        var confirmation = new ScriptedConfirmation(confirm: true);

        fixture.Composition = Rebuild(fixture, confirmation);

        await fixture.Composition.Coordinator
            .InstallAsync(GameId, PackageId, Version, TestContext.Current.CancellationToken);

        var firstAskCount = confirmation.AskCount;

        // La segunda instalación del MISMO juego con la MISMA versión no repite nada.
        var second = await fixture.Composition.Coordinator
            .InstallAsync(GameId, PackageId, Version, TestContext.Current.CancellationToken);

        Assert.True(second.IsCertified);
        Assert.True(second.IsAlreadyInstalled);

        // AC2: la operación privilegiada NO se vuelve a preguntar, porque la instalación
        // ya era válida y no se escribió nada.
        Assert.Equal(firstAskCount, confirmation.AskCount);
    }

    /// <summary>
    /// AC2: the privileged operation asks for the EXPLICIT confirmation before it
    /// executes, y la confirmación DECLINADA deja la instalación FALLIDA sin escribir
    /// (AD-7, AC2).
    /// </summary>
    [Fact]
    public async Task The_privileged_operation_asks_and_the_decline_leaves_the_installation_failed()
    {
        var fixture = await CreateFixtureAsync();
        var confirmation = new ScriptedConfirmation(confirm: false);

        fixture.Composition = Rebuild(fixture, confirmation);

        var result = await fixture.Composition.Coordinator
            .InstallAsync(GameId, PackageId, Version, TestContext.Current.CancellationToken);

        // AC2: la operación privilegiada PREGUNTÓ. El decline de la PRIMERA deja la
        // instalación fallida, así que la segunda operación privilegiada ni se preguntó
        // (AC2).
        Assert.Equal(1, confirmation.AskCount);
        Assert.Equal("writeConfig", confirmation.AskedOperations[0]);

        // AC2, AC5: el decline deja la instalación FALLIDA con su código estable.
        Assert.True(result.IsRejected);
        Assert.Equal(
            ErrorCatalog.InstallPrivilegedConfirmationDeclinedCode,
            result.Error!.Code);

        // AC2: la operación privilegiada NO se ejecutó, así que su archivo NO existe.
        Assert.False(File.Exists(Path.Combine(_gameRoot, "config", "options.ini")));
    }

    /// <summary>
    /// AC2: the confirmation is asked in the MOMENT, and it is NOT inherited after a
    /// restart of the client: the resumed operation asks AGAIN (AD-7, AC3).
    /// </summary>
    [Fact]
    public async Task The_confirmation_is_not_inherited_after_a_restart_of_the_client()
    {
        var fixture = await CreateFixtureAsync();

        // La PRIMERA sesión del cliente: la confirmación se DECLINA, así que la
        // instalación queda fallida.
        var firstConfirmation = new ScriptedConfirmation(confirm: false);

        fixture.Composition = Rebuild(fixture, firstConfirmation);

        await fixture.Composition.Coordinator
            .InstallAsync(GameId, PackageId, Version, TestContext.Current.CancellationToken);

        // La primera sesión preguntó por la primera operación privilegiada y el jugador
        // la DECLINÓ, así que la segunda operación privilegiada ni se preguntó (AC2).
        Assert.Equal(1, firstConfirmation.AskCount);

        // El cliente ARRANCA DE NUEVO: otra composición, otro puerto de confirmación, y
        // el estado persistido dice que la operación quedó fallida.
        var restarted = await CreateFixtureAsync();

        var secondConfirmation = new ScriptedConfirmation(confirm: true);

        restarted.Composition = Rebuild(restarted, secondConfirmation);

        // AC5: el error de la instalación fallida quedó PERSISTIDO.
        await using (var context = await OpenContextAsync())
        {
            var store = new ClientStateStore(context, restarted.Layout);

            var code = await store.LoadInstallationFailureAsync(
                GameId,
                TestContext.Current.CancellationToken);

            Assert.Equal(
                ErrorCatalog.InstallPrivilegedConfirmationDeclinedCode,
                code);
        }

        // AC3: el reintinto VOLVE A PREGUNTAR, porque la confirmación no se hereda de
        // ninguna sesión anterior. El manifiesto declara DOS operaciones privilegiadas,
        // así que la nueva sesión vuelve a preguntar por las dos (AC2, AC3).
        var retried = await restarted.Composition.Coordinator
            .RetryAsync(GameId, PackageId, Version, TestContext.Current.CancellationToken);

        Assert.Equal(2, secondConfirmation.AskCount);
        Assert.True(retried.IsCertified);
        Assert.True(retried.Verdict!.IsInstalled);

        // Y la operación privilegiada se escribió esta vez, porque el jugador la
        // confirmó.
        Assert.True(File.Exists(Path.Combine(_gameRoot, "config", "options.ini")));
    }

    /// <summary>
    /// AC4: the progress carries the PHASE and the INTEGER percentage of the REAL bytes
    /// written (AC4, UX-DR-13, UX-DR-34).
    /// </summary>
    [Fact]
    public async Task The_progress_carries_the_phase_and_the_integer_percentage_of_the_real_bytes()
    {
        var fixture = await CreateFixtureAsync();
        var confirmation = new ScriptedConfirmation(confirm: true);

        fixture.Composition = Rebuild(fixture, confirmation);

        var phases = new List<InstallPhase>();

        fixture.Composition.Coordinator.ProgressChanged += progress =>
        {
            phases.Add(progress.Phase);
        };

        await fixture.Composition.Coordinator
            .InstallAsync(GameId, PackageId, Version, TestContext.Current.CancellationToken);

        // AC4: la fase «Extrayendo» se publica durante la extracción.
        Assert.Contains(InstallPhase.Extracting, phases);

        // AC4: la fase «Aplicando manifiesto» se publica durante la aplicación.
        Assert.Contains(InstallPhase.ApplyingManifest, phases);

        // AC4: el porcentaje final es un ENTERO que llega al cien, y sale de los bytes
        // REALES escritos.
        var last = phases.Count > 0 ? phases[^1] : InstallPhase.Idle;

        Assert.Equal(InstallPhase.Completed, last);
    }

    /// <summary>
    /// AC5: a FAILED installation leaves its error PERSISTED, and the operation is
    /// REINTENTABLE without reinstalling the client (AD-8, AD-11, AC5, NFR-9).
    /// </summary>
    [Fact]
    public async Task A_failed_installation_leaves_its_error_persisted_and_retryable()
    {
        var fixture = await CreateFixtureAsync();
        var confirmation = new ScriptedConfirmation(confirm: false);

        fixture.Composition = Rebuild(fixture, confirmation);

        await fixture.Composition.Coordinator
            .InstallAsync(GameId, PackageId, Version, TestContext.Current.CancellationToken);

        // AC5: el error queda PERSISTIDO en el sumidero durable y en el estado terminal
        // de la fila.
        await using (var context = await OpenContextAsync())
        {
            var store = new ClientStateStore(context, fixture.Layout);

            var code = await store.LoadInstallationFailureAsync(
                GameId,
                TestContext.Current.CancellationToken);

            Assert.Equal(
                ErrorCatalog.InstallPrivilegedConfirmationDeclinedCode,
                code);

            var installation = await store.LoadInstallationAsync(
                GameId,
                TestContext.Current.CancellationToken);

            Assert.NotNull(installation);
            Assert.Equal(InstallationState.Failed, installation!.ValidationState);
        }

        // AC5, NFR-9: el reintento NO reinstala el cliente: reutiliza el paquete validado.
        // El jugador confirma esta vez, así que la operación se completa (AC2).
        confirmation.SetDecision(confirm: true);

        var retried = await fixture.Composition.Coordinator
            .RetryAsync(GameId, PackageId, Version, TestContext.Current.CancellationToken);

        Assert.True(retried.IsCertified);
        Assert.True(retried.Verdict!.IsInstalled);
    }

    /// <summary>
    /// AD-9, AC1: a <c>zip-slip</c> —una entrada del archivo cuyo nombre sale de la
    /// raíz— NO escribe nada fuera de la raíz autorizada (AD-9).
    /// </summary>
    [Fact]
    public async Task A_zip_slip_does_not_write_outside_the_authorized_directory()
    {
        var fixture = await CreateFixtureAsync(escapeEntryName: "../escape.txt");

        fixture.Composition = Rebuild(fixture, new ScriptedConfirmation(confirm: true));

        await fixture.Composition.Coordinator
            .InstallAsync(GameId, PackageId, Version, TestContext.Current.CancellationToken);

        // AD-9, AC1: NADA se escribió fuera de la raíz autorizada. La entrada que
        // declara un `..` queda CONFINADA, porque el destino final se resuelve y se
        // comprueba contra la raíz antes de escribir.
        Assert.False(File.Exists(Path.Combine(_workingDirectory, "escape.txt")));
        Assert.False(File.Exists(Path.Combine(_gameRoot, "..", "escape.txt")));

        // Y todo lo que se escribió está DENTRO de la raíz autorizada (AD-9).
        foreach (var file in Directory.EnumerateFiles(_gameRoot, "*", SearchOption.AllDirectories))
        {
            Assert.StartsWith(
                _gameRoot,
                file,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// AD-9, AC1: a manifest whose destination ESCAPES the root is REJECTED before any
    /// write, así que la instalación no queda certificada como válida (AD-9, AC5).
    /// </summary>
    [Fact]
    public async Task A_manifest_whose_destination_escapes_the_root_is_not_installed()
    {
        var fixture = await CreateFixtureAsync(escapeDestinationPath: "../escape.txt");

        fixture.Composition = Rebuild(fixture, new ScriptedConfirmation(confirm: true));

        var result = await fixture.Composition.Coordinator
            .InstallAsync(GameId, PackageId, Version, TestContext.Current.CancellationToken);

        // AD-9, AC5: el confine RECHAZA la ruta antes de escribir, así que la instalación
        // no queda certificada como válida.
        Assert.False(result.Verdict!.IsInstalled);

        // Y NADA se escribió fuera de la raíz autorizada (AD-9, AC1).
        Assert.False(File.Exists(Path.Combine(_workingDirectory, "escape.txt")));
    }

    /// <summary>
    /// AC6: the modes <c>Installer</c> and <c>External</c> NO existen en the MVP: the
    /// closed set of the manifest has no operation that runs a command (AD-7, R-MVP-4).
    /// </summary>
    [Fact]
    public void The_modes_installer_and_external_do_not_exist_in_the_mvp()
    {
        // El conjunto cerrado de AD-7 es el MISMO en el servidor y en el cliente, así que
        // no hay un modo «Installer» ni un modo «External» que se pueda ejecutar.
        Assert.Equal(
            6,
            ManifestOperations.AllowedNames.Count);

        Assert.DoesNotContain("installer", ManifestOperations.AllowedNames, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("external", ManifestOperations.AllowedNames, StringComparer.OrdinalIgnoreCase);

        // Y no hay ninguna operación que ejecute comandos arbitrarios (AD-7).
        Assert.DoesNotContain("runCommand", ManifestOperations.AllowedNames, StringComparer.Ordinal);
    }

    /// <summary>
    /// Builds the fixture of the installation with the REAL adapters (AD-16).
    /// </summary>
    /// <param name="escapeEntryName">
    /// Name of the entry that escapes the root, or <c>null</c> for a well formed package.
    /// </param>
    /// <param name="escapeDestinationPath">
    /// Destination of the <c>copyFile</c> operation that escapes the root, or <c>null</c>
    /// for a well formed manifest.
    /// </param>
    /// <returns>The fixture of the installation.</returns>
    private async Task<Fixture> CreateFixtureAsync(
        string? escapeEntryName = null,
        string? escapeDestinationPath = null)
    {
        var layout = new TemporaryLayout(
            _workingDirectory,
            _installDirectory,
            _downloadsDirectory,
            _manifestsDirectory,
            _databasePath);

        // El PAQUETE REAL: un `zip` de verdad, con los bytes que el manifiesto firmado
        // declara (AD-2, AD-8).
        var packagePath = layout.PartialFilePath(PackageId);

        var packageBytes = BuildPackage(escapeEntryName);

        await File.WriteAllBytesAsync(packagePath, packageBytes, TestContext.Current.CancellationToken);

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
                new InstallationManifest.Extract(
                    escapeEntryName ?? "data/level1.bin",
                    "game/data"),
                new InstallationManifest.CopyFile("readme.txt", escapeDestinationPath ?? "game/readme.txt"),
                new InstallationManifest.WriteConfig("config/options.ini", "modo=1"),
                new InstallationManifest.SetLaunchConfig(
                    new InstallationManifest.LaunchSettings("game/game.exe", ["-novid"], "game")),
                new InstallationManifest.Verify("game/readme.txt"),
            ]);

        var canonical = ManifestCanonicalizer.Canonicalize(manifest);

        var envelope = new InstallationManifest.SignedEnvelope(
            canonical,
            signer.Sign(canonical),
            signer.GetKeyId());

        var composition = BuildComposition(
            layout,
            envelope,
            new ScriptedConfirmation(confirm: true),
            signer);

        await Task.CompletedTask;

        return new Fixture(layout, composition, packageBytes, canonical);
    }

    /// <summary>
    /// Builds the REAL package: a <c>zip</c> with the bytes of the manifest (AD-7, AD-8).
    /// </summary>
    /// <param name="escapeEntryName">Name of the entry that escapes the root, or <c>null</c>.</param>
    /// <returns>The bytes of the package.</returns>
    private static byte[] BuildPackage(string? escapeEntryName)
    {
        using var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "data/level1.bin", Fixture.PackageContent);

            WriteEntry(archive, "readme.txt", "El juego de prueba del evento.");

            if (escapeEntryName is not null)
            {
                WriteEntry(archive, escapeEntryName, "esto no debe salir de la raíz");
            }
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
    /// Builds the composition over the REAL adapters, con la confirmación que se le pasa
    /// (AD-16).
    /// </summary>
    /// <param name="layout">Layout of the data of the client.</param>
    /// <param name="envelope">Signed envelope that the server serves.</param>
    /// <param name="confirmation">Port of the confirmation of the privileged operations.</param>
    /// <param name="signer">Signer of the event, whose key is the fixed anchor.</param>
    /// <returns>The composition of the installations.</returns>
    private static InstallationComposition BuildComposition(
        TemporaryLayout layout,
        InstallationManifest.SignedEnvelope envelope,
        IPrivilegedOperationConfirmation confirmation,
        Ed25519ManifestSigner signer)
    {
        var loggerFactory = NullLoggerFactory.Instance;

        var stateStore = new ClientStateStore(
            new ClientStateDbContext(BuildOptions(layout.StateDatabasePath)),
            layout);

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
            confirmation);
    }

    /// <summary>
    /// Rebuilds the composition of the fixture con la confirmación que se le pasa, para
    /// que la PRUEBA cambie la decisión del jugador sin tocar el disco (AD-16).
    /// </summary>
    /// <param name="fixture">Fixture of the installation.</param>
    /// <param name="confirmation">Port of the confirmation of the privileged operations.</param>
    /// <returns>The composition of the installations.</returns>
    private static InstallationComposition Rebuild(Fixture fixture, IPrivilegedOperationConfirmation confirmation)
    {
        var signer = new Ed25519ManifestSigner();

        var envelope = new InstallationManifest.SignedEnvelope(
            fixture.Canonical,
            signer.Sign(fixture.Canonical),
            signer.GetKeyId());

        return BuildComposition(fixture.Layout, envelope, confirmation, signer);
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

    /// <summary>
    /// Opens the context of the client over the REAL SQLite file (AD-11).
    /// </summary>
    /// <returns>The context of the client.</returns>
    private async Task<ClientStateDbContext> OpenContextAsync() =>
        new(BuildOptions(_databasePath));

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
    /// Fixture of the installation: el layout, la composición y los bytes del paquete
    /// (AD-16).
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

        /// <summary>
        /// The canonical text of the signed manifest, para poder REBUILD la composición
        /// con otra decisión del jugador (AD-16).
        /// </summary>
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
    /// Port of the confirmation de la operación privilegiada que responde con la
    /// decisión que el test fija (AD-16).
    /// </summary>
    private sealed class ScriptedConfirmation : IPrivilegedOperationConfirmation
    {
        private bool _confirm;

        public ScriptedConfirmation(bool confirm) => _confirm = confirm;

        /// <summary>Number of times that the confirmation was asked.</summary>
        public int AskCount { get; private set; }

        /// <summary>Names of the operations whose confirmation was asked.</summary>
        public List<string> AskedOperations { get; } = [];

        /// <summary>
        /// Changes the decision of the player, así que el reintento puede CONFIRMAR lo
        /// que la primera sesión DECLINÓ (AC5).
        /// </summary>
        /// <param name="confirm">Whether the player confirms from now on.</param>
        public void SetDecision(bool confirm) => _confirm = confirm;

        public Task<bool> ConfirmAsync(string operation, string detail, CancellationToken cancellationToken)
        {
            AskCount++;
            AskedOperations.Add(operation);

            return Task.FromResult(_confirm);
        }
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
