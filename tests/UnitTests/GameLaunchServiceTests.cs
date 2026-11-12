using LANPartyHub.Client.Application.GameLauncher;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Domain.Install;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The launch of a videogame (AD-19, FR-9, AC1, AC2, AC5).
///
/// <para>
/// Los tests fijan el ORDEN NORMATIVO del lanzamiento: la instalación se CERTIFICA
/// ANTES de lanzar, el ejecutable se confina a la raíz de instalación y cada fallo
/// produce un error del catálogo con su causa y la RUTA EXACTA (AD-19, AC1, AD-9).
/// </para>
/// <para>
/// El nivel de integración —con un ejecutable REAL en disco— vive en
/// <c>GameLaunchTests</c> (AD-16, epics.md 8.1).
/// </para>
/// </summary>
public sealed class GameLaunchServiceTests : IDisposable
{
    private const string GameId = "0193f2a1b2c34d4e5f60718293a4b5c6";

    private readonly List<string> _temporaryFolders = [];

    /// <summary>
    /// AC1: an installation that is NOT registered does NOT start any process, and the
    /// error says so (AD-19, AC1).
    /// </summary>
    [Fact]
    public async Task A_game_without_installation_starts_no_process()
    {
        var runner = new RecordingRunner();
        var service = CreateService(runner, inspection: null);

        var outcome = await service.LaunchAsync(GameId, TestContext.Current.CancellationToken);

        Assert.True(outcome.IsRejected);
        Assert.False(outcome.IsLaunched);
        Assert.Equal(ErrorCatalog.GameLaunchInstallationInvalidCode, outcome.Error!.Code);
        Assert.Equal(0, runner.StartCount);
    }

    /// <summary>
    /// AC1: an installation that is NOT <c>Installed</c> does NOT start any process
    /// (AD-19, AC1, AC5).
    /// </summary>
    [Theory]
    [InlineData(InstallationState.Repairable)]
    [InlineData(InstallationState.Failed)]
    [InlineData(InstallationState.Installing)]
    [InlineData(InstallationState.Pending)]
    public async Task An_installation_that_is_not_installed_starts_no_process(InstallationState state)
    {
        var runner = new RecordingRunner();
        var service = CreateService(runner, CreateInspection(state));

        var outcome = await service.LaunchAsync(GameId, TestContext.Current.CancellationToken);

        Assert.True(outcome.IsRejected);
        Assert.Equal(ErrorCatalog.GameLaunchInstallationInvalidCode, outcome.Error!.Code);
        Assert.Equal(0, runner.StartCount);
    }

    /// <summary>
    /// AC1: a MISSING executable prevents the launch and the error names its EXACT path
    /// (AD-19, AC1, AD-9).
    /// </summary>
    [Fact]
    public async Task A_missing_executable_names_its_exact_path()
    {
        var root = CreateTemporaryFolder();
        var runner = new RecordingRunner();
        var service = CreateService(
            runner,
            CreateInspection(InstallationState.Installed, HostPath("game", "bin", "ausente.exe"), root: root));

        var outcome = await service.LaunchAsync(GameId, TestContext.Current.CancellationToken);

        Assert.True(outcome.IsRejected);
        Assert.Equal(ErrorCatalog.GameLaunchExecutableMissingCode, outcome.Error!.Code);
        Assert.Equal(0, runner.StartCount);

        // El diagnóstico NOMBRA la ruta exacta del ejecutable que falta (AC1).
        var expected = Path.Combine(root, "game", "bin", "ausente.exe");

        Assert.Contains(expected, outcome.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// AC1, AD-9: an executable that resolves OUTSIDE the installation root is REJECTED
    /// and no process is started (AD-9, AC1).
    /// </summary>
    [Fact]
    public async Task An_executable_outside_the_root_is_rejected()
    {
        var root = CreateTemporaryFolder();
        var runner = new RecordingRunner();
        var service = CreateService(
            runner,
            CreateInspection(InstallationState.Installed, HostPath("..", "fuera", "juego.exe"), root: root));

        var outcome = await service.LaunchAsync(GameId, TestContext.Current.CancellationToken);

        Assert.True(outcome.IsRejected);
        Assert.Equal(ErrorCatalog.GameLaunchPathOutsideRootCode, outcome.Error!.Code);
        Assert.Equal(0, runner.StartCount);
    }

    /// <summary>
    /// AC1, AD-9: a working directory that resolves OUTSIDE the installation root is
    /// REJECTED and no process is started (AD-9, AC1).
    /// </summary>
    [Fact]
    public async Task A_working_directory_outside_the_root_is_rejected()
    {
        var root = CreateTemporaryFolder();
        CreateExecutable(root, HostPath("game", "juego.exe"));

        var runner = new RecordingRunner();
        var service = CreateService(
            runner,
            CreateInstallation(
                InstallationState.Installed,
                HostPath("game", "juego.exe"),
                root,
                workingDirectory: HostPath("..", "fuera")));

        var outcome = await service.LaunchAsync(GameId, TestContext.Current.CancellationToken);

        Assert.True(outcome.IsRejected);
        Assert.Equal(ErrorCatalog.GameLaunchPathOutsideRootCode, outcome.Error!.Code);
        Assert.Equal(0, runner.StartCount);
    }

    /// <summary>
    /// AC1: the arguments reach the runner IN ORDER and the working directory is the one
    /// that the manifest declared (AD-19, AC1).
    /// </summary>
    [Fact]
    public async Task The_arguments_arrive_in_order_with_the_working_directory()
    {
        var root = CreateTemporaryFolder();
        var executable = CreateExecutable(root, HostPath("game", "juego.exe"));

        var runner = new RecordingRunner();
        var service = CreateService(
            runner,
            CreateInspection(
                InstallationState.Installed,
                HostPath("game", "juego.exe"),
                root: root,
                arguments: ["-novid", "-console", "mapa1"],
                workingDirectory: HostPath("game", "bin")));

        var outcome = await service.LaunchAsync(GameId, TestContext.Current.CancellationToken);

        Assert.True(outcome.IsLaunched);
        Assert.Equal(1, runner.StartCount);

        Assert.Equal(executable, runner.ExecutablePath);
        Assert.Equal(["-novid", "-console", "mapa1"], runner.Arguments);
        Assert.Equal(Path.Combine(root, "game", "bin"), runner.WorkingDirectory);
    }

    /// <summary>
    /// AC1: the executable is resolved against the ROOT of the installation, and never
    /// outside of it (AD-9, AC1).
    /// </summary>
    [Fact]
    public async Task The_executable_is_resolved_against_the_installation_root()
    {
        var root = CreateTemporaryFolder();
        var executable = CreateExecutable(root, HostPath("game", "juego.exe"));

        var runner = new RecordingRunner();
        var service = CreateService(
            runner,
            CreateInspection(InstallationState.Installed, HostPath("game", "juego.exe"), root: root));

        var outcome = await service.LaunchAsync(GameId, TestContext.Current.CancellationToken);

        Assert.True(outcome.IsLaunched);
        Assert.Equal(executable, runner.ExecutablePath);
        Assert.StartsWith(root, runner.ExecutablePath!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// AC1: the launch is CERTIFIED BEFORE the process is started, so a launch that is
    /// rejected never reaches the runner (AD-19, AC1).
    /// </summary>
    [Fact]
    public async Task The_installation_is_certified_before_the_process_is_started()
    {
        var root = CreateTemporaryFolder();
        CreateExecutable(root, HostPath("game", "juego.exe"));

        var inspector = new RecordingInspector(CreateInspection(InstallationState.Installed, HostPath("game", "juego.exe"), root: root));
        var runner = new RecordingRunner();
        var service = new GameLaunchService(
            inspector,
            new StubPathResolver(root),
            runner,
            NullLogger<GameLaunchService>.Instance);

        await service.LaunchAsync(GameId, TestContext.Current.CancellationToken);

        Assert.Equal(1, inspector.InspectCount);
        Assert.Equal(1, runner.StartCount);
    }

    /// <summary>
    /// AC1: a process that does NOT start produces the error of the catalog and no
    /// process is left behind (AD-19, AC1, AD-15).
    /// </summary>
    [Fact]
    public async Task A_process_that_does_not_start_is_rejected()
    {
        var root = CreateTemporaryFolder();
        CreateExecutable(root, HostPath("game", "juego.exe"));

        var runner = new RecordingRunner { FailOnStart = true };
        var service = CreateService(
            runner,
            CreateInstallation(InstallationState.Installed, HostPath("game", "juego.exe"), root: root));

        var outcome = await service.LaunchAsync(GameId, TestContext.Current.CancellationToken);

        Assert.True(outcome.IsRejected);
        Assert.Equal(ErrorCatalog.GameLaunchProcessFailedCode, outcome.Error!.Code);
        Assert.Null(outcome.Handle);
    }

    /// <summary>
    /// AC2: the launch hands over the handle of the process, so the client can tell
    /// whether the game is still running (AD-19, AC2).
    /// </summary>
    [Fact]
    public async Task A_launched_game_hands_over_the_handle_of_its_process()
    {
        var root = CreateTemporaryFolder();
        CreateExecutable(root, HostPath("game", "juego.exe"));

        var runner = new RecordingRunner();
        var service = CreateService(
            runner,
            CreateInstallation(InstallationState.Installed, HostPath("game", "juego.exe"), root: root));

        var outcome = await service.LaunchAsync(GameId, TestContext.Current.CancellationToken);

        Assert.True(outcome.IsLaunched);
        Assert.NotNull(outcome.Handle);
        Assert.True(outcome.Handle!.IsRunning);
    }

    /// <summary>
    /// AC4, UX-DR-43: the client does NOT declare global keyboard hooks, así que no
    /// captura teclas mientras el juego está en ejecución (AC4).
    /// </summary>
    [Fact]
    public void The_client_declares_no_global_keyboard_hook()
    {
        var root = RepositoryLayout.Root;

        var source = File.ReadAllText(
            Path.Combine(root, "src", "Client", "Infrastructure", "Process", "SystemGameProcessRunner.cs"));

        foreach (var forbidden in new[]
        {
            "SetWindowsHookEx",
            "RegisterHotKey",
            "GetAsyncKeyState",
            "RawInput",
            "KeyboardHook",
        })
        {
            Assert.DoesNotContain(forbidden, source, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// AC5, AD-19: the launcher module uses the <c>IGameProcessRunner</c> interface, y
    /// la conexión directa a servidores de juego NO está implementada (AD-19, AC5).
    /// </summary>
    [Fact]
    public void The_launcher_uses_the_port_and_does_not_implement_the_direct_connection()
    {
        var root = RepositoryLayout.Root;

        var service = File.ReadAllText(
            Path.Combine(root, "src", "Client", "Application", "GameLauncher", "GameLaunchService.cs"));

        Assert.Contains("IGameProcessRunner", service, StringComparison.Ordinal);

        var outcome = File.ReadAllText(
            Path.Combine(root, "src", "Client", "Application", "GameLauncher", "GameLaunchOutcome.cs"));

        Assert.Contains("IGameProcessHandle", outcome, StringComparison.Ordinal);

        // La conexión directa a servidores de juego y los launchers externos quedan NO
        // implementados: el puerto no los expone (AD-19, AC5).
        foreach (var forbidden in new[] { "Steam", "DirectConnect", "GameServer" })
        {
            Assert.DoesNotContain(forbidden, service, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Creates the use case of the launch over the given doubles.
    /// </summary>
    /// <param name="runner">Double of the port of the run.</param>
    /// <param name="inspection">Inspection that the double of the port returns.</param>
    /// <param name="inspector">Double of the port of the inspection, when it is given.</param>
    /// <returns>The use case of the launch.</returns>
    private static GameLaunchService CreateService(
        RecordingRunner runner,
        InstallationInspection? inspection,
        RecordingInspector? inspector = null) =>
        new(
            inspector ?? new RecordingInspector(inspection),
            new StubPathResolver(inspection?.RootPath ?? Path.GetTempPath()),
            runner,
            NullLogger<GameLaunchService>.Instance);

    /// <summary>
    /// Creates an inspection of an installation that is certified as valid.
    /// </summary>
    /// <param name="state">State of the installation.</param>
    /// <param name="executablePath">Path of the executable, relative to the root.</param>
    /// <param name="root">Root of the installation.</param>
    /// <param name="arguments">Arguments of the launch.</param>
    /// <param name="workingDirectory">Working directory of the launch, relative to the root.</param>
    /// <returns>The inspection of the installation.</returns>
    private static InstallationInspection CreateInstallation(
        InstallationState state,
        string executablePath,
        string root,
        IReadOnlyList<string>? arguments = null,
        string workingDirectory = "") =>
        new(
            root,
            InstallationCertification.Certify(
                packageValidatedAgainstAnchor: true,
                manifestVerifyPassed: true,
                pathsConfinedToRoot: true,
                repairablePackageAvailable: false) with { State = state },
            new InstallationManifest.LaunchSettings(
                executablePath,
                arguments ?? [],
                workingDirectory));

    /// <summary>
    /// Creates an inspection of an installation in the given state.
    /// </summary>
    /// <param name="state">State of the installation.</param>
    /// <param name="executablePath">Path of the executable, relative to the root.</param>
    /// <param name="root">Root of the installation.</param>
    /// <param name="arguments">Arguments of the launch.</param>
    /// <param name="workingDirectory">Working directory of the launch, relative to the root.</param>
    /// <returns>The inspection of the installation.</returns>
    private static InstallationInspection CreateInspection(
        InstallationState state,
        string executablePath = "",
        string? root = null,
        IReadOnlyList<string>? arguments = null,
        string workingDirectory = "") =>
        CreateInstallation(
            state,
            executablePath.Length == 0 ? HostPath("game", "juego.exe") : executablePath,
            root ?? Path.GetTempPath(),
            arguments,
            workingDirectory);

    /// <summary>
    /// Creates a temporary folder that the test cleans up.
    /// </summary>
    /// <returns>The path of the folder.</returns>
    private string CreateTemporaryFolder()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "lanpartyhub-gamelaunch",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(path);

        _temporaryFolders.Add(path);

        return path;
    }

    /// <summary>
    /// Creates an executable file inside the root of the installation.
    /// </summary>
    /// <param name="root">Root of the installation.</param>
    /// <param name="relativePath">Path of the executable, relative to the root.</param>
    /// <returns>The absolute path of the executable.</returns>
    /// <summary>
    /// A relative path in the FORM of the host, so the rule is exercised the same
    /// way everywhere (AD-9).
    /// </summary>
    /// <param name="segments">Segments of the path, separated by the host separator.</param>
    /// <returns>The relative path, in the form of the host.</returns>
    private static string HostPath(params string[] segments) =>
        string.Join(Path.DirectorySeparatorChar, segments);

    private static string CreateExecutable(string root, string relativePath)
    {
        var path = Path.Combine(root, relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        File.WriteAllText(path, "contenido");

        return path;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var folder in _temporaryFolders)
        {
            try
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, recursive: true);
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
    }

    /// <summary>
    /// Double of the port of the inspection of an installation (AD-19, AD-16).
    /// </summary>
    private sealed class RecordingInspector : IInstallationInspector
    {
        private readonly InstallationInspection? _inspection;

        public RecordingInspector(InstallationInspection? inspection) => _inspection = inspection;

        public int InspectCount { get; private set; }

        public string GameRootPath(string gameId) => _inspection?.RootPath ?? string.Empty;

        public Task<InstallationInspection?> InspectAsync(string gameId, CancellationToken cancellationToken)
        {
            InspectCount++;

            return Task.FromResult(_inspection);
        }
    }

    /// <summary>
    /// Double of the port of the run of the videogame process (AD-19, AD-16).
    /// </summary>
    private sealed class RecordingRunner : IGameProcessRunner
    {
        public int StartCount { get; private set; }

        public string? ExecutablePath { get; private set; }

        public IReadOnlyList<string> Arguments { get; private set; } = [];

        public string? WorkingDirectory { get; private set; }

        public bool FailOnStart { get; init; }

        public Task<IGameProcessHandle> StartAsync(
            string executablePath,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            CancellationToken cancellationToken)
        {
            StartCount++;

            if (FailOnStart)
            {
                throw new InvalidOperationException("El proceso no arrancó.");
            }

            ExecutablePath = executablePath;
            Arguments = arguments;
            WorkingDirectory = workingDirectory;

            return Task.FromResult<IGameProcessHandle>(new StubProcessHandle());
        }
    }

    /// <summary>
    /// Double of the handle of a process (AD-19, AD-16).
    /// </summary>
    private sealed class StubProcessHandle : IGameProcessHandle
    {
        public int ProcessId => 4242;

        public bool IsRunning => true;
    }

    /// <summary>
    /// Double of the resolver of the paths of the installation (AD-9, AD-16).
    ///
    /// <para>
    /// El doble resuelve contra la RAÍZ que le declaran las pruebas, así que la ruta que
    /// sale de la raíz se rechaza con su ruta canónica, como el resolvedor real (AD-9).
    /// </para>
    /// </summary>
    private sealed class StubPathResolver : IInstallPathResolver
    {
        public StubPathResolver(string root) => RootPath = root;

        public string RootPath { get; }

        public string GameRootPath(string gameId) => RootPath;

        public string ResolveInsideGameRoot(string gameId, string relativePath) => ResolveInside(RootPath, relativePath);

        public string ResolveInside(string rootPath, string relativePath)
        {
            var candidate = Path.GetFullPath(Path.Combine(rootPath, relativePath));

            if (!candidate.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InstallPathOutsideRootException(relativePath, candidate, rootPath);
            }

            return candidate;
        }

        public bool IsInsideGameRoot(string gameId, string relativePath) => IsInside(RootPath, relativePath);

        public bool IsInside(string rootPath, string relativePath)
        {
            try
            {
                ResolveInside(rootPath, relativePath);

                return true;
            }
            catch (InstallPathOutsideRootException)
            {
                return false;
            }
        }
    }
}
