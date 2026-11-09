using LANPartyHub.Server.Application.Ports;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Server.Application.Clients;

/// <summary>
/// Use case that persists the state of the connection that a client declares
/// (AD-18, AD-22, AD-29, FR-10).
///
/// <para>
/// La transición del <c>connectionState</c> de un <c>ClientRegistration</c> es un
/// HECHO INTERNO del servidor que se PERSISTE: la tabla de clientes del organizador
/// refleja la conexión y la desconexión de cada equipo (AD-29, FR-10).
/// </para>
/// <para>
/// El cliente que publica es SIEMPRE el de la fila: el servidor no acepta una
/// publicación que nombre a otro cliente, porque el estado de un equipo es el suyo y
/// no el de otro (AD-29).
/// </para>
/// <para>
/// Un cliente que NO está registrado ignora la publicación: no se crea una fila por un
/// estado, porque el alta es la operación de registro (AD-30, AD-32).
/// </para>
/// </summary>
public sealed class ReportClientState
{
    private readonly IClientRegistrationRepository _repository;
    private readonly ILogger<ReportClientState> _logger;

    /// <summary>Creates the use case that persists the state of the client.</summary>
    /// <param name="repository">Port of the registry of the clients.</param>
    /// <param name="logger">Log of the state of the client.</param>
    public ReportClientState(
        IClientRegistrationRepository repository,
        ILogger<ReportClientState> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Persists the state of the connection that the client declares.
    /// </summary>
    /// <param name="state">State that the client declares.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the state has been persisted.</returns>
    public async Task ExecuteAsync(
        ClientStateChanged state,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);

        var existing = await _repository
            .FindByClientIdAsync(state.ClientId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            // AD-30, AD-32: el alta del equipo es la operación de REGISTRO, así que un
            // estado de un cliente que no está registrado no crea ninguna fila.
            _logger.LogWarning(
                "El cliente {ClientId} publica su estado pero no está registrado: la publicación se ignora.",
                state.ClientId);

            return;
        }

        var updated = new ClientRegistrationRow(
            existing.ClientId,
            existing.MachineName,
            existing.ClientVersion,
            existing.ProtocolVersion,
            state.ConnectionState,
            existing.LastSeenAt);

        await _repository.SaveAsync(updated, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "El cliente {ClientId} declara el estado de conexión {ConnectionState}.",
            existing.ClientId,
            state.ConnectionState);
    }
}
