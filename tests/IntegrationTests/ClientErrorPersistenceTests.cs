using LANPartyHub.Server.Application.Ports;
using LANPartyHub.Server.Infrastructure.Persistence;
using LANPartyHub.Server.Infrastructure.Persistence.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LANPartyHub.IntegrationTests;

/// <summary>
/// The persistence of the errors that the clients report (AD-15, AD-22, FR-10).
///
/// <para>
/// Los tests escriben contra el SQLite REAL en archivo y observan las filas que quedan
/// en la tabla <c>CLIENT_ERROR</c>: la escritura es idempotente por <c>errorId</c>, así
/// que una republicación NO duplica el diagnóstico que el organizador ve, y dos
/// escrituras CONCURRENTES del mismo error NO compiten por la clave primaria
/// (AD-15, AD-22).
/// </para>
/// </summary>
public sealed class ClientErrorPersistenceTests
{
    private const string ClientId = "0193f2a1b2c34d4e5f60718293a4b5c6";
    private const string ErrorId = "0193f2a1b2c34d4e5f60718293a4b5f0";

    [Fact]
    public async Task The_same_error_written_twice_leaves_one_row()
    {
        var dataDirectory = Directory.CreateTempSubdirectory("lan-party-hub-error-upsert-").FullName;

        try
        {
            await ApplyMigrationsAsync(dataDirectory);

            var write = CreateWrite();

            await SaveAsync(dataDirectory, write);
            await SaveAsync(dataDirectory, write);

            // AD-15, AD-22: la escritura es IDEMPOTENTE por `errorId`, así que la
            // republicación NO crea una segunda fila.
            var rows = await ReadErrorIdsAsync(dataDirectory);

            Assert.Equal([ErrorId], rows);
        }
        finally
        {
            TryDelete(dataDirectory);
        }
    }

    [Fact]
    public async Task The_concurrent_writes_of_the_same_error_leave_one_row_and_do_not_fail()
    {
        var dataDirectory = Directory.CreateTempSubdirectory("lan-party-hub-error-concurrent-").FullName;

        try
        {
            await ApplyMigrationsAsync(dataDirectory);

            var write = CreateWrite();

            // AD-22: dos escrituras CONCURRENTES del mismo error NO compiten por la clave
            // primaria `PK_CLIENT_ERROR`: la escritura es un UPSERT y ninguna falla.
            var writes = Enumerable.Range(0, 8)
                .Select(_ => SaveAsync(dataDirectory, write));

            await Task.WhenAll(writes);

            var rows = await ReadErrorIdsAsync(dataDirectory);

            Assert.Equal([ErrorId], rows);
        }
        finally
        {
            TryDelete(dataDirectory);
        }
    }

    [Fact]
    public async Task The_concurrent_inserts_of_the_same_error_leave_one_row_and_do_not_fail()
    {
        var dataDirectory = Directory.CreateTempSubdirectory("lan-party-hub-error-race-").FullName;

        try
        {
            await ApplyMigrationsAsync(dataDirectory);

            var write = CreateWrite();

            // AD-22: el INTERLAVADO REAL de dos escrituras concurrentes del mismo error.
            //
            // El servidor atiende las publicaciones del hub en paralelo, así que DOS
            // publicaciones del mismo error pueden llegar a la escritura a la vez. La
            // escritura es un UPSERT sobre la clave primaria, así que la segunda NUNCA
            // compite por la clave y NUNCA falla con la excepción de la clave duplicada.
            const int writers = 8;

            var inserts = Enumerable.Range(0, writers).Select(_ => SaveAsync(dataDirectory, write));

            await Task.WhenAll(inserts);

            var rows = await ReadErrorIdsAsync(dataDirectory);

            Assert.Equal([ErrorId], rows);
        }
        finally
        {
            TryDelete(dataDirectory);
        }
    }

    [Fact]
    public async Task The_insert_of_the_error_that_already_exists_is_ignored_and_does_not_fail()
    {
        var dataDirectory = Directory.CreateTempSubdirectory("lan-party-hub-error-ignore-").FullName;

        try
        {
            await ApplyMigrationsAsync(dataDirectory);

            await SaveAsync(dataDirectory, CreateWrite());

            // AD-22: la escritura es un UPSERT sobre la clave primaria, así que el
            // `INSERT` de la fila que ya existe se IGNORA y NO lanza la excepción de la
            // clave duplicada.
            await SaveAsync(dataDirectory, CreateWrite());

            var rows = await ReadErrorIdsAsync(dataDirectory);

            Assert.Equal([ErrorId], rows);
        }
        finally
        {
            TryDelete(dataDirectory);
        }
    }

    [Fact]
    public async Task The_errors_of_two_operations_of_the_same_client_leave_two_rows()
    {
        var dataDirectory = Directory.CreateTempSubdirectory("lan-party-hub-error-distinct-").FullName;

        try
        {
            await ApplyMigrationsAsync(dataDirectory);

            await SaveAsync(dataDirectory, CreateWrite());
            await SaveAsync(
                dataDirectory,
                CreateWrite() with { ErrorId = "0193f2a1b2c34d4e5f60718293a4b5f1" });

            // Dos diagnósticos DISTINTOS del mismo equipo son dos filas distintas.
            var rows = await ReadErrorIdsAsync(dataDirectory);

            Assert.Equal(2, rows.Count);
        }
        finally
        {
            TryDelete(dataDirectory);
        }
    }

    private static ClientErrorWrite CreateWrite() => new(
        ErrorId,
        ClientId,
        "0193f2a1-b2c3-4d4e-5f60-718293a4b5c8",
        "PACKAGE_INTEGRITY_MISMATCH",
        "El paquete está dañado: el hash no coincide",
        "El sha256 calculado del paquete no coincide con el del manifiesto firmado.",
        "Descarta el paquete y descárgalo de nuevo: la operación no se puede reanudar.",
        DateTimeOffset.UtcNow);

    private static async Task SaveAsync(string dataDirectory, ClientErrorWrite write)
    {
        await using var context = CreateContext(dataDirectory);
        var repository = new ClientErrorRepository(context);

        await repository.SaveAsync(write, TestContext.Current.CancellationToken);
    }

    private static CatalogDbContext CreateContext(string dataDirectory)
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseSqlite($"Data Source={DatabasePath(dataDirectory)};Pooling=False")
            .Options;

        return new CatalogDbContext(options);
    }

    private static async Task ApplyMigrationsAsync(string dataDirectory)
    {
        await using var context = CreateContext(dataDirectory);

        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);

        // La tabla `CLIENT_ERROR` declara una clave foránea hacia `CLIENT_REGISTRATION`,
        // así que el cliente del error tiene que estar registrado (AD-22).
        context.ClientRegistrations.Add(new ClientRegistrationEntity
        {
            ClientId = ClientId,
            MachineName = "EQ-PC",
            ClientVersion = "1.0.0",
            ProtocolVersion = 3,
            ConnectionState = "anchor-verified",
            LastSeenAt = DateTimeOffset.UtcNow.ToString("O"),
        });

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<IReadOnlyList<string>> ReadErrorIdsAsync(string dataDirectory)
    {
        await using var connection = new SqliteConnection($"Data Source={DatabasePath(dataDirectory)}");
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT errorId FROM CLIENT_ERROR ORDER BY errorId";

        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var ids = new List<string>();

        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            ids.Add(reader.GetString(0));
        }

        return ids;
    }

    private static string DatabasePath(string dataDirectory) =>
        Path.Combine(dataDirectory, DataDirectory.CatalogDatabaseFileName);

    private static void TryDelete(string dataDirectory)
    {
        try
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
        catch (IOException)
        {
            // The operating system can still hold one of the files.
        }
    }
}
