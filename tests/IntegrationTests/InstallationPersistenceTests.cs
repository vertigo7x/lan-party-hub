using LANPartyHub.Client.Application;
using LANPartyHub.Client.Domain.Install;
using LANPartyHub.Client.Infrastructure.Persistence;
using LANPartyHub.Shared.Contracts;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LANPartyHub.IntegrationTests;

/// <summary>
/// Integration level (AD-16): the installation of a videogame is persisted in a REAL
/// SQLite file, and the identifier of the installation root is STABLE against a repair
/// and against the restart of the client (AD-22, AC1).
///
/// <para>
/// El test escribe en un archivo de disco, cierra la conexión y lee los datos de vuelta
/// con otra conexión, así que observa lo que se persistió DE VERDAD y no lo que una
/// base de datos en memoria conservaría.
/// </para>
/// </summary>
public sealed class InstallationPersistenceTests : IDisposable
{
    private const string GameId = "0193f2a1-b2c3-4d4e-5f60-718293a4b5c6";
    private const string Version = "1.0.0";

    private readonly string _workingDirectory;
    private readonly string _databasePath;
    private readonly string _installDirectory;

    /// <summary>Creates an isolated working directory for the test.</summary>
    public InstallationPersistenceTests()
    {
        _workingDirectory = Path.Combine(Path.GetTempPath(), "lanpartyhub-integration", Guid.NewGuid().ToString("N"));
        _databasePath = Path.Combine(_workingDirectory, "state.sqlite");
        _installDirectory = Path.Combine(_workingDirectory, "install");

        Directory.CreateDirectory(_workingDirectory);
    }

    /// <summary>Removes the working directory of the test.</summary>
    public void Dispose()
    {
        if (Directory.Exists(_workingDirectory))
        {
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
    }

    /// <summary>
    /// The installation is persisted in a real SQLite file and read back with another
    /// connection (AD-11, AD-22).
    /// </summary>
    [Fact]
    public async Task The_installation_is_persisted_in_a_real_sqlite_file()
    {
        var installDirectoryId = InstallationCertification.DeriveInstallDirectoryId(_installDirectory);

        await using (var context = await OpenContextAsync())
        {
            var store = new ClientStateStore(context);

            await store.MigrateAsync(TestContext.Current.CancellationToken);

            await store.SaveInstallationAsync(
                new ClientInstallation(
                    GameId,
                    Version,
                    InstallationState.Installed,
                    _installDirectory,
                    installDirectoryId),
                TestContext.Current.CancellationToken);
        }

        Assert.True(File.Exists(_databasePath));

        await using var reopenedContext = await OpenContextAsync();

        var reopened = new ClientStateStore(reopenedContext);

        var installation = await reopened.LoadInstallationAsync(
            GameId,
            TestContext.Current.CancellationToken);

        Assert.NotNull(installation);
        Assert.Equal(GameId, installation!.GameId);
        Assert.Equal(Version, installation.InstalledVersion);
        Assert.Equal(InstallationState.Installed, installation.ValidationState);
        Assert.Equal(_installDirectory, installation.InstallPath);
        Assert.Equal(installDirectoryId, installation.InstallDirectoryId);
    }

    /// <summary>
    /// The identifier of the installation root is STABLE against a repair: the row that
    /// the repair updates keeps the identifier that it already had (AC1).
    /// </summary>
    [Fact]
    public async Task The_identifier_of_the_root_is_stable_against_a_repair()
    {
        var installDirectoryId = InstallationCertification.DeriveInstallDirectoryId(_installDirectory);

        await using (var context = await OpenContextAsync())
        {
            var store = new ClientStateStore(context);

            await store.MigrateAsync(TestContext.Current.CancellationToken);

            await store.SaveInstallationAsync(
                new ClientInstallation(
                    GameId,
                    Version,
                    InstallationState.Installed,
                    _installDirectory,
                    installDirectoryId),
                TestContext.Current.CancellationToken);
        }

        // La reparación cambia el estado de la instalación a `Repairable` y vuelve a
        // guardar la fila (AC6).
        await using (var context = await OpenContextAsync())
        {
            var store = new ClientStateStore(context);

            await store.SaveInstallationAsync(
                new ClientInstallation(
                    GameId,
                    Version,
                    InstallationState.Repairable,
                    _installDirectory,
                    installDirectoryId),
                TestContext.Current.CancellationToken);
        }

        await using var reopenedContext = await OpenContextAsync();

        var reopened = new ClientStateStore(reopenedContext);

        var installation = await reopened.LoadInstallationAsync(
            GameId,
            TestContext.Current.CancellationToken);

        Assert.NotNull(installation);
        Assert.Equal(InstallationState.Repairable, installation!.ValidationState);

        // AC1: el identificador NO cambia ante una reparación.
        Assert.Equal(installDirectoryId, installation.InstallDirectoryId);
    }

    /// <summary>
    /// The identifier of the installation root is STABLE against the restart of the
    /// client: the row survives the closing of la conexión y se lee igual (AC1).
    /// </summary>
    [Fact]
    public async Task The_identifier_of_the_root_is_stable_against_the_restart_of_the_client()
    {
        var installDirectoryId = InstallationCertification.DeriveInstallDirectoryId(_installDirectory);

        await using (var context = await OpenContextAsync())
        {
            var store = new ClientStateStore(context);

            await store.MigrateAsync(TestContext.Current.CancellationToken);

            await store.SaveInstallationAsync(
                new ClientInstallation(
                    GameId,
                    Version,
                    InstallationState.Installed,
                    _installDirectory,
                    installDirectoryId),
                TestContext.Current.CancellationToken);
        }

        // El cliente arranca de nuevo: otra conexión, el mismo archivo.
        await using var restartedContext = await OpenContextAsync();

        var restarted = new ClientStateStore(restartedContext);

        var installation = await restarted.LoadInstallationAsync(
            GameId,
            TestContext.Current.CancellationToken);

        Assert.NotNull(installation);
        Assert.Equal(installDirectoryId, installation!.InstallDirectoryId);
    }

    /// <summary>
    /// The KEY of the row is the <c>gameId</c>, así que una segunda instalación del
    /// MISMO videojuego actualiza la fila que ya existe en lugar de crear otra
    /// (AD-22).
    /// </summary>
    [Fact]
    public async Task The_key_of_the_row_is_the_game_id()
    {
        await using (var storeContext = await OpenContextAsync())
        {
            var store = new ClientStateStore(storeContext);

            await store.MigrateAsync(TestContext.Current.CancellationToken);

            await store.SaveInstallationAsync(
                new ClientInstallation(
                    GameId,
                    Version,
                    InstallationState.Installed,
                    _installDirectory,
                    InstallationCertification.DeriveInstallDirectoryId(_installDirectory)),
                TestContext.Current.CancellationToken);

            await store.SaveInstallationAsync(
                new ClientInstallation(
                    GameId,
                    "2.0.0",
                    InstallationState.Repairable,
                    _installDirectory,
                    InstallationCertification.DeriveInstallDirectoryId(_installDirectory)),
                TestContext.Current.CancellationToken);

            var installation = await store.LoadInstallationAsync(
                GameId,
                TestContext.Current.CancellationToken);

            Assert.Equal("2.0.0", installation!.InstalledVersion);
            Assert.Equal(InstallationState.Repairable, installation.ValidationState);
        }

        // La fila se cuenta con una conexión PROPIA, con el contexto del cliente ya
        // cerrado, así que la lectura ve lo que se persistió de verdad (AD-11).
        Assert.Equal(1L, await CountRowsAsync("CLIENT_INSTALLATION"));
    }

    /// <summary>
    /// The path of the installation NEVER leaves the client: la fila la guarda y no se
    /// publica por ningún otro canal (AD-22).
    /// </summary>
    [Fact]
    public async Task The_path_of_the_installation_stays_in_the_row_of_the_client()
    {
        await using (var storeContext = await OpenContextAsync())
        {
            var store = new ClientStateStore(storeContext);

            await store.MigrateAsync(TestContext.Current.CancellationToken);

            await store.SaveInstallationAsync(
                new ClientInstallation(
                    GameId,
                    Version,
                    InstallationState.Installed,
                    _installDirectory,
                    InstallationCertification.DeriveInstallDirectoryId(_installDirectory)),
                TestContext.Current.CancellationToken);
        }

        // La ruta se lee con una conexión PROPIA, con el contexto del cliente ya
        // cerrado, así que la lectura ve lo que se persistió de verdad (AD-11, AD-22).
        Assert.Equal(_installDirectory, await ReadInstallPathAsync());
    }

    /// <summary>
    /// Counts the rows of a table of the database of the client with a connection PROPIA
    /// (AD-11).
    /// </summary>
    /// <param name="table">Name of the table.</param>
    /// <returns>The number of rows of the table.</returns>
    private async Task<long> CountRowsAsync(string table)
    {
        await using var connection = new SqliteConnection($"Data Source={_databasePath}");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}";

        return Convert.ToInt64(
            await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Reads the path of the installation from the row of the client with a conexión
    /// PROPIA (AD-11, AD-22).
    /// </summary>
    /// <returns>The path of the installation.</returns>
    private async Task<string?> ReadInstallPathAsync()
    {
        await using var connection = new SqliteConnection($"Data Source={_databasePath}");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT installPath FROM CLIENT_INSTALLATION WHERE gameId = $gameId";
        command.Parameters.AddWithValue("$gameId", GameId);

        return (string?)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A videogame that the client has NOT registered has NO installation (AD-22).
    /// </summary>
    [Fact]
    public async Task A_videogame_that_is_not_registered_has_no_installation()
    {
        await using var storeContext = await OpenContextAsync();

        var store = new ClientStateStore(storeContext);

        await store.MigrateAsync(TestContext.Current.CancellationToken);

        var installation = await store.LoadInstallationAsync(
            "0193f2a1-b2c3-4d4e-5f60-000000000000",
            TestContext.Current.CancellationToken);

        Assert.Null(installation);
    }

    /// <summary>
    /// Opens the context of the client over the real SQLite file of the test (AD-11).
    /// </summary>
    /// <returns>The context of the client.</returns>
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
}
