using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Client.Application.Connection;

/// <summary>
/// Use case of the automatic discovery of the event server (AC1, AC3, AC4, FR-3, NFR-9,
/// AD-3, AD-6).
///
/// <para>
/// The use case searches the servers of the event with a TIME LIMIT: the search never
/// blocks the player longer than the limit, and a network that does not answer ends the
/// search with <see cref="DiscoveryOutcome.Empty"/> instead of hanging (AC3, AC4).
/// </para>
/// <para>
/// A failure of the adapter ends the search with <see cref="DiscoveryOutcome.Blocked"/> and
/// it NEVER travels to the caller: a network that blocks mDNS does NOT kill the client,
/// because the manual address is a permanent and equivalent way to reach the server
/// (AC4, AD-3).
/// </para>
/// <para>
/// The use case depends on the port declared in this layer and never on an adapter, so it
/// can be exercised without a network (AD-1).
/// </para>
/// </summary>
public sealed class ServerDiscoveryService
{
    /// <summary>
    /// Time that the search of the servers lasts at most, when the caller does not fix
    /// another one (AC3).
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(3);

    private readonly IServerDiscovery _discovery;
    private readonly ILogger<ServerDiscoveryService> _logger;

    /// <summary>Creates the use case of the discovery of the event server.</summary>
    /// <param name="discovery">Port of the search of the servers of the event.</param>
    /// <param name="logger">Log of the use case.</param>
    public ServerDiscoveryService(
        IServerDiscovery discovery,
        ILogger<ServerDiscoveryService> logger)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(logger);

        _discovery = discovery;
        _logger = logger;
    }

    /// <summary>
    /// Searches the servers of the event and returns the outcome of the search (AC1, AC3,
    /// AC4).
    ///
    /// <para>
    /// The search that reaches its time limit WITHOUT an answer ends with
    /// <see cref="DiscoveryOutcome.Empty"/>: a network that does not answer is not a
    /// failure, it is a network without a server (AC3).
    /// </para>
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The outcome of the search and the servers that answered it.</returns>
    public Task<DiscoveryResult> DiscoverAsync(CancellationToken cancellationToken) =>
        DiscoverAsync(DefaultTimeout, cancellationToken);

    /// <summary>
    /// Searches the servers of the event with the given time limit (AC1, AC3, AC4).
    ///
    /// <param name="timeout">Time that the search lasts at most.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The outcome of the search and the servers that answered it.</returns>
    public async Task<DiscoveryResult> DiscoverAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);

        try
        {
            var servers = await _discovery
                .DiscoverAsync(timeout, cancellationToken)
                .ConfigureAwait(false);

            if (servers is null || servers.Count == 0)
            {
                _logger.LogInformation(
                    "La búsqueda de servidores por mDNS no encontró ningún servidor en esta red.");

                return DiscoveryResult.Empty();
            }

            _logger.LogInformation(
                "La búsqueda de servidores por mDNS encontró {ServerCount} servidor(es).",
                servers.Count);

            return DiscoveryResult.Found(servers);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // La cancelación la pidió el jugador: no es un fallo de la red y el token
            // viaja intacto (AC4).
            throw;
        }
        catch (Exception exception)
        {
            // La red puede bloquear el descubrimiento: el cliente NO falla de forma fatal
            // y la vía manual sigue operativa (AC4, AD-3).
            _logger.LogWarning(
                exception,
                "El descubrimiento por mDNS está bloqueado o falló: la búsqueda termina como bloqueada y la dirección manual sigue operativa.");

            return DiscoveryResult.Blocked();
        }
    }
}
