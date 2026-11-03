using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LANPartyHub.Client.Application.Connection;

/// <summary>
/// Supervisor of the availability of the event server (AC3, AC4, AC5, FR-3, NFR-9).
///
/// <para>
/// El supervisor sondea el servidor y PUBLICA su disponibilidad: cuando el servidor no
/// responde, el panel de conexión pasa a aviso con un reintento automático y un contador
/// visible, y NO se abre ningún modal que interrumpa al jugador (AC3, AC4, UX-DR-29).
/// </para>
/// <para>
/// Cuando el servidor vuelve DESPUÉS de una caída, el supervisor llama al manejador de la
/// reconexión, que REPETE la puerta de versión (AD-6) y la validación del ancla fijado
/// (AD-27), re-suscribe al hub y resincroniza el catálogo. El cliente se recupera SIN
/// reinstalarse (AC5).
/// </para>
/// <para>
/// La reconexión es la respuesta a una CAÍDA, no al estado permanente: con el servidor
/// SANO, el sondeo periódico se limita a publicar la disponibilidad y NO restablece la
/// conexión. Así el cliente no abre un socket, no repite la puerta y no descarga el
/// catálogo completo en cada sondeo (AC5).
/// </para>
/// <para>
/// La disponibilidad que el supervisor publica es la del SERVIDOR: el punto de presencia
/// dice que el servidor está en pie. El rechazo de la puerta lo publica su propio
/// diagnóstico, que es de seguridad y no de red (AD-6, AD-27).
/// </para>
/// <para>
/// El supervisor depende de los puertos declarados en esta capa y nunca de un adaptador,
/// así que la regla del reintento se puede ejercitar sin una red (AD-1).
/// </para>
/// </summary>
public sealed class ServerAvailabilityMonitor
{
    /// <summary>
    /// Time that the FIRST retry waits, when the server does not answer.
    ///
    /// <para>
    /// El primer reintento es PRONTO: el servidor puede que se haya reiniciado y el
    /// jugador no tiene que esperar a que expire un backoff largo (AC5).
    /// </para>
    /// </summary>
    public static readonly TimeSpan DefaultInitialRetryDelay = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Longest time that a retry waits: el backoff tiene TOPE, así que un servidor que
    /// sigue caído no convierte la espera en un intervalo enorme (AC4).
    /// </summary>
    public static readonly TimeSpan DefaultMaximumRetryDelay = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Time between two checks of the availability, when the server IS available.
    /// </summary>
    public static readonly TimeSpan DefaultAvailablePollInterval = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Time that the probe of the availability lasts at most.
    /// </summary>
    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Granularity of the countdown of the retry: la cuenta atrás del reintento se publica
    /// en pasos de un cuarto de segundo (AC4).
    /// </summary>
    public static readonly TimeSpan CountdownGranularity = TimeSpan.FromMilliseconds(250);

    private readonly IServerVersionProbe _versionProbe;
    private readonly IServerReconnectHandler? _reconnectHandler;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ServerAvailabilityMonitor> _logger;
    private readonly Lock _gate = new();

    private ServerAvailability _availability;
    private CancellationTokenSource? _cancellation;
    private Task? _supervision;

    /// <summary>Creates the supervisor of the availability of the event server.</summary>
    /// <param name="versionProbe">Port of the probe of the versions of the server.</param>
    /// <param name="reconnectHandler">
    /// Port of the reconexión after the server comes back, or <c>null</c> when the client
    /// only observes the availability.
    /// </param>
    /// <param name="logger">Log of the supervisor.</param>
    /// <param name="timeProvider">
    /// Source of the instant and of la espera. It defaults to the real one and it exists so
    /// that the countdown of the retry can be exercised with a known clock.
    /// </param>
    public ServerAvailabilityMonitor(
        IServerVersionProbe versionProbe,
        IServerReconnectHandler? reconnectHandler = null,
        ILogger<ServerAvailabilityMonitor>? logger = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(versionProbe);

        _versionProbe = versionProbe;
        _reconnectHandler = reconnectHandler;
        _logger = logger ?? NullLogger<ServerAvailabilityMonitor>.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;

        _availability = new ServerAvailability(
            Endpoint: null,
            ServerName: null,
            ServerVersion: null,
            Available: false);
    }

    /// <summary>
    /// Raised when the availability of the server changes, so the panel of the connection
    /// can show the state of the presence, el reintento y su contador (AC3, AC4).
    /// </summary>
    public event Action<ServerAvailability>? AvailabilityChanged;

    /// <summary>
    /// The last availability that the supervisor published (AC3).
    /// </summary>
    public ServerAvailability Availability
    {
        get
        {
            lock (_gate)
            {
                return _availability;
            }
        }
    }

    /// <summary>
    /// Time that the FIRST retry waits, when the server does not answer (AC5).
    ///
    /// <para>
    /// El valor por defecto es <see cref="DefaultInitialRetryDelay"/> y existe como
    /// propiedad para que la regla del reintento se pueda ejercitar con un reloj conocido
    /// (AD-1).
    /// </para>
    /// </summary>
    public TimeSpan InitialRetryDelay { get; init; } = DefaultInitialRetryDelay;

    /// <summary>
    /// Longest time that a retry waits: el backoff tiene TOPE (AC4).
    /// </summary>
    public TimeSpan MaximumRetryDelay { get; init; } = DefaultMaximumRetryDelay;

    /// <summary>
    /// Time between two checks of the availability, when the server IS available.
    /// </summary>
    public TimeSpan AvailablePollInterval { get; init; } = DefaultAvailablePollInterval;

    /// <summary>
    /// Starts the supervision of the availability of the server (AC3, AC4).
    ///
    /// <para>
    /// La supervisión NO bloquea: el llamador recibe una tarea que se completa cuando la
    /// supervisión ha ARRANCADO, y la supervisión sigue viva por su cuenta (AC4).
    /// </para>
    /// </summary>
    /// <param name="endpoint">Endpoint of the server of the event.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the supervision has started.</returns>
    public Task StartAsync(ServerEndpoint endpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        lock (_gate)
        {
            if (_supervision is not null)
            {
                return Task.CompletedTask;
            }

            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _supervision = SuperviseAsync(endpoint, _cancellation.Token);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops the supervision of the availability (AC4).
    /// </summary>
    /// <returns>A task that completes when the supervision has stopped.</returns>
    public async Task StopAsync()
    {
        CancellationTokenSource? cancellation;
        Task? supervision;

        lock (_gate)
        {
            cancellation = _cancellation;
            supervision = _supervision;

            _cancellation = null;
            _supervision = null;
        }

        if (cancellation is null)
        {
            return;
        }

        await cancellation.CancelAsync().ConfigureAwait(false);

        if (supervision is not null)
        {
            try
            {
                await supervision.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // La cancelación la pidió el supervisor al detenerse: no es un fallo.
            }
        }

        cancellation.Dispose();
    }

    /// <summary>
    /// Runs the loop of the supervision: it probes, it publishes and it waits (AC3, AC4).
    /// </summary>
    /// <param name="endpoint">Endpoint of the server of the event.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the supervision stops.</returns>
    private async Task SuperviseAsync(ServerEndpoint endpoint, CancellationToken cancellationToken)
    {
        var retryDelay = InitialRetryDelay;

        // La reconexión se debe a una CAÍDA: el cliente ya abrió su conexión al
        // arrancar, así que con el servidor sano NO hay nada que restablecer. La bandera
        // se levanta cuando el servidor NO responde y se baja cuando la reconexión
        // termina bien (AC5).
        var recoveryPending = false;

        while (!cancellationToken.IsCancellationRequested)
        {
            var probe = await ProbeAsync(endpoint, cancellationToken).ConfigureAwait(false);

            if (probe is null)
            {
                // La sonda no pudo pronunciarse: el servidor NO está disponible y el
                // reintento sigue su curso (AC4).
                //
                // La caída deja una RECUPERACIÓN pendiente: cuando el servidor vuelva, el
                // cliente tiene que restablecer su conexión (AC5).
                recoveryPending = true;

                PublishUnavailable(endpoint, retryDelay);

                if (!await CountdownAsync(retryDelay, cancellationToken).ConfigureAwait(false))
                {
                    return;
                }

                retryDelay = NextRetryDelay(retryDelay);

                continue;
            }

            // El servidor volvió: el punto de presencia pasa a disponible (AC1, AC3).
            PublishAvailable(endpoint, probe);

            if (!recoveryPending)
            {
                // El servidor está SANO y el cliente NO pasó por una caída: NO hay nada
                // que restablecer. El sondeo periódico sigue publicando la
                // disponibilidad, y el cliente NO abre un socket, NO repite la puerta de
                // versión y NO descarga el catálogo en cada sondeo (AC5).
                if (!await DelayAsync(AvailablePollInterval, cancellationToken).ConfigureAwait(false))
                {
                    return;
                }

                continue;
            }

            // AD-6, AD-27: la reconexión REPETE la puerta de versión y la validación del
            // ancla fijado. El servidor que responde puede ser otro, así que la confianza
            // de la sesión anterior NO se hereda (AC5).
            //
            // Una reconexión que NO pasa la puerta no cambia la disponibilidad del
            // SERVIDOR: el servidor está en pie, y el cliente reintenta. El motivo del
            // rechazo lo publica su propio diagnóstico (AD-6, AD-27).
            var reconnected = await ReconnectAsync(endpoint, cancellationToken).ConfigureAwait(false);

            if (!reconnected)
            {
                // La recuperación sigue PENDIENTE: el siguiente sondeo vuelve a intentar
                // la reconexión, porque el cliente todavía no recuperó su conexión (AC4,
                // AC5).
                if (!await CountdownAsync(retryDelay, cancellationToken).ConfigureAwait(false))
                {
                    return;
                }

                retryDelay = NextRetryDelay(retryDelay);

                continue;
            }

            // El cliente recuperó la conexión y resincronizó el catálogo SIN
            // reinstalarse (AC5). La recuperación queda dischargeada: los sondeos
            // siguientes NO vuelven a reconectar (AC5).
            recoveryPending = false;

            retryDelay = InitialRetryDelay;

            if (!await DelayAsync(AvailablePollInterval, cancellationToken).ConfigureAwait(false))
            {
                return;
            }
        }
    }

    /// <summary>
    /// Probes the availability of the server and resolves whether it answered (AC4).
    ///
    /// <param name="endpoint">Endpoint of the server of the event.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>
    /// <c>null</c> when the server did NOT answer, or the result of the probe when it did.
    /// </returns>
    private async Task<ServerVersionProbeResult?> ProbeAsync(
        ServerEndpoint endpoint,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _versionProbe
                .ProbeAsync(endpoint, ProbeTimeout, cancellationToken)
                .ConfigureAwait(false);

            return result.Reachable ? result : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // La sonda NO afirma la incompatibilidad: el servidor puede no responder y el
            // jugador tiene que poder reintentar (AC3, AC4).
            _logger.LogWarning(
                exception,
                "No se pudo comprobar la disponibilidad del servidor {ServerAddress}:{ServerPort}.",
                endpoint.Address,
                endpoint.Port);

            return null;
        }
    }

    /// <summary>
    /// Re-establishes the connection and resynchronizes the catalog (AC5).
    ///
    /// <para>
    /// Solo se llama cuando el cliente tiene una RECUPERACIÓN pendiente, es decir, cuando
    /// el servidor estuvo caído y volvió: con el servidor sano NO se restablece nada
    /// (AC5).
    /// </para>
    ///
    /// <param name="endpoint">Endpoint of the server of the event.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns><c>true</c> when the reconexión succeeded.</returns>
    private async Task<bool> ReconnectAsync(
        ServerEndpoint endpoint,
        CancellationToken cancellationToken)
    {
        if (_reconnectHandler is null)
        {
            return false;
        }

        try
        {
            var reconnected = await _reconnectHandler
                .ReconnectAsync(endpoint, cancellationToken)
                .ConfigureAwait(false);

            if (reconnected)
            {
                _logger.LogInformation(
                    "Se ha restablecido la conexión con el servidor {ServerAddress}:{ServerPort} y el catálogo se ha resincronizado.",
                    endpoint.Address,
                    endpoint.Port);
            }

            return reconnected;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // La reconexión NO puede impedir que el supervisor siga vigilando: el siguiente
            // reintento lo intentará otra vez (AC4, AC5).
            _logger.LogWarning(
                exception,
                "No se pudo restablecer la conexión con {ServerAddress}:{ServerPort}: el reintento continúa.",
                endpoint.Address,
                endpoint.Port);

            return false;
        }
    }

    /// <summary>
    /// Publishes the availability of a server that answered (AC1).
    /// </summary>
    /// <param name="endpoint">Endpoint of the server of the event.</param>
    /// <param name="probe">Result of the probe of the versions of the server.</param>
    private void PublishAvailable(ServerEndpoint endpoint, ServerVersionProbeResult probe) =>
        Publish(new ServerAvailability(
            endpoint,
            probe.ServerName,
            probe.ServerVersion,
            Available: true));

    /// <summary>
    /// Publishes the availability of a server that did NOT answer (AC3, AC4).
    ///
    /// <param name="endpoint">Endpoint of the server of the event.</param>
    /// <param name="retryDelay">Time until the next retry.</param>
    private void PublishUnavailable(ServerEndpoint endpoint, TimeSpan retryDelay)
    {
        var previous = Availability;

        Publish(new ServerAvailability(
            endpoint,
            previous.ServerName,
            previous.ServerVersion,
            Available: false,
            retryDelay));
    }

    /// <summary>
    /// Publishes the availability to whoever observes it (AC3, AC4).
    ///
    /// <param name="availability">Availability of the server.</param>
    private void Publish(ServerAvailability availability)
    {
        lock (_gate)
        {
            _availability = availability;
        }

        AvailabilityChanged?.Invoke(availability);
    }

    /// <summary>
    /// Waits for the retry, publishing the countdown that the player sees (AC4).
    ///
    /// <para>
    /// La cuenta atrás se publica en pasos: el contador del panel baja de «Reintentando ·
    /// 3 s» a «Reintentando · 2 s» y así hasta cero (AC4, UX-DR-29).
    /// </para>
    /// </summary>
    /// <param name="delay">Time that the retry waits.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns><c>false</c> when the wait was cancelled.</returns>
    private async Task<bool> CountdownAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        var remaining = delay;

        while (remaining > TimeSpan.Zero)
        {
            var previous = Availability;

            Publish(previous with { RetryCountdown = remaining });

            var step = remaining < CountdownGranularity ? remaining : CountdownGranularity;

            if (!await DelayAsync(step, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            remaining -= step;

            if (remaining < TimeSpan.Zero)
            {
                remaining = TimeSpan.Zero;
            }
        }

        return true;
    }

    /// <summary>
    /// Waits for the given time, using the source of the instant of the supervisor.
    ///
    /// <param name="delay">Time to wait.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns><c>false</c> when the wait was cancelled.</returns>
    private async Task<bool> DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// Computes the delay of the NEXT retry: the backoff CRECE con TOPE (AC4).
    ///
    /// <param name="current">Delay of the current retry.</param>
    /// <returns>The delay of the next retry.</returns>
    public TimeSpan NextRetryDelay(TimeSpan current)
    {
        var next = TimeSpan.FromTicks(current.Ticks * 2);

        return next > MaximumRetryDelay ? MaximumRetryDelay : next;
    }
}
