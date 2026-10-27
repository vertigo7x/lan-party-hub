using Microsoft.Data.Sqlite;

namespace LANPartyHub.IntegrationTests;

/// <summary>
/// Declared content of the integration level (AD-16): persistence against a real SQLite file,
/// never against an in memory database.
///
/// Story 1.1 declares the persistence that Story 5.3 moves to the Infrastructure layer of
/// the client. The shape it persists is already fixed by the spine: the progress of the HTTP
/// path is an accumulated offset with the columns <c>bytesPersisted</c> and <c>totalBytes</c>,
/// and there is no chunk map (AD-24, AD-28).
/// </summary>
internal sealed class SqliteDownloadProgressStore : IDisposable
{
    private const string TableName = "DOWNLOAD_PROGRESS";

    private readonly string _databasePath;

    /// <summary>Opens the store and creates the schema of the progress table.</summary>
    /// <param name="databasePath">Path of the SQLite file.</param>
    public SqliteDownloadProgressStore(string databasePath)
    {
        _databasePath = databasePath;
        EnsureSchema();
    }

    /// <summary>Path of the SQLite file that holds the data.</summary>
    public string DatabasePath => _databasePath;

    /// <summary>Persists the accumulated offset of a download operation.</summary>
    /// <param name="operationId">Opaque UUID v7 identifier of the operation.</param>
    /// <param name="bytesPersisted">Bytes already written in the partial file.</param>
    /// <param name="totalBytes">Total bytes of the package.</param>
    public void SaveProgress(string operationId, long bytesPersisted, long totalBytes)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"INSERT INTO {TableName} (operationId, bytesPersisted, totalBytes) VALUES ($operationId, $bytesPersisted, $totalBytes) " +
            "ON CONFLICT(operationId) DO UPDATE SET bytesPersisted = $bytesPersisted, totalBytes = $totalBytes;";
        command.Parameters.AddWithValue("$operationId", operationId);
        command.Parameters.AddWithValue("$bytesPersisted", bytesPersisted);
        command.Parameters.AddWithValue("$totalBytes", totalBytes);
        command.ExecuteNonQuery();
    }

    /// <summary>Reads the persisted progress of a download operation.</summary>
    /// <param name="operationId">Opaque UUID v7 identifier of the operation.</param>
    public DownloadProgress? GetProgress(string operationId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT bytesPersisted, totalBytes FROM {TableName} WHERE operationId = $operationId;";
        command.Parameters.AddWithValue("$operationId", operationId);

        using var reader = command.ExecuteReader();

        if (!reader.Read())
        {
            return null;
        }

        return new DownloadProgress(reader.GetInt64(0), reader.GetInt64(1));
    }

    /// <summary>Names of the columns of the progress table.</summary>
    public IReadOnlyList<string> ColumnNames()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({TableName});";

        var columns = new List<string>();

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }

    /// <summary>
    /// Reconciles the persisted offset against the bytes that the partial file really holds
    /// (AD-11): the effective progress is the intersection of both.
    /// </summary>
    /// <param name="bytesPersisted">Offset persisted in the database.</param>
    /// <param name="partialFileLength">Length of the partial file in disk.</param>
    public static long Reconcile(long bytesPersisted, long partialFileLength) => Math.Min(bytesPersisted, partialFileLength);

    /// <inheritdoc />
    public void Dispose()
    {
        // The connections are opened without pooling, so closing them releases the file
        // right away. The global pool of the process is never touched, because clearing it
        // would break the connections of a test class running in parallel.
    }

    /// <summary>Progress of a download operation as it is persisted.</summary>
    /// <param name="BytesPersisted">Accumulated offset in bytes.</param>
    /// <param name="TotalBytes">Total bytes of the package.</param>
    public sealed record DownloadProgress(long BytesPersisted, long TotalBytes);

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection($"Data Source={_databasePath};Pooling=False");
        connection.Open();
        return connection;
    }

    private void EnsureSchema()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"CREATE TABLE IF NOT EXISTS {TableName} (" +
            "operationId TEXT NOT NULL PRIMARY KEY, " +
            "bytesPersisted INTEGER NOT NULL, " +
            "totalBytes INTEGER NOT NULL);";
        command.ExecuteNonQuery();
    }
}
