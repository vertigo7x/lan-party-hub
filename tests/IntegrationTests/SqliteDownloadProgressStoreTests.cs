using Xunit;

namespace LANPartyHub.IntegrationTests;

/// <summary>
/// Integration level (AD-16): persistence against a real SQLite file. The test writes to a
/// file in disk, closes the connection and reads the data back with another connection, so
/// it observes what was really persisted and not what an in memory database would keep.
/// </summary>
public sealed class SqliteDownloadProgressStoreTests : IDisposable
{
    private readonly string _workingDirectory;
    private readonly string _databasePath;

    /// <summary>Creates an isolated working directory for the test.</summary>
    public SqliteDownloadProgressStoreTests()
    {
        _workingDirectory = Path.Combine(Path.GetTempPath(), "lanpartyhub-integration", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workingDirectory);
        _databasePath = Path.Combine(_workingDirectory, "state.sqlite");
    }

    /// <summary>Removes the working directory of the test.</summary>
    public void Dispose()
    {
        if (Directory.Exists(_workingDirectory))
        {
            Directory.Delete(_workingDirectory, recursive: true);
        }
    }

    [Fact]
    public void The_progress_is_persisted_in_a_real_sqlite_file()
    {
        const string operationId = "0193f2a1-b2c3-4d4e-5f60-718293a4b5c6";

        using (var store = new SqliteDownloadProgressStore(_databasePath))
        {
            store.SaveProgress(operationId, bytesPersisted: 3_221_225_472, totalBytes: 7_600_000_000);
        }

        Assert.True(File.Exists(_databasePath));
        Assert.True(new FileInfo(_databasePath).Length > 0);

        using var reopened = new SqliteDownloadProgressStore(_databasePath);
        var progress = reopened.GetProgress(operationId);

        Assert.NotNull(progress);
        Assert.Equal(3_221_225_472, progress!.BytesPersisted);
        Assert.Equal(7_600_000_000, progress.TotalBytes);
    }

    [Fact]
    public void The_progress_table_holds_exactly_the_columns_of_the_http_path()
    {
        using var store = new SqliteDownloadProgressStore(_databasePath);

        var columns = store.ColumnNames();

        Assert.Equal(new[] { "operationId", "bytesPersisted", "totalBytes" }, columns);
        Assert.DoesNotContain("chunkMap", columns, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_operation_without_persisted_progress_is_not_found()
    {
        using var store = new SqliteDownloadProgressStore(_databasePath);

        Assert.Null(store.GetProgress("0193f2a1-b2c3-4d4e-5f60-718293a4b5c6"));
    }

    [Fact]
    public void The_persisted_offset_survives_a_new_connection()
    {
        const string operationId = "0193f2a1-b2c3-4d4e-5f60-718293a4b5c6";

        using (var store = new SqliteDownloadProgressStore(_databasePath))
        {
            store.SaveProgress(operationId, bytesPersisted: 1024, totalBytes: 4096);
            store.SaveProgress(operationId, bytesPersisted: 2048, totalBytes: 4096);
        }

        using var reopened = new SqliteDownloadProgressStore(_databasePath);
        var progress = reopened.GetProgress(operationId);

        Assert.NotNull(progress);
        Assert.Equal(2048, progress!.BytesPersisted);
    }

    [Fact]
    public void The_effective_progress_is_the_intersection_of_the_offset_and_the_partial_file()
    {
        const string operationId = "0193f2a1-b2c3-4d4e-5f60-718293a4b5c6";
        var partialFilePath = Path.Combine(_workingDirectory, "package.part");

        using (var store = new SqliteDownloadProgressStore(_databasePath))
        {
            store.SaveProgress(operationId, bytesPersisted: 2048, totalBytes: 4096);
        }

        File.WriteAllBytes(partialFilePath, new byte[1024]);

        using var reopened = new SqliteDownloadProgressStore(_databasePath);
        var progress = reopened.GetProgress(operationId);

        Assert.NotNull(progress);

        var effective = SqliteDownloadProgressStore.Reconcile(
            progress!.BytesPersisted,
            new FileInfo(partialFilePath).Length);

        Assert.Equal(1024, effective);
    }
}
