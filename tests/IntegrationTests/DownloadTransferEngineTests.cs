using LANPartyHub.Client.Application;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Host;
using LANPartyHub.Client.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;
using ClientProgram = LANPartyHub.Client.Program;

namespace LANPartyHub.IntegrationTests;

/// <summary>
/// Integration level (AD-16): the closed value of the engine of the transfer against a
/// real SQLite file of the client.
///
/// <para>
/// The test starts the real client over a temporary data folder, so the migrations are
/// applied for real and the reconciliation reads the rows of the real database. A record
/// whose engine does not exist in the active version is rejected WITHOUT stopping the
/// startup (AD-24).
/// </para>
/// </summary>
public sealed class DownloadTransferEngineTests : IDisposable
{
    private readonly List<string> _temporaryFolders = [];

    [Fact]
    public async Task An_operation_of_the_active_engine_is_reconciled()
    {
        var dataFolder = CreateTemporaryDataFolder();

        await StartClientAsync(dataFolder);

        const string operationId = "operacion-activa";
        const string packageId = "paquete-activo";

        WritePartialFile(dataFolder, packageId, 8);
        InsertOperation(dataFolder, operationId, packageId, TransferEngineRegistry.HttpRangeTransferEngine, 100, 100);

        var startup = await StartClientAsync(dataFolder);

        // La operación del motor activo NO se rechaza: se reconcilia.
        Assert.Empty(startup.Reconciliation.RejectedOperations);

        var corrected = Assert.Single(startup.Reconciliation.CorrectedProgresses);
        Assert.Equal(operationId, corrected.OperationId);
        Assert.Equal(100, corrected.PersistedOffset);
        Assert.Equal(8, corrected.EffectiveOffset);
    }

    [Fact]
    public async Task An_operation_of_the_previous_engine_is_rejected_without_stopping_the_startup()
    {
        var dataFolder = CreateTemporaryDataFolder();

        await StartClientAsync(dataFolder);

        const string operationId = "operacion-antigua";
        const string packageId = "paquete-antiguo";

        WritePartialFile(dataFolder, packageId, 8);
        InsertOperation(dataFolder, operationId, packageId, "HttpRange", 4, 100);

        // El arranque COMPLETA: el rechazo de un motor desconocido nunca detiene el
        // cliente (AD-24).
        var startup = await StartClientAsync(dataFolder);

        var rejected = Assert.Single(startup.Reconciliation.RejectedOperations);
        Assert.Equal(operationId, rejected.OperationId);
        Assert.Equal("HttpRange", rejected.TransferEngine);

        // El registro NO se borra: queda en la base de datos para que otra versión lo lea.
        var engines = ReadEngines(
            DatabasePath(dataFolder),
            "SELECT transferEngine FROM DOWNLOAD_OPERATION WHERE operationId = $operationId",
            ("$operationId", operationId));

        Assert.Equal(new[] { "HttpRange" }, engines);
    }

    [Fact]
    public async Task An_operation_of_a_torrent_engine_is_rejected_without_stopping_the_startup()
    {
        var dataFolder = CreateTemporaryDataFolder();

        await StartClientAsync(dataFolder);

        const string operationId = "operacion-torrent";
        const string packageId = "paquete-torrent";

        WritePartialFile(dataFolder, packageId, 8);
        InsertOperation(dataFolder, operationId, packageId, "Torrent", 4, 100);

        var startup = await StartClientAsync(dataFolder);

        var rejected = Assert.Single(startup.Reconciliation.RejectedOperations);
        Assert.Equal(operationId, rejected.OperationId);
        Assert.Equal("Torrent", rejected.TransferEngine);

        Assert.Empty(startup.Reconciliation.CorrectedProgresses);
    }

    [Fact]
    public async Task The_migration_rewrites_the_previous_engine_into_the_active_one()
    {
        var dataFolder = CreateTemporaryDataFolder();

        // La base de datos se deja en la migración ANTERIOR al renombrado, que es el
        // estado en el que una máquina que se actualiza guarda sus operaciones.
        await MigrateAsync(dataFolder, "20261103093620_TrustAnchorTable");

        const string operationId = "operacion-heredada";
        const string packageId = "paquete-heredado";

        InsertOperation(dataFolder, operationId, packageId, "HttpRange", 4, 100);

        var before = ReadEngines(
            DatabasePath(dataFolder),
            "SELECT transferEngine FROM DOWNLOAD_OPERATION WHERE operationId = $operationId",
            ("$operationId", operationId));

        Assert.Equal(new[] { "HttpRange" }, before);

        // La migración del renombrado se aplica encima y REESCRIBE el valor persistido.
        await MigrateAsync(dataFolder);

        var after = ReadEngines(
            DatabasePath(dataFolder),
            "SELECT transferEngine FROM DOWNLOAD_OPERATION WHERE operationId = $operationId",
            ("$operationId", operationId));

        Assert.Equal(new[] { "HttpRangeTransferEngine" }, after);
    }

    [Fact]
    public async Task An_operation_migrated_from_the_previous_engine_is_reconciled()
    {
        var dataFolder = CreateTemporaryDataFolder();

        await MigrateAsync(dataFolder, "20261103093620_TrustAnchorTable");

        const string operationId = "operacion-migrada";
        const string packageId = "paquete-migrado";

        WritePartialFile(dataFolder, packageId, 8);
        InsertOperation(dataFolder, operationId, packageId, "HttpRange", 100, 100);

        // El arranque aplica la migración del renombrado y la operación deja de estar
        // rechazada: la operación se REANUDA (AD-24).
        var startup = await StartClientAsync(dataFolder);

        Assert.Empty(startup.Reconciliation.RejectedOperations);

        var corrected = Assert.Single(startup.Reconciliation.CorrectedProgresses);
        Assert.Equal(operationId, corrected.OperationId);
        Assert.Equal(100, corrected.PersistedOffset);
        Assert.Equal(8, corrected.EffectiveOffset);
    }

    [Fact]
    public async Task The_column_of_the_engine_holds_the_identifier_of_the_only_implementation()
    {
        var dataFolder = CreateTemporaryDataFolder();

        await StartClientAsync(dataFolder);

        const string operationId = "operacion-identificador";
        const string packageId = "paquete-identificador";

        InsertOperation(dataFolder, operationId, packageId, TransferEngineRegistry.HttpRangeTransferEngine, 0, 100);

        var engines = ReadEngines(
            DatabasePath(dataFolder),
            "SELECT transferEngine FROM DOWNLOAD_OPERATION WHERE operationId = $operationId",
            ("$operationId", operationId));

        // AC4: el valor persistido es EXACTAMENTE el identificador de la única
        // implementación del puerto.
        Assert.Equal(new[] { "HttpRangeTransferEngine" }, engines);
        Assert.Equal("HttpRangeTransferEngine", TransferEngineRegistry.HttpRangeTransferEngine);
    }

    private static string DatabasePath(string dataFolder) =>
        Path.Combine(dataFolder, ClientDataLayout.StateDatabaseFileName);

    private string CreateTemporaryDataFolder()
    {
        var folder = Directory.CreateTempSubdirectory("lan-party-hub-transfer-");

        _temporaryFolders.Add(folder.FullName);

        return folder.FullName;
    }

    private async Task<ClientStartupResult> StartClientAsync(string dataFolder)
    {
        var logFolder = CreateTemporaryDataFolder();

        using var factory = ClientProgram.CreateLoggerFactory(logFolder);
        using var correlation = new ClientCorrelationFactory(factory);

        return await ClientProgram.StartClientAsync(
            [],
            correlation,
            factory,
            dataFolder: dataFolder,
            baseDirectory: logFolder,
            cancellationToken: TestContext.Current.CancellationToken);
    }

    private static async Task MigrateAsync(string dataFolder, string? targetMigration = null)
    {
        var options = new DbContextOptionsBuilder<ClientStateDbContext>()
            .UseSqlite(
                $"Data Source={DatabasePath(dataFolder)};Pooling=False",
                sqlite => sqlite.MigrationsAssembly(typeof(ClientStateDbContext).Assembly.GetName().Name))
            .Options;

        await using var context = new ClientStateDbContext(options);

        if (targetMigration is null)
        {
            await context.Database.MigrateAsync(TestContext.Current.CancellationToken);

            return;
        }

        // La migración objetivo se aplica con el migrador de EF Core, que deja la base de
        // datos en el estado de esa migración y no en el de la última.
        await context.Database
            .GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>()
            .MigrateAsync(targetMigration, TestContext.Current.CancellationToken);
    }

    private static void WritePartialFile(string dataFolder, string packageId, int length)
    {
        // La carpeta de las descargas la crea el arranque, así que el test la crea cuando
        // escribe el archivo parcial antes de que el cliente arranque.
        var downloads = Path.Combine(dataFolder, ClientDataLayout.DownloadsFolderName);
        Directory.CreateDirectory(downloads);

        var path = Path.Combine(
            downloads,
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

    private static IReadOnlyList<string> ReadEngines(
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

        var engines = new List<string>();

        using var results = reader.ExecuteReader();

        while (results.Read())
        {
            engines.Add(results.GetString(0));
        }

        return engines;
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
