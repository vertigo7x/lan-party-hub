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
public sealed class ClientStateHubPublisher : IDownloadProgressReporter, IAsyncDisposable
{
    /// <summary>Route of the hub of the state of the client.</summary>
    public const string Route = "/hubs/client-state";

    /// <summary>
    /// Name of the method of the hub that receives the progress of a download operation
    /// (AD-22, AR-3).
    /// </summary>
    public const string DownloadProgressUpdatedMethodName = nameof(DownloadProgressUpdated);

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
