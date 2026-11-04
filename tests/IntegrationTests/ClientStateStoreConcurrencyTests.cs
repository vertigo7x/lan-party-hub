using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LANPartyHub.IntegrationTests;

/// <summary>
/// Concurrency of the access to the database of the client (AD-11, AD-24, SM-2).
///
/// <para>
/// La descarga PERSISTE el offset acumulado mientras corre (AD-11, SM-2) y la
/// publicación del progreso LEE la identidad al mismo tiempo, así que las dos operaciones
/// comparten el mismo contexto de EF Core. Un <see cref="DbContext"/> NO es seguro entre
/// hilos: sin serializar el acceso, EF Core lo rechaza con «A second operation was started
/// on this context instance before a previous operation completed» y la descarga se
/// rompe a mitad.
/// </para>
/// <para>
/// La prueba fuerza el solapamiento de las dos operaciones sobre el MISMO contexto, que
/// es lo que hace el cliente real, y comprueba que las dos terminan bien.
/// </para>
/// </summary>
public sealed class ClientStateStoreConcurrencyTests : IDisposable
{
    private readonly string _workingDirectory;
    private readonly string _databasePath;

    /// <summary>Creates an isolated working directory for the test.</summary>
    public ClientStateStoreConcurrencyTests()
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
    public async Task Writing_the_progress_and_reading_the_operations_do_not_collide_on_the_same_context()
    {
        // AD-11, SM-2: la escritura del offset y la lectura de las operaciones
        // persistidas comparten el contexto del cliente.
        await using var context = CreateContext();

        var store = new ClientStateStore(context);

        await store.SaveOperationAsync(
            "0193f2a1-b2c3-4d4e-5f60-718293a4b5c6",
            "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e70",
            "Downloading",
            "http-range",
            TestContext.Current.CancellationToken);

        // Las dos operaciones se lanzan A LA VEZ, así que el solapamiento sobre el
        // contexto es real y no una casualidad del planificador.
        var writing = Task.Run(async () =>
        {
            for (var offset = 1; offset <= 50; offset++)
            {
                await store.SaveProgressAsync(
                    "0193f2a1-b2c3-4d4e-5f60-718293a4b5c6",
                    offset * 1024L,
                    7_600_000_000L,
                    TestContext.Current.CancellationToken);
            }
        }, TestContext.Current.CancellationToken);

        var reading = Task.Run(async () =>
        {
            for (var attempt = 0; attempt < 50; attempt++)
            {
                await store.LoadDownloadOperationsAsync(TestContext.Current.CancellationToken);
                await store.LoadIdentityAsync(TestContext.Current.CancellationToken);
            }
        }, TestContext.Current.CancellationToken);

        // Sin serializar el acceso, EF Core lanza aquí
        // «InvalidOperationException: A second operation was started on this context
        // instance before a previous operation completed».
        await Task.WhenAll(writing, reading);
    }

    [Fact]
    public async Task The_persisted_offset_is_the_last_one_that_was_written()
    {
        await using var context = CreateContext();

        var store = new ClientStateStore(context);

        await store.SaveOperationAsync(
            "0193f2a1-b2c3-4d4e-5f60-718293a4b5c6",
            "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e70",
            "Downloading",
            "http-range",
            TestContext.Current.CancellationToken);

        var writing = Task.Run(async () =>
        {
            for (var offset = 1; offset <= 50; offset++)
            {
                await store.SaveProgressAsync(
                    "0193f2a1-b2c3-4d4e-5f60-718293a4b5c6",
                    offset * 1024L,
                    7_600_000_000L,
                    TestContext.Current.CancellationToken);
            }
        }, TestContext.Current.CancellationToken);

        var reading = Task.Run(async () =>
        {
            for (var attempt = 0; attempt < 50; attempt++)
            {
                await store.LoadDownloadOperationsAsync(TestContext.Current.CancellationToken);
            }
        }, TestContext.Current.CancellationToken);

        await Task.WhenAll(writing, reading);

        var operations = await store.LoadDownloadOperationsAsync(TestContext.Current.CancellationToken);

        var operation = Assert.Single(operations);

        Assert.Equal(50 * 1024L, operation.BytesPersisted);
        Assert.Equal(7_600_000_000L, operation.TotalBytes);
    }

    /// <summary>
    /// Creates the context over the real SQLite file of the test (AD-11, AD-16).
    /// </summary>
    /// <returns>The context of the database of the client.</returns>
    private ClientStateDbContext CreateContext()
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

        var context = new ClientStateDbContext(options);

        context.Database.Migrate();

        return context;
    }
}
