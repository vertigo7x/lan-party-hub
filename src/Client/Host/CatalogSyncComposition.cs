using LANPartyHub.Client.Application.CatalogSync;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Infrastructure.Networking;
using LANPartyHub.Client.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Client.Host;

/// <summary>
/// Composition of the synchronization of the catalog of the client (AD-1, AD-5, AD-26).
///
/// <para>
/// The composition opens the composition that the synchronization needs: the client of the
/// server, the store of the snapshot and the service that applies it. It lives as long as
/// the window of the player, because the synchronization has to react to the trigger of
/// the hub while the client runs.
/// </para>
/// <para>
/// The composition is the ONLY place that knows the adapters: the use case depends on the
/// ports and never on them (AD-1).
/// </para>
/// </summary>
public sealed class CatalogSyncComposition : IAsyncDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ClientStateDbContext _context;
    private readonly CatalogHubClient _hubClient;

    /// <summary>Creates the composition of the synchronization of the catalog.</summary>
    /// <param name="serverAddress">Address of the event server.</param>
    /// <param name="stateDatabasePath">Absolute path of the SQLite file of the client.</param>
    /// <param name="headers">Source of the identifiers of correlation of the requests.</param>
    /// <param name="loggerFactory">Factory of the log of the client.</param>
    public CatalogSyncComposition(
        Uri serverAddress,
        string stateDatabasePath,
        IClientRequestHeaders headers,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(serverAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(stateDatabasePath);
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        // El pool está desactivado porque la base de datos se abre pocas veces y un
        // handle agrupado mantendría el archivo de la carpeta de datos bloqueado mientras
        // el cliente corre (AD-11, AD-17).
        var connectionString = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = stateDatabasePath,
            Pooling = false,
        }.ToString();

        var options = new DbContextOptionsBuilder<ClientStateDbContext>()
            .UseSqlite(
                connectionString,
                sqlite => sqlite.MigrationsAssembly(typeof(ClientStateDbContext).Assembly.GetName().Name))
            .Options;

        _context = new ClientStateDbContext(options);

        _httpClient = new HttpClient(new CorrelationHeadersHandler(headers))
        {
            BaseAddress = serverAddress,
        };

        var snapshotClient = new CatalogHttpClient(_httpClient);
        var store = new CatalogSnapshotStore(_context);

        _hubClient = new CatalogHubClient(
            serverAddress,
            loggerFactory.CreateLogger<CatalogHubClient>());

        var applier = new CatalogSnapshotApplier(
            store,
            loggerFactory.CreateLogger<CatalogSnapshotApplier>());

        Service = new CatalogSyncService(
            snapshotClient,
            store,
            _hubClient,
            applier,
            loggerFactory.CreateLogger<CatalogSyncService>());
    }

    /// <summary>The use case of the synchronization of the catalog.</summary>
    public CatalogSyncService Service { get; }

    /// <summary>
    /// Starts the synchronization: it subscribes to the trigger of a change of the catalog
    /// and it performs the first synchronization (AC1, AC-3).
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the synchronization has started.</returns>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await Service.StartAsync(cancellationToken).ConfigureAwait(false);

        var state = await Service.SynchronizeAsync(cancellationToken).ConfigureAwait(false);

        Publish(state);
    }

    /// <summary>
    /// Raised when the state of the synchronization changes, so the notice of the interface
    /// can show the pending synchronization without blocking the navigation (AC3, UX-DR-32).
    /// </summary>
    public event Action<CatalogSyncState>? StateChanged;

    /// <summary>
    /// Publishes the state of the synchronization to whoever observes it (AC3, UX-DR-32).
    /// </summary>
    /// <param name="state">State of the synchronization of the catalog.</param>
    public void Publish(CatalogSyncState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        StateChanged?.Invoke(state);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _hubClient.DisposeAsync().ConfigureAwait(false);

        _httpClient.Dispose();

        await _context.DisposeAsync().ConfigureAwait(false);
    }
}
