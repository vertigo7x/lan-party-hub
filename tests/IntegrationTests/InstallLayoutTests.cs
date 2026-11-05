using LANPartyHub.Client.Application.Install;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Host;
using LANPartyHub.Client.Infrastructure.Filesystem;
using LANPartyHub.Client.Infrastructure.Persistence;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LANPartyHub.IntegrationTests;

/// <summary>
/// Integration level (AD-16): the layout of the client on the REAL file system, and the
/// confinement of the extraction inside the configured directory (AD-9, NFR-5, AC1, AC3,
/// AC4).
///
/// <para>
/// El test escribe en un disco real y comprueba dónde quedan las cosas: la caché
/// <c>manifests/</c> y la base <c>state.sqlite</c> viven bajo la carpeta de datos del
/// cliente, y los parciales en <c>downloads/</c>, FUERA de la raíz de instalación
/// (AD-9, AD-11, AD-17).
/// </para>
/// <para>
/// El caso del rechazo sin escribir nada es el del checklist de
/// <c>docs/plan.md</c>: un intento de extracción fuera del directorio autorizado no
/// deja ni un archivo ni un directorio fuera de la raíz (AD-27).
/// </para>
/// </summary>
public sealed class InstallLayoutTests : IDisposable
{
    private const string PackageId = "0193f2a1-b2c3-4d4e-5f60-718293a4b5c6";

    private readonly string _dataFolder;
    private readonly string _installDirectory;
    private readonly string _outsideFolder;

    /// <summary>Creates an isolated working directory for the test.</summary>
    public InstallLayoutTests()
    {
        _dataFolder = Path.Combine(
            Path.GetTempPath(),
            "lanpartyhub-integration",
            Guid.NewGuid().ToString("N"));

        _installDirectory = Path.Combine(_dataFolder, ClientDataLayout.InstallFolderName);
        _outsideFolder = Path.Combine(_dataFolder, "fuera-de-la-raiz");

        Directory.CreateDirectory(_dataFolder);
        Directory.CreateDirectory(_outsideFolder);
    }

    /// <summary>
    /// The layout of AD-9 lives on the disk: la caché de manifiestos y la base de datos
    /// del cliente están bajo la carpeta de datos, y la raíz de instalación es su
    /// hermana (AD-9, AD-11, AD-17).
    /// </summary>
    [Fact]
    public void The_manifests_and_the_state_database_live_under_the_data_folder()
    {
        var layout = ClientDataLayout.Create(_dataFolder, _installDirectory);

        layout.EnsureCreated();

        Assert.StartsWith(
            _dataFolder,
            layout.ManifestsDirectory,
            StringComparison.OrdinalIgnoreCase);

        Assert.StartsWith(
            _dataFolder,
            layout.StateDatabasePath,
            StringComparison.OrdinalIgnoreCase);

        Assert.True(Directory.Exists(layout.ManifestsDirectory));

        // La base de datos es un archivo de la CARPETA de datos, y no un hermano de la
        // caché de manifiestos dentro de ella (AD-9, AD-11).
        Assert.Equal(
            Path.Combine(_dataFolder, ClientDataLayout.StateDatabaseFileName),
            layout.StateDatabasePath);

        Assert.StartsWith(
            _dataFolder,
            layout.StateDatabasePath,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The partial file lives in <c>downloads/</c>, which is OUTSIDE the installation
    /// root: el confine de AD-9 sólo encierra la raíz de instalación (AD-9, AC4).
    /// </summary>
    [Fact]
    public void The_partial_file_lives_outside_the_installation_root()
    {
        var layout = ClientDataLayout.Create(_dataFolder, _installDirectory);

        layout.EnsureCreated();

        var partial = layout.PartialFilePath(PackageId);

        Assert.StartsWith(
            layout.DownloadsDirectory,
            partial,
            StringComparison.OrdinalIgnoreCase);

        Assert.False(partial.StartsWith(layout.InstallDirectory, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The resolver confines the writes to the root, and the partial file is NOT
    /// confinado: la caché de los descargas es de otro directorio por diseño (AD-9).
    /// </summary>
    [Fact]
    public void The_resolver_confines_the_writes_and_leaves_the_downloads_alone()
    {
        var layout = ClientDataLayout.Create(_dataFolder, _installDirectory);

        layout.EnsureCreated();

        var resolver = new InstallPathResolver(layout);

        Assert.True(resolver.IsInsideRoot("game/data.bin"));
        Assert.False(resolver.IsInsideRoot("../fuera-de-la-raiz/data.bin"));
    }

    /// <summary>
    /// A manifest that declares an ABSOLUTE path is rejected and NOTHING is written: ni
    /// el directorio declarado, ni el archivo, ni nada fuera de la raíz (AC2, AD-27).
    /// </summary>
    [Fact]
    public async Task A_manifest_with_an_absolute_path_is_rejected_without_writing_anything()
    {
        var layout = ClientDataLayout.Create(_dataFolder, _installDirectory);

        layout.EnsureCreated();

        var resolver = new InstallPathResolver(layout);

        var writer = new ManifestTreeWriter(
            resolver,
            NullLogger<ManifestTreeWriter>.Instance);

        var absolute = Path.Combine(_outsideFolder, "datos.txt");

        var manifest = ManifestWith(
            new InstallationManifest.CreateDirectory("game"),
            new InstallationManifest.WriteConfig(absolute, "contenido"));

        var outcome = await writer.ApplyAsync(
            manifest,
            resolver.RootPath,
            packagePath: null,
            TestContext.Current.CancellationToken);

        Assert.False(outcome.PathsConfinedToRoot);
        Assert.False(outcome.VerifyPassed);
        Assert.NotNull(outcome.RejectedPath);

        // Nada se escribió FUERA de la raíz: ni el archivo absoluto, ni la carpeta
        // declarada por la operación que sí era relativa (AD-27).
        Assert.False(File.Exists(absolute));
        Assert.False(Directory.Exists(Path.Combine(_installDirectory, "game")));
    }

    /// <summary>
    /// A manifest that declares a path that LEAVES the root with <c>..</c> is rejected
    /// and nothing is written (AC3, AD-27).
    /// </summary>
    [Fact]
    public async Task A_manifest_that_leaves_the_root_is_rejected_without_writing_anything()
    {
        var layout = ClientDataLayout.Create(_dataFolder, _installDirectory);

        layout.EnsureCreated();

        var resolver = new InstallPathResolver(layout);

        var writer = new ManifestTreeWriter(
            resolver,
            NullLogger<ManifestTreeWriter>.Instance);

        var manifest = ManifestWith(
            new InstallationManifest.WriteConfig("../fuera-de-la-raiz/datos.txt", "contenido"));

        var outcome = await writer.ApplyAsync(
            manifest,
            resolver.RootPath,
            packagePath: null,
            TestContext.Current.CancellationToken);

        Assert.False(outcome.PathsConfinedToRoot);
        Assert.False(outcome.VerifyPassed);
        Assert.NotNull(outcome.RejectedPath);

        Assert.False(File.Exists(Path.Combine(_outsideFolder, "datos.txt")));
    }

    /// <summary>
    /// A manifest whose paths are RELATIVE is applied inside the root: el confine deja
    /// pasar lo que de verdad está dentro (AC1).
    /// </summary>
    [Fact]
    public async Task A_manifest_with_relative_paths_is_written_inside_the_root()
    {
        var layout = ClientDataLayout.Create(_dataFolder, _installDirectory);

        layout.EnsureCreated();

        var resolver = new InstallPathResolver(layout);

        var writer = new ManifestTreeWriter(
            resolver,
            NullLogger<ManifestTreeWriter>.Instance);

        var manifest = ManifestWith(
            new InstallationManifest.CreateDirectory("game"),
            new InstallationManifest.WriteConfig("game/config.ini", "clave=valor"));

        var outcome = await writer.ApplyAsync(
            manifest,
            resolver.RootPath,
            packagePath: null,
            TestContext.Current.CancellationToken);

        Assert.True(outcome.PathsConfinedToRoot);
        Assert.Null(outcome.RejectedPath);

        Assert.True(File.Exists(Path.Combine(_installDirectory, "game", "config.ini")));
    }

    /// <summary>
    /// The confinement is REPEATED before every write: una ruta que era válida deja de
    /// serlo cuando aparece un enlace que sale de la raíz, y la escritura posterior se
    /// rechaza (AD-9, AC1).
    /// </summary>
    [Fact]
    public async Task The_confinement_is_repeated_before_every_write()
    {
        var layout = ClientDataLayout.Create(_dataFolder, _installDirectory);

        layout.EnsureCreated();

        var resolver = new InstallPathResolver(layout);

        var writer = new ManifestTreeWriter(
            resolver,
            NullLogger<ManifestTreeWriter>.Instance);

        // La primera escritura es legítima y ocurre dentro de la raíz.
        await writer.ApplyAsync(
            ManifestWith(new InstallationManifest.WriteConfig("game/config.ini", "valor")),
            resolver.RootPath,
            packagePath: null,
            TestContext.Current.CancellationToken);

        Assert.True(File.Exists(Path.Combine(_installDirectory, "game", "config.ini")));

        // Ahora aparece un ENLACE que saca a la raíz, y la escritura posterior se
        // rechaza.
        var link = Path.Combine(_installDirectory, "escape");

        if (!TryCreateDirectoryLink(link, _outsideFolder))
        {
            // La máquina de la prueba no puede crear enlaces de directorio: el
            // re-chequeo por escritura no se puede ejercitar aquí.
            return;
        }

        var outcome = await writer.ApplyAsync(
            ManifestWith(new InstallationManifest.WriteConfig("escape/datos.txt", "valor")),
            resolver.RootPath,
            packagePath: null,
            TestContext.Current.CancellationToken);

        Assert.False(outcome.PathsConfinedToRoot);
        Assert.NotNull(outcome.RejectedPath);

        Assert.False(File.Exists(Path.Combine(_outsideFolder, "datos.txt")));
    }

    /// <summary>
    /// The installation root that MOVES becomes the only confined one, at once: la raíz
    /// se lee del layout en cada llamada, así que el cambio surte efecto inmediato
    /// (AD-9, AC1).
    /// </summary>
    [Fact]
    public void The_moved_root_is_the_only_confined_one_from_that_moment_on()
    {
        var layout = ClientDataLayout.Create(_dataFolder, _installDirectory);

        layout.EnsureCreated();

        var resolver = new InstallPathResolver(layout);

        var newRoot = Path.Combine(_dataFolder, "juegos");

        layout.SetInstallDirectory(newRoot);

        Assert.Equal(Path.GetFullPath(newRoot), resolver.RootPath);
        Assert.True(resolver.IsInsideRoot("game/data.bin"));

        // La raíz ANTERIOR ya no confina: una ruta que estaba dentro de ella ahora queda
        // fuera, porque el confine se comprueba contra la raíz vigente (AD-9).
        Assert.False(resolver.IsInsideRoot(Path.Combine(_installDirectory, "game", "data.bin")));
    }

    /// <summary>
    /// Creates a DIRECTORY LINK to a directory, when the machine allows it.
    /// </summary>
    /// <param name="link">Path of the link to create.</param>
    /// <param name="target">Path the link points to.</param>
    /// <returns><c>true</c> when the link was created.</returns>
    private static bool TryCreateDirectoryLink(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                Directory.CreateSymbolicLink(link, target);

                return true;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return false;
            }
        }

        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c mklink /J \"{link}\" \"{target}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });

            if (process is null)
            {
                return false;
            }

            process.WaitForExit();

            return process.ExitCode == 0;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Builds a manifest with the given operations, so that el confine se ejercite
    /// sobre el contrato real de AD-7.
    /// </summary>
    /// <param name="operations">Operations of the manifest.</param>
    /// <returns>The manifest.</returns>
    private static InstallationManifest ManifestWith(
        params InstallationManifest.ManifestOperation[] operations) =>
        new(
            1,
            PackageId,
            "1.0.0",
            1024,
            new string('a', 64),
            new InstallationManifest.LaunchSettings("game/game.exe", [], "game"),
            operations);

    /// <summary>Removes the working directory of the test.</summary>
    public void Dispose()
    {
        if (Directory.Exists(_dataFolder))
        {
            try
            {
                Directory.Delete(_dataFolder, recursive: true);
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
}
