using System.Text;
using LANPartyHub.Client.Application;
using LANPartyHub.Client.Application.GameLauncher;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Domain.Install;
using LANPartyHub.Client.Infrastructure.Filesystem;
using LANPartyHub.Client.Infrastructure.Install;
using LANPartyHub.Client.Infrastructure.Persistence;
using LANPartyHub.Client.Infrastructure.Process;
using LANPartyHub.Shared.Contracts;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LANPartyHub.IntegrationTests;

/// <summary>
/// Integration level (AD-16): the LAUNCH of a videogame with a REAL executable on disk
/// (AD-19, FR-9, AC1, AC2, AC3).
///
/// <para>
/// El test NO usa dobles del disco ni del proceso: el ejecutable es una copia REAL del
/// INTÉRPRETE DEL HOST dentro de la raíz de instalación, y el <c>probe</c> que acompaña
/// registra los argumentos que recibió y el directorio de trabajo en el que corrió. Así
/// que lo que se observa es lo que de verdad pasa (AD-16).
/// </para>
/// <para>
/// El intérprete copiado es REAL: por eso el <c>probe</c> existe, porque un script
/// necesita un intérprete que lo ejecute. La copia vive DENTRO de la raíz de
/// instalación, así que el confine de AD-9 se respeta de verdad (AD-9).
/// </para>
/// <para>
/// El intérprete es el del HOST: <c>cmd.exe</c> en Windows, que es la plataforma del
/// producto (AD-17), y <c>sh</c> en los demás. El probe escribe en el lenguaje de ese
/// intérprete, así que lo que se observa es lo mismo en todas las plataformas: los
/// argumentos LLEGARON y el directorio de trabajo es la raíz (AC1).
/// </para>
/// </summary>
public sealed class GameLaunchTests : IDisposable
{
    private const string GameId = "0193f2a1-b2c3-4d4e-5f60-718293a4b5c6";
    private const string PackageId = "paquete-lanzador-1";
    private const string Version = "1.0.0";

    /// <summary>
    /// Version of ANOTHER live package of the SAME game: la que está instalada NO es
    /// (AC1, AD-26).
    /// </summary>
    private const string OtherVersion = "0.9.0";

    private readonly string _workingDirectory;
    private readonly string _databasePath;
    private readonly string _installDirectory;
    private readonly string _downloadsDirectory;
    private readonly string _manifestsDirectory;

    /// <summary>Creates an isolated working directory for the test.</summary>
    public GameLaunchTests()
    {
        _workingDirectory = Path.Combine(
            Path.GetTempPath(),
            "lanpartyhub-gamelaunch-integration",
            Guid.NewGuid().ToString("N"));

        _databasePath = Path.Combine(_workingDirectory, "state.sqlite");
        _installDirectory = Path.Combine(_workingDirectory, "install");
        _downloadsDirectory = Path.Combine(_workingDirectory, "downloads");
        _manifestsDirectory = Path.Combine(_workingDirectory, "manifests");

        Directory.CreateDirectory(_workingDirectory);
        Directory.CreateDirectory(_downloadsDirectory);
        Directory.CreateDirectory(_manifestsDirectory);
    }

    /// <summary>
    /// AC1: the arguments of the launch reach the child process IN ORDER, and the
    /// working directory of the process is the ROOT of the installation (AD-19, AC1).
    /// </summary>
    [Fact]
    public async Task The_arguments_reach_the_child_process_and_the_working_directory_is_the_root()
    {
        using var fixture = await CreateFixtureAsync(
            HostInterpreter.ExecutablePath,
            ["-novid", "-console", "mapa1"],
            runsProbe: true);

        var inspection = await fixture.Inspector
            .InspectAsync(GameId, TestContext.Current.CancellationToken);

        Assert.NotNull(inspection);

        // Los argumentos del lanzamiento son los que declara el manifiesto, y el
        // manifiesto los declara con el LENGUAJE del intérprete del host: `cmd.exe`
        // ejecuta el probe con `/c` y `sh` lo ejecuta por su nombre (AD-16, AD-17).
        Assert.Equal(
            HostInterpreter.ProbeArguments(
                HostInterpreter.ProbeRelativePath,
                ["-novid", "-console", "mapa1"]),
            inspection.LaunchSettings.Arguments);

        var outcome = await fixture.Service
            .LaunchAsync(GameId, TestContext.Current.CancellationToken);

        Assert.True(outcome.IsLaunched);

        // El proceso hijo tiene que TERMINAR para que el probe haya escrito sus archivos.
        await WaitForProbeAsync();

        // Los argumentos LLEGARON al hijo, en ORDEN (AC1).
        var arguments = await File.ReadAllTextAsync(
            Path.Combine(_installDirectory, "args.txt"),
            HostInterpreter.ProbeEncoding,
            TestContext.Current.CancellationToken);

        Assert.Contains("-novid", arguments, StringComparison.Ordinal);
        Assert.Contains("-console", arguments, StringComparison.Ordinal);
        Assert.Contains("mapa1", arguments, StringComparison.Ordinal);

        Assert.True(
            arguments.IndexOf("-novid", StringComparison.Ordinal)
            < arguments.IndexOf("-console", StringComparison.Ordinal),
            "Los argumentos NO llegaron en orden.");

        Assert.True(
            arguments.IndexOf("-console", StringComparison.Ordinal)
            < arguments.IndexOf("mapa1", StringComparison.Ordinal),
            "Los argumentos NO llegaron en orden.");

        // El directorio de trabajo del proceso es la RAÍZ de la instalación (AC1, AD-9).
        var workingDirectory = await File.ReadAllTextAsync(
            Path.Combine(_installDirectory, "cwd.txt"),
            HostInterpreter.ProbeEncoding,
            TestContext.Current.CancellationToken);

        Assert.Contains(
            Path.GetFullPath(_installDirectory),
            workingDirectory,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// AC1: a MISSING executable prevents the launch, the error names its EXACT path and
    /// NO process is left behind (AD-19, AC1, AD-9).
    /// </summary>
    [Fact]
    public async Task A_missing_executable_is_rejected_with_its_exact_path()
    {
        using var fixture = await CreateFixtureAsync(
            "game\\no-existe.exe",
            ["-novid"],
            createsExecutable: false);

        var outcome = await fixture.Service
            .LaunchAsync(GameId, TestContext.Current.CancellationToken);

        Assert.True(outcome.IsRejected);
        Assert.Equal(ErrorCatalog.GameLaunchExecutableMissingCode, outcome.Error!.Code);

        var expected = Path.Combine(_installDirectory, "game", "no-existe.exe");

        Assert.Contains(expected, outcome.Reason!, StringComparison.OrdinalIgnoreCase);

        // NINGÚN proceso se quedó en marcha (AC1).
        Assert.Null(outcome.Handle);
    }

    /// <summary>
    /// AC1, AD-9: an executable that resolves OUTSIDE the installation root is REJECTED
    /// and NO process is started (AD-9, AC1).
    /// </summary>
    [Fact]
    public async Task An_executable_outside_the_root_makes_the_installation_invalid_and_starts_no_process()
    {
        using var fixture = await CreateFixtureAsync(
            HostInterpreter.OutsideRootPath,
            ["-novid"],
            createsExecutable: false);

        var outcome = await fixture.Service
            .LaunchAsync(GameId, TestContext.Current.CancellationToken);

        Assert.True(outcome.IsRejected);
        // El inspector certifica ANTES de lanzar: un manifiesto cuya ruta de ejecutable
        // sale de la raíz hace inválida la instalación (AD-9, AD-19). El rechazo
        // específico de ruta del lanzador se cubre con el inspector aislado en unidad.
        Assert.Equal(ErrorCatalog.GameLaunchInstallationInvalidCode, outcome.Error!.Code);
        Assert.Null(outcome.Handle);
    }

    /// <summary>
    /// AC2: the client can tell whether the launched game is STILL RUNNING (AD-19, AC2).
    /// </summary>
    [Fact]
    public async Task The_client_tells_whether_the_process_is_still_running()
    {
        // El proceso tiene que SEGUIR VIVO: `ping` de Windows y `sleep` del host son
        // procesos que duran, y la prueba comprueba lo mismo en ambos (AC2).
        using var fixture = await CreateFixtureAsync(
            HostInterpreter.ExecutablePath,
            HostInterpreter.LongRunningArguments);

        var outcome = await fixture.Service
            .LaunchAsync(GameId, TestContext.Current.CancellationToken);

        Assert.True(outcome.IsLaunched);
        Assert.NotNull(outcome.Handle);

        // El proceso VIVO se declara en ejecución (AC2).
        Assert.True(outcome.Handle!.IsRunning);
    }

    /// <summary>
    /// AC1: an installation that is NOT certified as valid starts NO process, even with a
    /// real executable on disk (AD-19, AC1, AC5).
    /// </summary>
    [Fact]
    public async Task An_installation_that_is_not_certified_starts_no_process()
    {
        using var fixture = await CreateFixtureAsync(
            HostInterpreter.ExecutablePath,
            ["-novid"],
            certificationFails: true);

        var outcome = await fixture.Service
            .LaunchAsync(GameId, TestContext.Current.CancellationToken);

        Assert.True(outcome.IsRejected);
        Assert.Equal(ErrorCatalog.GameLaunchInstallationInvalidCode, outcome.Error!.Code);
        Assert.Null(outcome.Handle);
    }

    /// <summary>
    /// AC1: a game with SEVERAL live packages is inspected with the manifest of the
    /// INSTALLED VERSION, and not with the manifest of another one (AC1, AD-26).
    ///
    /// <para>
    /// El catálogo admite VARIOS paquetes vivos del mismo juego, así que el snapshot
    /// lleva más de un paquete del juego. El manifiesto que se lee es el de la VERSIÓN
    /// INSTALADA, porque es el único que describe el árbol que hay en disco (AC1).
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_manifest_of_the_installed_version_is_the_one_that_is_inspected()
    {
        using var fixture = await CreateFixtureAsync(
            HostInterpreter.ExecutablePath,
            ["-novid"],
            otherPackages: [AnotherPackage(OtherVersion, HostInterpreter.ExecutablePath, ["-otra-version"])]);

        var inspection = await fixture.Inspector
            .InspectAsync(GameId, TestContext.Current.CancellationToken);

        Assert.NotNull(inspection);

        // La instalación está CERTIFICADA, aunque el snapshot lleve otro paquete del
        // mismo juego con OTRA versión (AC1).
        Assert.True(inspection.Verdict.IsInstalled);

        // Los argumentos NO son los de la otra versión: el manifiesto de la versión
        // INSTALADA es el que se lee (AC1).
        Assert.Equal(["-novid"], inspection.LaunchSettings.Arguments);
        Assert.DoesNotContain("-otra-version", inspection.LaunchSettings.Arguments);
    }

    /// <summary>
    /// AC1: the manifest of ANOTHER version is NOT the one that is read, so the launch
    /// does NOT run the executable and the arguments of the other version against the
    /// installed tree (AC1, AD-26).
    ///
    /// <para>
    /// La caché guarda el manifiesto de la versión ANTERIOR porque ese paquete se
    /// validó antes, así que leerlo devolvería el ejecutable y los argumentos de la
    /// OTRA versión contra el árbol instalado (AC1).
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_manifest_of_another_version_is_not_the_one_that_is_inspected()
    {
        using var fixture = await CreateFixtureAsync(
            HostInterpreter.ExecutablePath,
            ["-novid"],
            otherPackages: [AnotherPackage(OtherVersion, HostInterpreter.ExecutablePath, ["-otra-version"])]);

        var inspection = await fixture.Inspector
            .InspectAsync(GameId, TestContext.Current.CancellationToken);

        Assert.NotNull(inspection);
        Assert.True(inspection.Verdict.IsInstalled);

        // Los argumentos NO son los de la otra versión: el manifiesto de la versión
        // INSTALADA es el que se lee (AC1).
        Assert.Equal(["-novid"], inspection.LaunchSettings.Arguments);
        Assert.DoesNotContain("-otra-version", inspection.LaunchSettings.Arguments);
    }

    /// <summary>
    /// AC1: an installation whose manifest of the OTHER version is NOT in the cache is
    /// STILL certified, because the manifest of the INSTALLED version is the one that
    /// is read (AC1, AD-26).
    ///
    /// <para>
    /// El paquete de la otra versión NO se descargó, así que su manifiesto no está en la
    /// caché. Leer el manifiesto equivocado devolvería «no certificada» y el juego
    /// instalado y válido quedaría sin poder jugarse (AC1).
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_installation_is_certified_when_the_manifest_of_the_other_version_is_not_cached()
    {
        using var fixture = await CreateFixtureAsync(
            HostInterpreter.ExecutablePath,
            ["-novid"],
            otherPackages: [AnotherPackage(OtherVersion, HostInterpreter.ExecutablePath, ["-otra-version"])],
            cachesOtherManifests: false);

        var inspection = await fixture.Inspector
            .InspectAsync(GameId, TestContext.Current.CancellationToken);

        Assert.NotNull(inspection);

        // La instalación es VÁLIDA y está CERTIFICADA: el manifiesto que se lee es el de
        // la versión INSTALADA, que sí está en la caché (AC1).
        Assert.True(inspection.Verdict.IsInstalled);
        Assert.Equal(["-novid"], inspection.LaunchSettings.Arguments);
    }

    /// <summary>
    /// AC1: the launch of an installed game whose OTHER version is ALSO published does
    /// NOT start the process of the other version (AC1, AD-26).
    ///
    /// <para>
    /// El proceso real arranca: lo que se observa es qué ejecutable y qué argumentos
    /// corrieron de verdad (AD-16, AC1).
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_launch_runs_the_installed_version_and_not_the_other_one()
    {
        using var fixture = await CreateFixtureAsync(
            HostInterpreter.ExecutablePath,
            ["-novid"],
            otherPackages: [AnotherPackage(OtherVersion, HostInterpreter.ExecutablePath, ["-otra-version"])],
            runsProbe: true);

        var outcome = await fixture.Service
            .LaunchAsync(GameId, TestContext.Current.CancellationToken);

        Assert.True(outcome.IsLaunched);

        await WaitForProbeAsync();

        // Los argumentos que LLEGARON al proceso son los de la versión INSTALADA, no los
        // de la otra versión (AC1).
        var arguments = await File.ReadAllTextAsync(
            Path.Combine(_installDirectory, "args.txt"),
            HostInterpreter.ProbeEncoding,
            TestContext.Current.CancellationToken);

        Assert.Contains("-novid", arguments, StringComparison.Ordinal);
        Assert.DoesNotContain("-otra-version", arguments, StringComparison.Ordinal);
    }

    /// <summary>
    /// Waits until the probe of the child process has written its files (AD-16).
    /// </summary>
    private async Task WaitForProbeAsync()
    {
        var arguments = Path.Combine(_installDirectory, "args.txt");
        var workingDirectory = Path.Combine(_installDirectory, "cwd.txt");

        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (File.Exists(arguments) && File.Exists(workingDirectory))
            {
                return;
            }

            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException("El proceso hijo no escribió los archivos del probe.");
    }

    /// <summary>
    /// Builds the fixture of the launch: the real layout, the real store, the real
    /// resolver, the real runner and the real service (AD-16).
    /// </summary>
    /// <param name="executablePath">Path of the executable, relative to the root.</param>
    /// <param name="arguments">Arguments of the launch.</param>
    /// <param name="createsExecutable">Whether the executable is written on disk.</param>
    /// <param name="runsProbe">Whether the child process writes the probe files.</param>
    /// <param name="certificationFails">Whether the manifest declares a missing file.</param>
    /// <param name="otherPackages">
    /// OTHER live packages of the SAME game, with their own version and their own
    /// launch configuration (AD-26).
    /// </param>
    /// <param name="cachesOtherManifests">
    /// Whether the manifest of the OTHER packages is written in the cache, as it happens
    /// when those packages were validated before (AD-2).
    /// </param>
    /// <returns>The fixture of the launch.</returns>
    private async Task<LaunchFixture> CreateFixtureAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        bool createsExecutable = true,
        bool runsProbe = false,
        bool certificationFails = false,
        IReadOnlyList<OtherPackage>? otherPackages = null,
        bool cachesOtherManifests = true)
    {
        var layout = ClientDataLayout.Create(_workingDirectory, _installDirectory);

        layout.EnsureCreated();

        // El INTERPRETE REAL se copia DENTRO de la raíz de instalación, así que el
        // confine de AD-9 se respeta de verdad (AD-9).
        var executable = Path.Combine(_installDirectory, executablePath);

        Directory.CreateDirectory(Path.GetDirectoryName(executable)!);

        var interpreter = HostInterpreter.Path;

        if (!File.Exists(interpreter))
        {
            throw new InvalidOperationException(
                $"El intérprete '{interpreter}' no existe: la prueba de lanzamiento necesita un ejecutable real.");
        }

        if (createsExecutable)
        {
            File.Copy(interpreter, executable, overwrite: true);
        }

        // El PROBE registra los argumentos que recibió y el directorio de trabajo en el
        // que corrió (AD-16).
        string? probePath = null;

        if (runsProbe)
        {
            var probe = Path.Combine(_installDirectory, HostInterpreter.ProbeRelativePath);

            await File.WriteAllTextAsync(
                probe,
                HostInterpreter.ProbeContent,
                TestContext.Current.CancellationToken);

            probePath = Path.GetRelativePath(_installDirectory, probe);
        }

        IReadOnlyList<string> launchArguments = runsProbe
            ? HostInterpreter.ProbeArguments(probePath!, arguments)
            : arguments;

        var context = await OpenContextAsync();

        var store = new ClientStateStore(context, layout);

        await store.MigrateAsync(TestContext.Current.CancellationToken);

        await store.SaveInstallationAsync(
            new ClientInstallation(
                GameId,
                Version,
                InstallationState.Installed,
                _installDirectory,
                InstallationCertification.DeriveInstallDirectoryId(_installDirectory)),
            TestContext.Current.CancellationToken);

        // El manifiesto FIRMADO que declara la configuración de lanzamiento (AD-2).
        var manifest = new InstallationManifest(
            1,
            PackageId,
            Version,
            1,
            new string('a', 64),
            new InstallationManifest.LaunchSettings(executablePath, launchArguments, "."),
            certificationFails
                ? [new InstallationManifest.Verify("game\\certification-missing.bin")]
                : []);

        var snapshot = new CatalogSnapshot(
            1,
            [],
            [
                // El catálogo admite VARIOS paquetes vivos del mismo juego, y el snapshot
                // los lleva TODOS (AD-26). El servidor los ORDENA por identificador de
                // paquete, así que el paquete de la OTRA versión —con un identificador
                // menor— va ANTES que el de la versión instalada: es el primero que
                // encuentra una búsqueda que no distingue la versión (AC1).
                ..(otherPackages ?? []).Select(package =>
                    new PackageDescriptor(
                        package.PackageId,
                        GameId,
                        package.Version,
                        1,
                        0,
                        new string('a', 64),
                        package.PackageId)),
                new PackageDescriptor(PackageId, GameId, Version, 1, 0, new string('a', 64), PackageId),
            ]);

        var snapshots = new CatalogSnapshotStore(context);

        await snapshots.SaveAsync(snapshot, TestContext.Current.CancellationToken);

        var cache = new ManifestCache(layout);

        await cache.StoreAsync(
            PackageId,
            System.Text.Json.JsonSerializer.Serialize(
                manifest,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)),
            TestContext.Current.CancellationToken);

        // El manifiesto de la OTRA versión TAMBIÉN puede estar en la caché, porque ese
        // paquete se validó antes: leerlo ejecutaría el ejecutable y los argumentos de
        // la otra versión contra el árbol instalado (AC1).
        foreach (var package in cachesOtherManifests ? otherPackages ?? [] : [])
        {
            var otherManifest = new InstallationManifest(
                1,
                package.PackageId,
                package.Version,
                1,
                new string('a', 64),
                new InstallationManifest.LaunchSettings(
                    package.ExecutablePath,
                    package.Arguments,
                    "."),
                []);

            await cache.StoreAsync(
                package.PackageId,
                System.Text.Json.JsonSerializer.Serialize(
                    otherManifest,
                    new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)),
                TestContext.Current.CancellationToken);
        }

        var paths = new InstallPathResolver(layout);

        var inspector = new InstallationInspector(
            store,
            snapshots,
            cache,
            paths,
            NullLogger<InstallationInspector>.Instance);

        var service = new GameLaunchService(
            inspector,
            paths,
            new SystemGameProcessRunner(NullLogger<SystemGameProcessRunner>.Instance),
            NullLogger<GameLaunchService>.Instance);

        return new LaunchFixture(service, inspector, context);
    }

    /// <summary>
    /// Builds another live package of the SAME game, with its own version and its own
    /// launch configuration (AD-26).
    ///
    /// <para>
    /// El identificador del paquete es MENOR que el del paquete instalado, así que el
    /// servidor lo ordena ANTES en el snapshot, como ordena por identificador de
    /// paquete (AD-26).
    /// </para>
    /// </summary>
    /// <param name="version">Version that the other package publishes.</param>
    /// <param name="executablePath">Path of the executable that the other package declares.</param>
    /// <param name="arguments">Arguments that the other package declares.</param>
    /// <returns>The other package of the game.</returns>
    private static OtherPackage AnotherPackage(
        string version,
        string executablePath,
        IReadOnlyList<string> arguments) => new(
            $"paquete-00-{version}",
            version,
            executablePath,
            arguments);

    /// <summary>
    /// Opens the database of the client over the REAL SQLite file (AD-11, AD-16).
    /// </summary>
    /// <returns>The context of the database of the client.</returns>
    private async Task<ClientStateDbContext> OpenContextAsync()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Pooling = false,
        }.ToString();

        var options = new DbContextOptionsBuilder<ClientStateDbContext>()
            .UseSqlite(
                connectionString,
                sqlite => sqlite.MigrationsAssembly(typeof(ClientStateDbContext).Assembly.GetName().Name))
            .Options;

        await Task.CompletedTask;

        return new ClientStateDbContext(options);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_workingDirectory))
            {
                Directory.Delete(_workingDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
            // La limpieza del test NO puede fallar la prueba (AD-16).
        }
        catch (UnauthorizedAccessException)
        {
            // La limpieza del test NO puede fallar la prueba (AD-16).
        }
    }

    /// <summary>
    /// The fixture of the launch (AD-16).
    ///
    /// <para>
    /// El contexto de la base de datos es PARTE de la fixture: vive mientras vive la
    /// prueba, porque el store lo usa en cada lectura (AD-11, AD-16).
    /// </para>
    /// </summary>
    /// <param name="Service">The real case of use of the launch.</param>
    /// <param name="Context">The context of the database of the client.</param>
    private sealed record LaunchFixture(
        GameLaunchService Service,
        InstallationInspector Inspector,
        ClientStateDbContext Context) : IDisposable
    {
        public void Dispose() => Context.Dispose();
    }

    /// <summary>
    /// Another live package of the SAME game, with its own version and its own launch
    /// configuration (AD-26).
    ///
    /// <param name="PackageId">Identifier of the other package.</param>
    /// <param name="Version">Version that the other package publishes.</param>
    /// <param name="ExecutablePath">Path of the executable that the other package declares.</param>
    /// <param name="Arguments">Arguments that the other package declares.</param>
    private sealed record OtherPackage(
        string PackageId,
        string Version,
        string ExecutablePath,
        IReadOnlyList<string> Arguments);
}
