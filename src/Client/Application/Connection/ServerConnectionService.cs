using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Client.Application.Connection;

/// <summary>
/// Use case of the connection with the event server by the address that the player typed
/// (AC1, AC3, FR-3, AD-3, AD-27).
///
/// <para>
/// The use case parses the address with the SINGLE rule of the Application layer, opens
/// the connection with a time limit and returns the attempt: the endpoint, the state of
/// the connection and, when the attempt failed, the error of the catalog con sus tres
/// partes (AC1, AC3, AR-4).
/// </para>
/// <para>
/// A failure NEVER travels to the caller as an exception: an address that does not answer
/// ends the attempt as <see cref="ConnectionState.Disconnected"/> with the three part
/// message, and the player can try again (AC3, UX-DR-36).
/// </para>
/// <para>
/// The use case depends on the port declared in this layer and never on an adapter, so it
/// can be exercised without a network (AD-1).
/// </para>
/// </summary>
public sealed class ServerConnectionService
{
    /// <summary>
    /// Time that the opening of the connection lasts at most, when the caller does not fix
    /// another one (AC3).
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(3);

    private readonly IServerConnector _connector;
    private readonly ILogger<ServerConnectionService> _logger;

    /// <summary>Creates the use case of the connection with the event server.</summary>
    /// <param name="connector">Port of the opening of the connection with the server.</param>
    /// <param name="logger">Log of the use case.</param>
    public ServerConnectionService(
        IServerConnector connector,
        ILogger<ServerConnectionService> logger)
    {
        ArgumentNullException.ThrowIfNull(connector);
        ArgumentNullException.ThrowIfNull(logger);

        _connector = connector;
        _logger = logger;
    }

    /// <summary>
    /// Connects with the server of the event at the address that the player typed, with
    /// the default time limit (AC1, AC3).
    /// </summary>
    /// <param name="address">Text of the address, in the form <c>host:puerto</c>.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The attempt of the connection.</returns>
    public Task<ConnectionAttempt> ConnectAsync(
        string? address,
        CancellationToken cancellationToken) =>
        ConnectAsync(address, DefaultTimeout, cancellationToken);

    /// <summary>
    /// Connects with the server of the event at the address that the player typed, with the
    /// given time limit (AC1, AC3).
    ///
    /// <para>
    /// A malformed address NEVER reaches the network: the attempt ends as disconnected
    /// with the three part message of the catalog (AC3, AR-4).
    /// </para>
    /// </summary>
    /// <param name="address">Text of the address, in the form <c>host:puerto</c>.</param>
    /// <param name="timeout">Time that the opening lasts at most.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The attempt of the connection.</returns>
    public async Task<ConnectionAttempt> ConnectAsync(
        string? address,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);

        if (!ServerEndpointParser.TryParse(address, out var endpoint, out var parseError))
        {
            _logger.LogWarning(
                "La dirección escrita por el jugador no es válida: el intento termina como desconectado con el código {ErrorCode}.",
                parseError!.Code);

            return ConnectionAttempt.Failed(null, parseError);
        }

        try
        {
            await _connector
                .ConnectAsync(endpoint!, timeout, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "El cliente se conectó al servidor {ServerAddress}:{ServerPort}; el ancla de confianza queda pendiente.",
                endpoint!.Address,
                endpoint.Port);

            return ConnectionAttempt.Succeeded(endpoint);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // La cancelación la pidió el jugador: no es un fallo de la red y el token
            // viaja intacto (AC3).
            throw;
        }
        catch (Exception exception)
        {
            // El destino puede no responder o puede rechazar la conexión: el cliente NO
            // falla de forma fatal y la vía manual sigue operativa (AC1, AC3, AD-3).
            _logger.LogWarning(
                exception,
                "No se pudo abrir la conexión con {ServerAddress}:{ServerPort}: el intento termina como desconectado.",
                endpoint!.Address,
                endpoint.Port);

            return ConnectionAttempt.Failed(
                endpoint,
                ErrorCatalog.Resolve(ErrorCatalog.UnknownErrorCode));
        }
    }
}
