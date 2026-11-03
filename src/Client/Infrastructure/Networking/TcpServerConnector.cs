using System.Net.Sockets;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Client.Infrastructure.Networking;

/// <summary>
/// Opens the connection with the event server with a socket TCP (AC1, AC2, AD-3, AD-12).
///
/// <para>
/// The adapter is the ONLY place that knows the sockets: the use case of the connection
/// only knows the port that the Application layer declares (AD-1).
/// </para>
/// <para>
/// The opening has a TIME LIMIT: a destination that does not answer never leaves the
/// player waiting without end (AC3).
/// </para>
/// <para>
/// The adapter NEVER fixes the trust anchor and NEVER authenticates the client: opening
/// the connection only says that somebody answered at that address, it does not make the
/// server trustworthy (AD-6, AD-27).
/// </para>
/// </summary>
public sealed class TcpServerConnector : IServerConnector
{
    private readonly ILogger<TcpServerConnector> _logger;

    /// <summary>Creates the adapter that opens the connection with the server.</summary>
    /// <param name="logger">Log of the adapter.</param>
    public TcpServerConnector(ILogger<TcpServerConnector> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
    }

    /// <inheritdoc />
    public async Task ConnectAsync(
        ServerEndpoint endpoint,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);

        using var client = new TcpClient();

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            await client.ConnectAsync(endpoint.Address, endpoint.Port, timeoutSource.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // El tiempo límite se agotó: el destino no respondió y el caso de uso lo
            // traduce en un intento desconectado con las tres partes (AC3).
            _logger.LogWarning(
                "El servidor {ServerAddress}:{ServerPort} no respondió dentro del tiempo límite.",
                endpoint.Address,
                endpoint.Port);

            throw new TimeoutException(
                $"The server {endpoint.Address}:{endpoint.Port} did not answer within the time limit.");
        }
    }
}
