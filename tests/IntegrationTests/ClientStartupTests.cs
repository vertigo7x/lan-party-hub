using System.Text.Json;
using LANPartyHub.Client.Application;
using LANPartyHub.Client.Host;
using LANPartyHub.Client.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;
using ClientProgram = LANPartyHub.Client.Program;

namespace LANPartyHub.IntegrationTests;

/// <summary>
/// Real startup of the client (AD-9, AD-11, AD-17, AD-23, AD-24).
///
/// <para>
/// The tests start the real client over a temporary data folder, with the real layout on disk
/// and the real SQLite file, because that is the only way to observe the layout, the schema
/// of the database of the client and the behaviour of the identity across restarts.
/// </para>
/// </summary>
public sealed class ClientStartupTests : IDisposable
{
    private static readonly string[] ExpectedTables =
    [
        "CLIENT_IDENTITY",
        "CLIENT_INSTALLATION",
        "DOWNLOAD_OPERATION",
        "DOWNLOAD_PROGRESS",
    ];

    private static readonly string[] ServerTables =
    [
        "CLIENT_ERROR",
        "CLIENT_INSTALLATION_VIEW",
        "CLIENT_REGISTRATION",
        "DOWNLOAD_INTENT",
        "GAME",
        "INSTALLATION_MANIFEST",
        "PACKAGE_DESCRIPTOR",
    ];

    private readonly List<string> _temporaryFolders = [];

    [Fact]
    public async Task The_startup_creates_the_four_entries_of_the_layout()
    {
        var dataFolder = CreateTemporaryDataFolder();

        var startup = await StartClientAsync(dataFolder);

        Assert.Equal(dataFolder, startup.DataFolder);
        Assert.True(Directory.Exists(Path.Combine(dataFolder, ClientDataLayout.InstallFolderName)));
        Assert.True(Directory.Exists(Path.Combine(dataFolder, ClientDataLayout.DownloadsFolderName)));
        Assert.True(Directory.Exists(Path.Combine(dataFolder, ClientDataLayout.ManifestsFolderName)));
        Assert.True(File.Exists(Path.Combine(dataFolder, ClientDataLayout.StateDatabaseFileName)));
    }

    [Fact]
    public async Task The_configured_install_root_is_the_confined_one_and_the_default_root_still_exists()
    {
        var dataFolder = CreateTemporaryDataFolder();
        var installRoot = Path.Combine(CreateTemporaryDataFolder(), "juegos");

        var startup = await StartClientAsync(dataFolder, ["--install-directory", installRoot]);

        // The configured root is the one that confines the installation.
        Assert.Equal(Path.GetFullPath(installRoot), startup.InstallDirectory);
        Assert.True(Directory.Exists(installRoot));

        // The layout of the data folder always holds the default root, and it is left unused.
        Assert.True(Directory.Exists(Path.Combine(dataFolder, ClientDataLayout.InstallFolderName)));
    }

    [Fact]
    public async Task The_startup_creates_the_four_tables_of_the_erd_of_the_client()
    {
        var dataFolder = CreateTemporaryDataFolder();

        await StartClientAsync(dataFolder);

        var tables = ReadNames(
            DatabasePath(dataFolder),
            "SELECT name FROM sqlite_master WHERE type = 'table'");

        foreach (var table in ExpectedTables)
        {
            Assert.Contains(table, tables);
        }
    }

    [Theory]
    [InlineData("CLIENT_IDENTITY", "clientId")]
    [InlineData(
        "CLIENT_INSTALLATION",
        "gameId,installedVersion,validationState,installPath,installDirectoryId")]
    [InlineData("DOWNLOAD_OPERATION", "operationId,packageId,state,transferEngine")]
    [InlineData("DOWNLOAD_PROGRESS", "operationId,bytesPersisted,totalBytes")]
    public async Task The_columns_of_the_table_keep_the_name_and_the_order_of_the_erd(
        string table,
        string expectedColumns)
    {
        var dataFolder = CreateTemporaryDataFolder();

        await StartClientAsync(dataFolder);

        var columns = ReadNames(
            DatabasePath(dataFolder),
            $"SELECT name FROM pragma_table_info('{table}')");

        Assert.Equal(expectedColumns.Split(','), columns);
    }

    [Fact]
    public async Task The_database_of_the_client_replicates_no_table_of_the_server()
    {
        var dataFolder = CreateTemporaryDataFolder();

        await StartClientAsync(dataFolder);

        var tables = ReadNames(
            DatabasePath(dataFolder),
            "SELECT name FROM sqlite_master WHERE type = 'table'");

        foreach (var table in ServerTables)
        {
            Assert.DoesNotContain(table, tables);
        }
    }

    [Fact]
    public async Task The_history_of_the_migrations_records_the_migration_of_the_client()
    {
        var dataFolder = CreateTemporaryDataFolder();

        await StartClientAsync(dataFolder);

        var appliedMigrations = ReadNames(
            DatabasePath(dataFolder),
            "SELECT MigrationId FROM __EFMigrationsHistory");

        Assert.NotEmpty(appliedMigrations);
        Assert.Contains(appliedMigrations, migration => migration.Contains("InitialClientStateSchema", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_startup_is_idempotent_over_a_database_that_is_already_migrated()
    {
        var dataFolder = CreateTemporaryDataFolder();

        await StartClientAsync(dataFolder);
        await StartClientAsync(dataFolder);

        var appliedMigrations = ReadNames(
            DatabasePath(dataFolder),
            "SELECT MigrationId FROM __EFMigrationsHistory");

        Assert.Single(appliedMigrations);
    }

    [Fact]
    public async Task The_second_startup_keeps_the_client_id_that_was_emitted()
    {
        var dataFolder = CreateTemporaryDataFolder();

        var first = await StartClientAsync(dataFolder);
        var second = await StartClientAsync(dataFolder);

        Assert.Equal(first.Identity.CanonicalId, second.Identity.CanonicalId);
        Assert.True(ClientIdentityIsCanonical(first.Identity.CanonicalId));

        var rows = ReadNames(DatabasePath(dataFolder), "SELECT clientId FROM CLIENT_IDENTITY");

        Assert.Equal(new[] { first.Identity.CanonicalId }, rows);
    }

    [Fact]
    public async Task An_operation_of_an_unknown_engine_is_rejected_and_the_startup_continues()
    {
        var dataFolder = CreateTemporaryDataFolder();

        await StartClientAsync(dataFolder);

        const string operationId = "operacion-1";
        const string packageId = "paquete-desconocido";

        WritePartialFile(dataFolder, packageId, 8);
        InsertOperation(dataFolder, operationId, packageId, "MotorQueNoExiste", 4, 100);

        var startup = await StartClientAsync(dataFolder);

        // The record is rejected and it is not interpreted as an active operation.
        var rejected = Assert.Single(startup.Reconciliation.RejectedOperations);
        Assert.Equal(operationId, rejected.OperationId);
        Assert.Equal("MotorQueNoExiste", rejected.TransferEngine);

        // The record stays in the database, so another version can read it.
        var offsets = ReadNumbers(
            DatabasePath(dataFolder),
            "SELECT bytesPersisted FROM DOWNLOAD_PROGRESS WHERE operationId = $operationId",
            ("$operationId", operationId));

        Assert.Equal(new[] { 4L }, offsets);
    }

    [Fact]
    public async Task An_operation_whose_package_id_cannot_be_a_file_name_is_not_reconciled()
    {
        var dataFolder = CreateTemporaryDataFolder();

        await StartClientAsync(dataFolder);

        const string operationId = "operacion-4";
        const string packageId = "paquete\\con\\separador";

        InsertOperation(dataFolder, operationId, packageId, "HttpRange", 4, 100);

        // The startup completes: a corrupted row never takes the client down.
        var startup = await StartClientAsync(dataFolder);

        Assert.Empty(startup.Reconciliation.CorrectedProgresses);

        // The offset of the operation is not touched.
        var offsets = ReadNumbers(
            DatabasePath(dataFolder),
            "SELECT bytesPersisted FROM DOWNLOAD_PROGRESS WHERE operationId = $operationId",
            ("$operationId", operationId));

        Assert.Equal(new[] { 4L }, offsets);
    }

    [Fact]
    public async Task The_persisted_offset_is_corrected_when_the_partial_file_is_shorter()
    {
        var dataFolder = CreateTemporaryDataFolder();

        await StartClientAsync(dataFolder);

        const string operationId = "operacion-2";
        const string packageId = "paquete-corto";

        WritePartialFile(dataFolder, packageId, 8);
        InsertOperation(dataFolder, operationId, packageId, "HttpRange", 100, 100);

        var startup = await StartClientAsync(dataFolder);

        var corrected = Assert.Single(startup.Reconciliation.CorrectedProgresses);
        Assert.Equal(operationId, corrected.OperationId);
        Assert.Equal(100, corrected.PersistedOffset);
        Assert.Equal(8, corrected.EffectiveOffset);

        var offsets = ReadNumbers(
            DatabasePath(dataFolder),
            "SELECT bytesPersisted FROM DOWNLOAD_PROGRESS WHERE operationId = $operationId",
            ("$operationId", operationId));

        Assert.Equal(new[] { 8L }, offsets);
    }

    [Fact]
    public async Task The_persisted_offset_is_kept_when_the_partial_file_is_longer()
    {
        var dataFolder = CreateTemporaryDataFolder();

        await StartClientAsync(dataFolder);

        const string operationId = "operacion-3";
        const string packageId = "paquete-largo";

        WritePartialFile(dataFolder, packageId, 8);
        InsertOperation(dataFolder, operationId, packageId, "HttpRange", 4, 100);

        var startup = await StartClientAsync(dataFolder);

        Assert.Empty(startup.Reconciliation.CorrectedProgresses);

        var offsets = ReadNumbers(
            DatabasePath(dataFolder),
            "SELECT bytesPersisted FROM DOWNLOAD_PROGRESS WHERE operationId = $operationId",
            ("$operationId", operationId));

        Assert.Equal(new[] { 4L }, offsets);
    }

    [Fact]
    public async Task A_negative_persisted_offset_is_corrected_to_the_size_of_the_partial_file()
    {
        var dataFolder = CreateTemporaryDataFolder();

        await StartClientAsync(dataFolder);

        const string operationId = "operacion-5";
        const string packageId = "paquete-negativo";

        WritePartialFile(dataFolder, packageId, 8);
        InsertOperation(dataFolder, operationId, packageId, "HttpRange", -4, 100);

        var startup = await StartClientAsync(dataFolder);

        var corrected = Assert.Single(startup.Reconciliation.CorrectedProgresses);
        Assert.Equal(-4, corrected.PersistedOffset);
        Assert.Equal(0, corrected.EffectiveOffset);

        var offsets = ReadNumbers(
            DatabasePath(dataFolder),
            "SELECT bytesPersisted FROM DOWNLOAD_PROGRESS WHERE operationId = $operationId",
            ("$operationId", operationId));

        Assert.Equal(new[] { 0L }, offsets);
    }

    [Fact]
    public async Task The_persisted_offset_falls_back_to_zero_when_the_partial_file_does_not_exist()
    {
        var dataFolder = CreateTemporaryDataFolder();

        await StartClientAsync(dataFolder);

        const string operationId = "operacion-6";
        const string packageId = "paquete-sin-parcial";

        // There is no partial file on the disk: the effective progress cannot be anything
        // but zero.
        InsertOperation(dataFolder, operationId, packageId, "HttpRange", 50, 100);

        var startup = await StartClientAsync(dataFolder);

        var corrected = Assert.Single(startup.Reconciliation.CorrectedProgresses);
        Assert.Equal(50, corrected.PersistedOffset);
        Assert.Equal(0, corrected.EffectiveOffset);

        var offsets = ReadNumbers(
            DatabasePath(dataFolder),
            "SELECT bytesPersisted FROM DOWNLOAD_PROGRESS WHERE operationId = $operationId",
            ("$operationId", operationId));

        Assert.Equal(new[] { 0L }, offsets);
    }

    [Fact]
    public async Task The_install_directory_that_is_saved_is_persisted_and_read_on_the_next_startup()
    {
        var dataFolder = CreateTemporaryDataFolder();
        var deployment = CreateTemporaryDataFolder();
        var newRoot = Path.Combine(CreateTemporaryDataFolder(), "juegos");

        var first = await StartClientAsync(dataFolder, baseDirectory: deployment);

        Assert.NotNull(first.Settings);

        var saved = await first.Settings!.SaveInstallDirectoryAsync(
            newRoot,
            TestContext.Current.CancellationToken);

        // The new root is the only confined one from that moment on.
        Assert.Equal(Path.GetFullPath(newRoot), saved.InstallDirectory);
        Assert.True(Directory.Exists(newRoot));

        // The value is persisted in the configuration file of the deployment.
        var persisted = await File.ReadAllTextAsync(
            Path.Combine(deployment, ClientConfiguration.FileName),
            TestContext.Current.CancellationToken);

        using var document = JsonDocument.Parse(persisted);

        Assert.Equal(
            newRoot,
            document.RootElement
                .GetProperty(ClientConfiguration.SectionName)
                .GetProperty(ClientConfiguration.InstallDirectoryKey)
                .GetString());

        // The next startup reads the persisted value.
        var second = await StartClientAsync(dataFolder, baseDirectory: deployment);

        Assert.Equal(Path.GetFullPath(newRoot), second.InstallDirectory);
        Assert.True(Directory.Exists(newRoot));
    }

    [Fact]
    public async Task A_flag_wins_over_the_persisted_install_directory()
    {
        var dataFolder = CreateTemporaryDataFolder();
        var deployment = CreateTemporaryDataFolder();
        var persistedRoot = Path.Combine(CreateTemporaryDataFolder(), "persistido");
        var flaggedRoot = Path.Combine(CreateTemporaryDataFolder(), "flag");

        var first = await StartClientAsync(dataFolder, baseDirectory: deployment);

        await first.Settings!.SaveInstallDirectoryAsync(
            persistedRoot,
            TestContext.Current.CancellationToken);

        var second = await StartClientAsync(
            dataFolder,
            ["--install-directory", flaggedRoot],
            baseDirectory: deployment);

        Assert.Equal(Path.GetFullPath(flaggedRoot), second.InstallDirectory);
    }

    [Fact]
    public async Task The_startup_aborts_with_an_actionable_message_when_the_data_folder_cannot_be_written()
    {
        var parent = CreateTemporaryDataFolder();

        // A file where the data folder should be: the folder cannot be created, so the
        // startup aborts before the database is opened.
        var blocked = Path.Combine(parent, "bloqueado");
        File.WriteAllText(blocked, "no es una carpeta");

        var exception = await Assert.ThrowsAsync<ClientStartupException>(
            () => StartClientAsync(blocked));

        Assert.Contains(blocked, exception.Message);
        Assert.DoesNotContain("   at ", exception.Message, StringComparison.Ordinal);
    }

    private static bool ClientIdentityIsCanonical(string canonicalId) =>
        canonicalId.Length == 32
        && canonicalId.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string DatabasePath(string dataFolder) =>
        Path.Combine(dataFolder, ClientDataLayout.StateDatabaseFileName);

    private string CreateTemporaryDataFolder()
    {
        var folder = Directory.CreateTempSubdirectory("lan-party-hub-client-");

        _temporaryFolders.Add(folder.FullName);

        return folder.FullName;
    }

    private async Task<ClientStartupResult> StartClientAsync(
        string dataFolder,
        string[]? args = null,
        string? baseDirectory = null)
    {
        // The log of the client goes to a file next to the deployment, and the deployment
        // of this test is a temporary folder so the file does not stay in the output of the
        // tests.
        var logFolder = CreateTemporaryDataFolder();

        using var factory = ClientProgram.CreateLoggerFactory(logFolder);
        using var correlation = new ClientCorrelationFactory(factory);

        return await ClientProgram.StartClientAsync(
            args ?? [],
            correlation,
            factory,
            dataFolder: dataFolder,
            baseDirectory: baseDirectory,
            cancellationToken: TestContext.Current.CancellationToken);
    }

    private static void WritePartialFile(string dataFolder, string packageId, int length)
    {
        var path = Path.Combine(
            dataFolder,
            ClientDataLayout.DownloadsFolderName,
            packageId + ClientDataLayout.PartialFileExtension);

        File.WriteAllBytes(path, new byte[length]);
    }

    private static void InsertOperation(
        string dataFolder,
        string operationId,
        string packageId,
        string transferEngine,
        long bytesPersisted,
        long totalBytes)
    {
        using var connection = new SqliteConnection($"Data Source={DatabasePath(dataFolder)};Pooling=False");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO DOWNLOAD_OPERATION (operationId, packageId, state, transferEngine) "
            + "VALUES ($operationId, $packageId, $state, $transferEngine);"
            + "INSERT INTO DOWNLOAD_PROGRESS (operationId, bytesPersisted, totalBytes) "
            + "VALUES ($operationId, $bytesPersisted, $totalBytes);";
        command.Parameters.AddWithValue("$operationId", operationId);
        command.Parameters.AddWithValue("$packageId", packageId);
        command.Parameters.AddWithValue("$state", "Downloading");
        command.Parameters.AddWithValue("$transferEngine", transferEngine);
        command.Parameters.AddWithValue("$bytesPersisted", bytesPersisted);
        command.Parameters.AddWithValue("$totalBytes", totalBytes);
        command.ExecuteNonQuery();
    }

    private static IReadOnlyList<string> ReadNames(string databasePath, string command)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        connection.Open();

        using var reader = connection.CreateCommand();
        reader.CommandText = command;

        var names = new List<string>();

        using var results = reader.ExecuteReader();

        while (results.Read())
        {
            names.Add(results.GetString(0));
        }

        return names;
    }

    private static IReadOnlyList<long> ReadNumbers(
        string databasePath,
        string command,
        params (string Name, string Value)[] parameters)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        connection.Open();

        using var reader = connection.CreateCommand();
        reader.CommandText = command;

        foreach (var parameter in parameters)
        {
            reader.Parameters.AddWithValue(parameter.Name, parameter.Value);
        }

        var numbers = new List<long>();

        using var results = reader.ExecuteReader();

        while (results.Read())
        {
            numbers.Add(results.GetInt64(0));
        }

        return numbers;
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
        }
    }
}
