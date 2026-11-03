using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Shared.Contracts;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Client.Infrastructure.Networking;

/// <summary>
/// Adapter of the reception of the trigger of a change of the catalog (AD-5, AD-26).
///
/// <para>
/// The adapter listens to the hub <c>/hubs/client-state</c> of the event server and it
/// reacts to the event <see cref="CatalogChanged"/>, which is the trigger of the
/// resynchronization: it carries ONLY the version of the catalog and the client reads the
/// snapshot complete by REST (AD-5, AD-26).
/// </para>
/// <para>
/// The hub does not authenticate and does not authorize anybody: the LAN is trusted by
/// design and the only gating of the client is the compatibility of the version (AD-6).
/// </para>
/// <para>
/// A failure of the connection never erases the catalog that the client has: the
/// synchronization is what decides what to do with it, and the use case keeps the last
/// snapshot that it applied (AC3).
/// </para>
/// <para>
/// La suscripción es IDEMPOTENTE: el manejador de <c>CatalogChanged</c> se registra UNA
/// sola vez, así que re-suscribir al hub tras una caída NO duplica el disparo de la
/// resincronización (AC5, AD-5).
/// </para>
/// </summary>
public sealed class CatalogHubClient : ICatalogChangeNotifier, IAsyncDisposable
{
    /// <summary>Route of the hub of the state of the client.</summary>
    public const string Route = "/hubs/client-state";

    /// <summary>
    /// Name of the event that triggers the resynchronization of the catalog (AD-5).
    /// </summary>
    public const string CatalogChangedEventName = nameof(CatalogChanged);

    private readonly HubConnection _connection;
    private readonly Uri _serverAddress;
    private readonly ILogger<CatalogHubClient> _logger;
    private readonly Lock _gate = new();

    private Func<CatalogChanged, CancellationToken, Task>? _handler;
    private bool _subscribed;

    /// <summary>Creates the adapter of the reception of the trigger of a change.</summary>
    /// <param name="serverAddress">Address of the event server.</param>
    /// <param name="logger">Log of the adapter.</param>
    public CatalogHubClient(Uri serverAddress, ILogger<CatalogHubClient> logger)
    {
        ArgumentNullException.ThrowIfNull(serverAddress);
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        _serverAddress = serverAddress;

        var hubAddress = new Uri(serverAddress, Route);

        _connection = new HubConnectionBuilder()
            .WithUrl(hubAddress)
            .Build();
    }

    /// <summary>
    /// Raised when the connection with the hub is CLOSED, so the client can show the
    /// availability of the server and reintentar sin reinstalarse (AC3, AC4, AC5).
    ///
    /// <para>
    /// La transición de la conexión es un hecho del TRANSPORTE: no es un evento de hub, y
    /// el cliente no publica ningún evento de hub por ella (AD-29, AR-16).
    /// </para>
    /// </summary>
    public event Action<Exception?>? Closed;

    /// <summary>
    /// Raised when the connection with the hub is ESTABLISHED again, after a fall (AC5).
    /// </summary>
    public event Action? Reconnected;

    /// <inheritdoc />
    public async Task SubscribeAsync(
        Func<CatalogChanged, CancellationToken, Task> handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_gate)
        {
            // El manejador de `CatalogChanged` se registra UNA sola vez: re-suscribir tras
            // una caída NO puede duplicar el disparo de la resincronización (AC5, AD-5).
            if (!_subscribed)
            {
                _handler = handler;

                // El evento llega con el payload exacto de Shared: la versión del catálogo y
                // nada más (AD-18, AD-26).
                _connection.On<CatalogChanged>(
                    CatalogChangedEventName,
                    changed => _handler!(changed, CancellationToken.None));

                _connection.Reconnected += _ =>
                {
                    _logger.LogInformation("Se ha restablecido la conexión con el hub del estado del cliente.");

                    Reconnected?.Invoke();

                    return Task.CompletedTask;
                };

                _connection.Closed += exception =>
                {
                    if (exception is null)
                    {
                        _logger.LogInformation("Se ha cerrado la conexión con el hub del estado del cliente.");
                    }
                    else
                    {
                        _logger.LogWarning(
                            exception,
                            "Se ha cerrado la conexión con el hub del estado del cliente con un error.");
                    }

                    Closed?.Invoke(exception);

                    return Task.CompletedTask;
                };

                _subscribed = true;
            }
        }

        if (_connection.State != HubConnectionState.Connected)
        {
            await _connection.StartAsync(cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "El cliente está suscrito al hub {HubRoute} del servidor {ServerAddress}.",
            Route,
            _serverAddress);
    }

    /// <summary>
    /// Whether the client is subscribed to the hub of the state of the client (AC5).
    /// </summary>
    public bool IsSubscribed
    {
        get
        {
            lock (_gate)
            {
                return _subscribed;
            }
        }
    }

    /// <summary>
    /// Whether the connection with the hub is ESTABLECIDA (AC5).
    /// </summary>
    public bool IsConnected => _connection.State == HubConnectionState.Connected;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync().ConfigureAwait(false);
    }
}
