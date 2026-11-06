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
/// El test NO usa dobles del disco ni del proceso: el ejecutable es una copia REAL de
/// <c>cmd.exe</c> dentro de la raíz de instalación, y el <c>probe.cmd</c> que acompaña
/// registra los argumentos que recibió y el directorio de trabajo en el que corrió. Así
/// que lo que se observa es lo que de verdad pasa (AD-16).
/// </para>
/// <para>
/// El <c>cmd.exe</c> copiado es un INTERPRETE REAL: por eso el <c>probe.cmd</c> existe,
/// porque un <c>.cmd</c> necesita un intérprete que lo ejecute. La copia vive DENTRO de
/// la raíz de instalación, así que el confine de AD-9 se respeta de verdad (AD-9).
/// </para>
/// </summary>
public sealed class GameLaunchTests : IDisposable
{
    private const string GameId = "0193f2a1-b2c3-4d4e-5f60-718293a4b5c6";
    private const string PackageId = "paquete-lanzador-1";
    private const string Version = "1.0.0";

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
            "game\\cmd.exe",
            ["-novid", "-console", "mapa1"],
            runsProbe: true);

        var inspection = await fixture.Inspector
            .InspectAsync(GameId, TestContext.Current.CancellationToken);

        Assert.NotNull(inspection);
        Assert.Equal(
            ["/u", "/c", "game\\probe.cmd", "-novid", "-console", "mapa1"],
            inspection.LaunchSettings.Arguments);

        var outcome = await fixture.Service
            .LaunchAsync(GameId, TestContext.Current.CancellationToken);

        Assert.True(outcome.IsLaunched);

        // El proceso hijo tiene que TERMINAR para que el probe haya escrito sus archivos.
        await WaitForProbeAsync();

        // Los argumentos LLEGARON al hijo, en ORDEN (AC1).
        var arguments = await File.ReadAllTextAsync(
            Path.Combine(_installDirectory, "args.txt"),
            Encoding.Unicode,
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
            Encoding.Unicode,
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
            "..\\fuera\\cmd.exe",
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
        using var fixture = await CreateFixtureAsync("game\\cmd.exe", ["/c", "ping", "-n", "30", "127.0.0.1"]);

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
            "game\\cmd.exe",
            ["-novid"],
            certificationFails: true);

        var outcome = await fixture.Service
            .LaunchAsync(GameId, TestContext.Current.CancellationToken);

        Assert.True(outcome.IsRejected);
        Assert.Equal(ErrorCatalog.GameLaunchInstallationInvalidCode, outcome.Error!.Code);
        Assert.Null(outcome.Handle);
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
    /// <returns>The fixture of the launch.</returns>
    private async Task<LaunchFixture> CreateFixtureAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        bool createsExecutable = true,
        bool runsProbe = false,
        bool certificationFails = false)
    {
        var layout = ClientDataLayout.Create(_workingDirectory, _installDirectory);

        layout.EnsureCreated();

        // El INTERPRETE REAL se copia DENTRO de la raíz de instalación, así que el
        // confine de AD-9 se respeta de verdad (AD-9).
        var executable = Path.Combine(_installDirectory, executablePath);

        Directory.CreateDirectory(Path.GetDirectoryName(executable)!);

        var interpreter = Path.Combine(
            Environment.SystemDirectory,
            "cmd.exe");

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
            var probe = Path.Combine(Path.GetDirectoryName(executable)!, "probe.cmd");

            await File.WriteAllTextAsync(
                probe,
                new StringBuilder()
                    // El script se interpreta con la página OEM de cmd.exe: escribir
                    // rutas absolutas UTF-8 falla cuando el perfil de Windows contiene
                    // caracteres no ASCII. El directorio de trabajo ya es la raíz que
                    // se verifica, así que las salidas relativas son deliberadas.
                    .AppendLine("echo %* > args.txt")
                    .AppendLine("cd > cwd.txt")
                .ToString(),
                TestContext.Current.CancellationToken);

            probePath = Path.GetRelativePath(_installDirectory, probe);
        }

        IReadOnlyList<string> launchArguments = runsProbe
            // `/u` hace que los redireccionamientos de cmd.exe conserven la ruta de
            // trabajo Unicode; así la prueba no depende de la página OEM del perfil.
            ? ["/u", "/c", probePath!, ..arguments]
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
            [new PackageDescriptor(PackageId, GameId, Version, 1, 0, new string('a', 64), PackageId)]);

        var snapshots = new CatalogSnapshotStore(context);

        await snapshots.SaveAsync(snapshot, TestContext.Current.CancellationToken);

        var cache = new ManifestCache(layout);

        await cache.StoreAsync(
            PackageId,
            System.Text.Json.JsonSerializer.Serialize(
                manifest,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)),
            TestContext.Current.CancellationToken);

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
}
