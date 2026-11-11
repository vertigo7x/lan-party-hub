using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Shared.Contracts;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Client.Infrastructure.Networking;

/// <summary>
/// Adapter of the publication of the progress of a download operation (AD-22, AR-3, AC5).
///
/// <para>
/// El adaptador publica en el hub <c>/hubs/client-state</c> del servidor del evento, que es
/// el ÚNICO canal de push del sistema. El método que recibe la publicación es
/// <see cref="DownloadProgressUpdatedMethodName"/>, y el nombre queda FIJADO aquí: la
/// historia que proyecta el progreso en el servidor lo hereda (AD-22, AD-26).
/// </para>
/// <para>
/// El payload es el CONTRATO EXACTO <see cref="DownloadProgressUpdated"/>: el identificador
/// del cliente, el de la operación, los bytes transferidos, el total y el estado de la
/// máquina. El contrato de <c>Shared</c> no crece y el inventario de contratos queda
/// intacto (AD-18, AR-3).
/// </para>
/// <para>
/// El hub todavía NO declara el método que recibe la publicación: el servidor lo recibe
/// en su propia historia. El adaptador TOLERA esa ausencia y la REGISTRA, así que la
/// descarga del cliente no se rompe por una proyección que todavía no existe (AD-22).
/// </para>
/// <para>
/// La publicación NUNCA impide la descarga: una falla del hub se registra y se sigue,
/// porque lo que sobrevive al reinicio es el offset persistido en el disco del cliente
/// (AD-11).
/// </para>
/// </summary>
public sealed class ClientStateHubPublisher :
    IDownloadProgressReporter,
    IClientStatePublisher,
    IClientConnectionPublisher,
    IClientErrorPublisher,
    IInstallationProgressReporter,
    IOperationOutcomeReporter,
    IAsyncDisposable
{
    /// <summary>Route of the hub of the state of the client.</summary>
    public const string Route = "/hubs/client-state";

    /// <summary>
    /// Name of the method of the hub that receives the progress of a download operation
    /// (AD-22, AR-3).
    /// </summary>
    public const string DownloadProgressUpdatedMethodName = nameof(DownloadProgressUpdated);

    /// <summary>
    /// Name of the method of the hub that receives the state of the client (AD-18, AD-29).
    /// </summary>
    public const string ClientStateChangedMethodName = nameof(ClientStateChanged);

    /// <summary>
    /// Name of the method of the hub that receives the progress of an installation
    /// operation (AD-22).
    /// </summary>
    public const string InstallationProgressUpdatedMethodName = nameof(InstallationProgressUpdated);

    /// <summary>
    /// Name of the method of the hub that receives the TERMINAL result of an operation
    /// (AD-33).
    /// </summary>
    public const string OperationOutcomeReportedMethodName = nameof(OperationOutcomeReported);

    /// <summary>
    /// Name of the method of the hub that receives the CONNECTION state ALONE
    /// (AD-18, AD-29).
    /// </summary>
    public const string ClientConnectionChangedMethodName = nameof(ClientConnectionChanged);

    /// <summary>
    /// Name of the method of the hub that receives the ERROR that the client registers
    /// (AD-15, AD-18, AD-22).
    /// </summary>
    public const string ClientErrorReportedMethodName = nameof(ClientErrorReported);

    private readonly HubConnection _connection;
    private readonly ILogger<ClientStateHubPublisher> _logger;
    private readonly Lock _gate = new();

    private bool _started;

    /// <summary>Creates the adapter of the publication of the progress.</summary>
    /// <param name="serverAddress">Address of the event server.</param>
    /// <param name="logger">Log of the adapter.</param>
    public ClientStateHubPublisher(Uri serverAddress, ILogger<ClientStateHubPublisher> logger)
    {
        ArgumentNullException.ThrowIfNull(serverAddress);
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;

        _connection = new HubConnectionBuilder()
            .WithUrl(new Uri(serverAddress, Route))
            .Build();
    }

    /// <summary>
    /// Whether the connection with the hub is ESTABLECIDA (AD-22).
    /// </summary>
    public bool IsConnected => _connection.State == HubConnectionState.Connected;

    /// <inheritdoc />
    public async Task ReportAsync(DownloadProgressUpdated progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);

        try
        {
            await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);

            if (_connection.State != HubConnectionState.Connected)
            {
                // El servidor no está: la proyección del progreso se pierde, pero el offset
                // persistido NO se pierde y la descarga continúa (AD-11, AD-22).
                _logger.LogWarning(
                    "No se pudo publicar el progreso de la operación {OperationId}: el hub del estado del cliente no está conectado.",
                    progress.OperationId);

                return;
            }

            await _connection
                .InvokeAsync(DownloadProgressUpdatedMethodName, progress, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // AD-22: el servidor todavía no declara el método que recibe la publicación, y
            // el hub responde con un error al invocar un método que no existe. El
            // adaptador TOLERA esa ausencia y la REGISTRA: la proyección del progreso es
            // de otra historia y no puede romper la descarga del cliente (AD-11).
            _logger.LogWarning(
                exception,
                "No se pudo publicar el progreso de la operación {OperationId}: el servidor no aceptó la publicación.",
                progress.OperationId);
        }
    }

    /// <inheritdoc />
    public async Task PublishAsync(ClientStateChanged state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);

        await InvokeAsync(
            ClientStateChangedMethodName,
            state,
            state.ClientId,
            "el estado del cliente",
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Publishes the CONNECTION state of the client ALONE, so the server can reflect the
    /// disconnection without emptying the projection it already holds (AD-18, AD-29).
    ///
    /// <para>
    /// La publicación NO lleva juegos ni operaciones: es el UNICO caso en el que el
    /// cliente comunica su conexión sin poder sostener una instantánea completa de su
    /// estado (AD-22, AD-29, AD-32).
    /// </para>
    /// </summary>
    /// <param name="changed">Connection state that the client declares.</param>
    /// <param name="cancellationToken">Token that cancels the publication.</param>
    /// <returns>A task that completes when the publication has been attempted.</returns>
    public async Task PublishAsync(ClientConnectionChanged changed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changed);

        await InvokeAsync(
            ClientConnectionChangedMethodName,
            changed,
            changed.ClientId,
            "la conexión del cliente",
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Publishes the REAL phase of an installation operation, so the server can project
    /// it and the table of clients shows the phase REAL (AD-22, FR-10).
    ///
    /// <para>
    /// El payload NO lleva porcentaje: un porcentaje sobre un total que no se conoce
    /// sería una mentira, así que la publicación conserva la fase tal cual (AD-22).
    /// </para>
    /// </summary>
    /// <param name="progress">Progress of the installation that the client declares.</param>
    /// <param name="cancellationToken">Token that cancels the publication.</param>
    /// <returns>A task that completes when the publication has been attempted.</returns>
    public async Task ReportAsync(
        InstallationProgressUpdated progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);

        await InvokeAsync(
            InstallationProgressUpdatedMethodName,
            progress,
            progress.ClientId,
            "el progreso de la instalación",
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Publishes the TERMINAL result of an operation, so the server can show it as a
    /// RESULT and never as an error (AD-33, R-MVP-8).
    /// </summary>
    /// <param name="reported">Result that the client declares.</param>
    /// <param name="cancellationToken">Token that cancels the publication.</param>
    /// <returns>A task that completes when the publication has been attempted.</returns>
    public async Task ReportAsync(
        OperationOutcomeReported reported,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reported);

        await InvokeAsync(
            OperationOutcomeReportedMethodName,
            reported,
            reported.ClientId,
            "el resultado de la operación",
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task PublishErrorAsync(
        ClientErrorReported reported,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reported);

        return InvokeAsync(
            ClientErrorReportedMethodName,
            reported,
            reported.ClientId,
            "el error del cliente",
            cancellationToken);
    }

    /// <summary>
    /// Invokes a method of the hub, TOLERANDO que el servidor todavía NO lo declare: la
    /// proyección es un complemento y no puede romper al jugador (AD-1, AD-22).
    /// </summary>
    /// <typeparam name="TPayload">Type of the published payload.</typeparam>
    /// <param name="methodName">Name of the method of the hub.</param>
    /// <param name="payload">Payload of the publication.</param>
    /// <param name="clientId">Identifier of the client, for the diagnostic.</param>
    /// <param name="description">Description of what is published, for the diagnostic.</param>
    /// <param name="cancellationToken">Token that cancels the publication.</param>
    /// <returns>A task that completes when the publication has been attempted.</returns>
    private async Task InvokeAsync<TPayload>(
        string methodName,
        TPayload payload,
        string clientId,
        string description,
        CancellationToken cancellationToken)
    {
        try
        {
            await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);

            if (_connection.State != HubConnectionState.Connected)
            {
                // AD-29: el servidor no está, así que la publicación no llega. El
                // cliente sigue operando: la publicación NO es lo que habilita al
                // equipo (AD-1).
                _logger.LogWarning(
                    "No se pudo publicar {Description} del cliente {ClientId}: el hub del estado del cliente no está conectado.",
                    description,
                    clientId);

                return;
            }

            await _connection
                .InvokeAsync(methodName, payload, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // La publicación TOLERA la ausencia del método en el servidor, y la REGISTRA:
            // la proyección es de otra historia y no puede romper al cliente (AD-1).
            _logger.LogWarning(
                exception,
                "No se pudo publicar {Description} del cliente {ClientId}: el servidor no aceptó la publicación.",
                description,
                clientId);
        }
    }

    /// <summary>
    /// Opens the connection with the hub, when it is not open yet (AD-22).
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the connection.</param>
    /// <returns>A task that completes when the connection has been attempted.</returns>
    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_started)
            {
                return;
            }

            _started = true;
        }

        try
        {
            await _connection.StartAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "El cliente está conectado al hub {HubRoute} del servidor.",
                Route);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // El servidor puede no estar: la proyección del progreso se pierde, pero la
            // descarga sigue y su offset persistido sobrevive (AD-11, AD-22).
            _logger.LogWarning(
                exception,
                "No se pudo conectar con el hub {HubRoute}: el progreso se guardará solo en el disco del cliente.",
                Route);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync().ConfigureAwait(false);
    }
}
