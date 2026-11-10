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
/// <para>
/// La lectura es de LOTE: el organizador recibe el estado, las instalaciones, las
/// operaciones en curso, los resultados terminales y los errores de TODOS los equipos
/// en UNA SOLA respuesta, y NO hace ninguna petición adicional por equipo (SM-4,
/// FR-10). Los errores que se muestran son los YA REGISTRADOS: esta historia los
/// MUESTRA y no los produce (AD-15, AD-22).
/// </para>
/// </summary>
public sealed class ListClients
{
    private readonly IClientRegistrationRepository _repository;
    private readonly IClientInstallationViewRepository _installations;
    private readonly IClientErrorRepository _errors;
    private readonly ClientOperationsProjection _operations;
    private readonly ILogger<ListClients> _logger;

    /// <summary>Creates the use case that reads the registered clients.</summary>
    /// <param name="repository">Port of the registry of the clients.</param>
    /// <param name="installations">Port of the projection of the installations.</param>
    /// <param name="errors">Port of the registry of the errors of the clients.</param>
    /// <param name="operations">Projection of the operations that are in course.</param>
    /// <param name="logger">Log of the reading of the clients.</param>
    public ListClients(
        IClientRegistrationRepository repository,
        IClientInstallationViewRepository installations,
        IClientErrorRepository errors,
        ClientOperationsProjection operations,
        ILogger<ListClients> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(installations);
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _installations = installations;
        _errors = errors;
        _operations = operations;
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

        var views = new List<ClientRegistrationView>(rows.Count);

        foreach (var row in rows)
        {
            views.Add(await ProjectAsync(row, cancellationToken).ConfigureAwait(false));
        }

        return
        [
            .. views
                .OrderBy(view => view.MachineName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(view => view.ClientId, StringComparer.Ordinal)
        ];
    }

    /// <summary>
    /// Projects the row of a client onto the contract of the view, ENRIQUECING it with
    /// the installations, the operations in course, the terminal results and the errors
    /// that are already registered (AD-22, FR-10).
    /// </summary>
    /// <param name="row">Row of the client.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The enriched view of the client.</returns>
    private async Task<ClientRegistrationView> ProjectAsync(
        ClientRegistrationRow row,
        CancellationToken cancellationToken)
    {
        // AD-22: las instalaciones se leen de la proyección PERSISTIDA, con el texto de
        // presentación que copió el `StateDisplayMap`. La ruta de la instalación NUNCA
        // sale del cliente (AD-22).
        var installations = await _installations
            .ListByClientAsync(row.ClientId, cancellationToken)
            .ConfigureAwait(false);

        // AD-15, AD-22: los errores que se muestran son los YA REGISTRADOS.
        var errors = await _errors
            .ListByClientAsync(row.ClientId, cancellationToken)
            .ConfigureAwait(false);

        // AD-22: las operaciones en curso y los resultados terminales viven EN MEMORIA.
        var operations = _operations.ListActiveOperations(row.ClientId);
        var outcomes = _operations.ListOutcomes(row.ClientId);

        return new ClientRegistrationView(
            row.ClientId,
            row.MachineName,
            row.ClientVersion,
            row.ProtocolVersion,
            row.ConnectionState,
            row.LastSeenAt,
            [
                .. installations.Select(installation => new ClientRegistrationView.InstalledGame(
                    installation.GameId,
                    installation.InstalledVersion,
                    installation.ValidationStateText))
            ],
            operations,
            outcomes,
            [
                .. errors.Select(error => new ClientRegistrationView.RegisteredError(
                    error.ErrorId,
                    error.OperationId,
                    error.Code,
                    error.Message,
                    error.Cause,
                    error.NextStep,
                    error.OccurredAt))
            ]);
    }
}
