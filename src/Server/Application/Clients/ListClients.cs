using LANPartyHub.Server.Application.Ports;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Server.Application.Clients;

/// <summary>
/// Use case that reads the clients registered in the event (AD-29, AD-30).
///
/// <para>
/// La vista de organización de clientes se obtiene EXCLUSIVAMENTE por REST, con
/// <c>GET /api/v1/clients</c>, y el evento <c>ClientsSnapshotChanged</c> no existe:
/// la tabla del organizador es una vista del propio servidor, que ya posee la fila
/// persistida (AD-29).
/// </para>
/// <para>
/// El orden es ESTABLE por nombre de equipo, de modo que dos lecturas seguidas del
/// mismo conjunto de clientes ordenan las filas igual y el organizador no ve las
/// filas saltar de sitio entre un refresco y el siguiente (AD-29, UX-DR-14).
/// </para>
/// </summary>
public sealed class ListClients
{
    private readonly IClientRegistrationRepository _repository;
    private readonly ILogger<ListClients> _logger;

    /// <summary>Creates the use case that reads the registered clients.</summary>
    /// <param name="repository">Port of the registry of the clients.</param>
    /// <param name="logger">Log of the reading of the clients.</param>
    public ListClients(
        IClientRegistrationRepository repository,
        ILogger<ListClients> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Reads the clients of the event and projects them onto the contract of
    /// <c>Shared</c>, with the exact shape that the view of organization expects
    /// (AD-18, AD-29).
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The registered clients, ordered by team name.</returns>
    public async Task<IReadOnlyList<ClientRegistrationView>> ExecuteAsync(
        CancellationToken cancellationToken)
    {
        var rows = await _repository
            .ListAsync(cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Se leen {ClientCount} clientes registrados para la vista de organización.",
            rows.Count);

        return
        [
            .. rows
                .OrderBy(row => row.MachineName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.ClientId, StringComparer.Ordinal)
                .Select(row => new ClientRegistrationView(
                    row.ClientId,
                    row.MachineName,
                    row.ClientVersion,
                    row.ProtocolVersion,
                    row.ConnectionState,
                    row.LastSeenAt))
        ];
    }
}
