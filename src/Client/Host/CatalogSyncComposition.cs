using LANPartyHub.Client.Application.Catalog;
using LANPartyHub.Client.Application.CatalogSync;
using LANPartyHub.Client.Application.Connection;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Infrastructure;
using LANPartyHub.Client.Infrastructure.Networking;
using LANPartyHub.Client.Infrastructure.Persistence;
using LANPartyHub.Shared.Contracts;
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
public sealed class CatalogSyncComposition : IAsyncDisposable, IServerReconnectHandler
{
    private readonly HttpClient _httpClient;
    private readonly ClientStateDbContext _context;
    private readonly CatalogHubClient _hubClient;
    private readonly ILogger<CatalogSyncComposition> _logger;
    private readonly CatalogSnapshotStore _store;
    private readonly ClientStateStore _stateStore;
    private readonly IServerConnectionGate? _connectionGate;
    private readonly IUiThread _uiThread;
    private readonly Lock _gate = new();

    private CatalogSyncState? _lastState;

    /// <summary>Creates the composition of the synchronization of the catalog.</summary>
    /// <param name="serverAddress">Address of the event server.</param>
    /// <param name="stateDatabasePath">Absolute path of the SQLite file of the client.</param>
    /// <param name="headers">Source of the identifiers of correlation of the requests.</param>
    /// <param name="loggerFactory">Factory of the log of the client.</param>
    /// <param name="connectionGate">
    /// Puerta de versión y del ancla fijado que la reconexión repite, or <c>null</c> when
    /// the composition cannot re-establish the connection (AC5, AD-6, AD-27).
    /// </param>
    /// <param name="uiThread">
    /// Hilo de la interfaz, que encola las notificaciones que llegan desde el grupo de
    /// hilos (AC3, AC4, AC5).
    /// </param>
    public CatalogSyncComposition(
        Uri serverAddress,
        string stateDatabasePath,
        IClientRequestHeaders headers,
        ILoggerFactory loggerFactory,
        IServerConnectionGate? connectionGate,
        IUiThread uiThread)
    {
        ArgumentNullException.ThrowIfNull(serverAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(stateDatabasePath);
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(uiThread);

        ServerAddress = serverAddress;

        // La publicación del estado se encola en el hilo de la interfaz: la reconexión
        // resincroniza el catálogo desde el grupo de hilos y un suscriptor que toque un
        // control desde ahí lo deja sin repintar para siempre (AC3, AC4, AC5).
        _uiThread = uiThread;

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

        // AC2: la última operación terminada se lee del MISMO archivo de estado del
        // cliente, así que el almacén comparte el contexto que la composición ya abrió.
        _stateStore = new ClientStateStore(_context);

        _logger = loggerFactory.CreateLogger<CatalogSyncComposition>();

        // AD-6: la versión del cliente es la del binario que se está ejecutando y la
        // versión del protocolo es la constante del contrato compartido, así que el
        // servidor puede decidir la compatibilidad de la petición (AD-6).
        var versions = new ClientBuildVersion();

        // El manejador de cabeceras es un DelegatingHandler y necesita un manejador
        // interior que sea el que abre la conexión: sin él, TODA petición del cliente
        // falla antes de salir y el catálogo nunca llega a la superficie (AD-15, AD-23).
        //
        // AD-6: el mismo manejador estampa la versión del cliente y la del protocolo en
        // TODA petición, de modo que el servidor puede rechazar con 426 la que no los
        // declara.
        _httpClient = new HttpClient(
            new ClientRequestHeadersHandler(headers, versions)
            {
                InnerHandler = new HttpClientHandler(),
            })
        {
            BaseAddress = serverAddress,
        };

        var snapshotClient = new CatalogHttpClient(_httpClient, versions);
        var store = new CatalogSnapshotStore(_context);

        _hubClient = new CatalogHubClient(
            serverAddress,
            loggerFactory.CreateLogger<CatalogHubClient>());

        var applier = new CatalogSnapshotApplier(
            store,
            loggerFactory.CreateLogger<CatalogSnapshotApplier>());

        _store = store;

        _connectionGate = connectionGate;

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
    /// Address of the event server that the composition talks to (AC1).
    /// </summary>
    public Uri ServerAddress { get; }

    /// <summary>
    /// Reads the installations that the client registered, which the ficha del videojuego
    /// consumes to derive the badge of the view state (AC3).
    ///
    /// <para>
    /// The reading is LOCAL and it does NOT ask the server: the installations belong to the
    /// client and the path of the installation never leaves it (AD-22).
    /// </para>
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The installations that the client registered.</returns>
    public async Task<IReadOnlyList<InstalledGameView>> LoadInstallationsAsync(
        CancellationToken cancellationToken)
    {
        var installations = await _store
            .LoadInstalledGamesAsync(cancellationToken)
            .ConfigureAwait(false);

        return installations
            .Select(installation => new InstalledGameView(
                installation.GameId,
                installation.InstalledVersion,
                installation.ValidationState))
            .ToArray();
    }

    /// <summary>
    /// Reads the last operation that FINISHED, so the persistent panel of the connection
    /// shows su resultado en vez de un espacio vacío (AC2, UX-DR-11).
    ///
    /// <para>
    /// La lectura es de SOLO LECTURA y no escribe nada: la última operación se lee de las
    /// operaciones de descarga que ya están persistidas, y no hay migración (AD-22).
    /// </para>
    /// <para>
    /// Una operación TERMINADA es la que está en un estado terminal: completada, cancelada
    /// o fallida. Una operación en curso NO es la última operación terminada (AD-10).
    /// </para>
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>
    /// The description of the last operation that finished, or <c>null</c> when the client
    /// has no finished operation yet.
    /// </returns>
    public async Task<string?> LoadLastFinishedOperationAsync(CancellationToken cancellationToken)
    {
        var operations = await _stateStore
            .LoadDownloadOperationsAsync(cancellationToken)
            .ConfigureAwait(false);

        var finished = operations
            .Where(operation => TryResolveTerminalState(operation.State, out _))
            .ToArray();

        if (finished.Length == 0)
        {
            return null;
        }

        var last = finished
            .OrderBy(operation => operation.OperationId, StringComparer.Ordinal)
            .Last();

        if (!TryResolveTerminalState(last.State, out var terminal) || terminal is null)
        {
            return null;
        }

        return $"{last.PackageId} · {StateDisplayMap.GetText(terminal.Value)}";
    }

    /// <summary>
    /// Resolves whether the state of an operation is TERMINAL, and which one is (AD-10).
    /// </summary>
    /// <param name="state">Name of the state of the operation.</param>
    /// <param name="terminal">The terminal state, when the state is terminal.</param>
    /// <returns><c>true</c> when the state is terminal.</returns>
    private static bool TryResolveTerminalState(string state, out DownloadState? terminal)
    {
        terminal = null;

        if (string.IsNullOrWhiteSpace(state))
        {
            return false;
        }

        if (!Enum.TryParse<DownloadState>(state, ignoreCase: false, out var parsed))
        {
            return false;
        }

        if (parsed is not (DownloadState.Completed or DownloadState.Cancelled or DownloadState.Failed))
        {
            return false;
        }

        terminal = parsed;

        return true;
    }

    /// <summary>
    /// Starts the synchronization: it subscribes to the trigger of a change of the catalog
    /// and it performs the first synchronization (AC1, AC-3).
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the synchronization has started.</returns>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // La suscripción al hub del servidor es un AVISO de los cambios del catálogo: si el
        // servidor no está disponible, la sincronización inicial se intenta igual y el
        // estado pendiente se publica, para que la superficie del catálogo sepa que no
        // hay conexión y ofrezca «Conectar al servidor» (AC3, AC4).
        try
        {
            await Service.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(
                exception,
                "No se pudo suscribir al aviso de cambios del catálogo: la sincronización inicial se intenta igual.");
        }

        var state = await Service.SynchronizeAsync(cancellationToken).ConfigureAwait(false);

        Publish(state);
    }

    /// <summary>
    /// Re-establishes the connection with the server of the event and resynchronizes the
    /// catalog, WITHOUT reinstalling anything (AC5).
    ///
    /// <para>
    /// AC5: la reconexión REPETE la puerta de versión (AD-6) y la validación del ancla
    /// fijado (AD-27), re-suscribe al hub del estado del cliente y llama a la
    /// sincronización del catálogo. El cliente recupera la conexión y vuelve a sincronizar
    /// el catálogo sin reinstalarse (AC5, AD-5, AD-26).
    /// </para>
    /// <para>
    /// La suscripción al hub es IDEMPOTENTE: el manejador de <c>CatalogChanged</c> se
    /// registra una sola vez, así que re-suscribir NO duplica el disparo de la
    /// resincronización (AC5, AD-5).
    /// </para>
    /// </summary>
    /// <param name="endpoint">Endpoint of the server of the event.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>
    /// <c>true</c> when the connection was re-established and the catalog was
    /// resynchronized, or <c>false</c> when the server could not be reached again.
    /// </returns>
    public async Task<bool> ReconnectAsync(
        ServerEndpoint endpoint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (_connectionGate is null)
        {
            return false;
        }

        ConnectionAttempt attempt;

        try
        {
            attempt = await _connectionGate
                .OpenAsync(endpoint, TimeSpan.FromSeconds(3), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "No se pudo reabrir la conexión con {ServerAddress}:{ServerPort}: la reconexión no se completa.",
                endpoint.Address,
                endpoint.Port);

            return false;
        }

        if (!attempt.IsConnected)
        {
            _logger.LogWarning(
                "La reconexión con {ServerAddress}:{ServerPort} no pasó la puerta de versión ni la del ancla fijado: la reconexión no se completa.",
                endpoint.Address,
                endpoint.Port);

            return false;
        }

        // La suscripción se hace de nuevo, y es idempotente: el hub se reinicia si hacía
        // falta y el manejador de `CatalogChanged` NO se duplica (AC5).
        await Service.StartAsync(cancellationToken).ConfigureAwait(false);

        var state = await Service.SynchronizeAsync(cancellationToken).ConfigureAwait(false);

        Publish(state);

        return true;
    }

    /// <summary>
    /// Raised when the state of the synchronization changes, so the notice of the interface
    /// can show the pending synchronization without blocking the navigation (AC3, UX-DR-32).
    ///
    /// <para>
    /// The subscription REPLAYS the last published state to the subscriber that arrives
    /// late: the first synchronization happens during the startup, BEFORE the window
    /// exists, so without the replay the state that carries the snapshot of the catalog
    /// would be lost and the surface of the catalog would never show the games (AC1).
    /// </para>
    /// <para>
    /// La notificación se ENCOLA en el hilo de la interfaz, porque la publicación llega
    /// desde el grupo de hilos cuando la reconexión resincroniza el catálogo. Un control
    /// de Avalonia SOLO se toca desde el hilo que lo posee (AC3, AC4, AC5).
    /// </para>
    /// </summary>
    public event Action<CatalogSyncState>? StateChanged
    {
        add
        {
            ArgumentNullException.ThrowIfNull(value);

            CatalogSyncState? replay;

            lock (_gate)
            {
                _stateChanged += value;

                replay = _lastState;
            }

            // El suscriptor que llega tarde recibe el ÚLTIMO estado: la primera
            // sincronización ocurre durante el arranque, antes de que exista la ventana
            // (AC1). App encola cualquier mutación de controles en el hilo de la interfaz;
            // entregar el replay aquí conserva el contrato síncrono del snapshot retenido.
            if (replay is not null)
            {
                value(replay);
            }
        }
        remove
        {
            lock (_gate)
            {
                _stateChanged -= value;
            }
        }
    }

    private Action<CatalogSyncState>? _stateChanged;

    /// <summary>
    /// Notifies ONE subscriber of the state, in the thread of the interface (AC3, AC4, AC5).
    ///
    /// <para>
    /// La notificación va encolada porque el suscriptor puede tocar un control de Avalonia,
    /// y un control SOLO se toca desde el hilo que lo posee (AC3).
    /// </para>
    /// </summary>
    /// <param name="subscriber">Suscriptor que se notifica.</param>
    /// <param name="state">State of the synchronization of the catalog.</param>
    private void NotifySubscriber(Action<CatalogSyncState> subscriber, CatalogSyncState state)
    {
        _uiThread.Post(() => subscriber(state));
    }

    /// <summary>
    /// Publishes the state of the synchronization to whoever observes it (AC3, UX-DR-32).
    ///
    /// <para>
    /// The state is RETAINED, so a subscriber that arrives after the publication receives
    /// it as soon as it subscribes (AC1).
    /// </para>
    /// <para>
    /// La publicación se NOTIFICA en el hilo de la interfaz: la reconexión resincroniza
    /// el catálogo desde el grupo de hilos y un suscriptor que toque un control desde ahí
    /// lo deja sin repintar para siempre (AC3, AC4, AC5).
    /// </para>
    /// </summary>
    /// <param name="state">State of the synchronization of the catalog.</param>
    public void Publish(CatalogSyncState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        Action<CatalogSyncState>? subscribers;

        lock (_gate)
        {
            _lastState = state;

            subscribers = _stateChanged;
        }

        if (subscribers is null)
        {
            return;
        }

        _uiThread.Post(() => subscribers(state));
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _hubClient.DisposeAsync().ConfigureAwait(false);

        _httpClient.Dispose();

        await _context.DisposeAsync().ConfigureAwait(false);
    }
}
