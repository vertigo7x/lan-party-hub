using LANPartyHub.Server.Application.Ports;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Server.Application.Clients;

/// <summary>
/// Use case that persists ONLY the connection state that a client declares
/// (AD-18, AD-22, AD-29, AD-32).
///
/// <para>
/// El caso de uso existe para el cliente que NO puede sostener una INSTANTÁNEA
/// COMPLETA de su estado: en ese caso declara SOLO la conexión, y el servidor SOLO
/// actualiza el <c>connectionState</c> de la fila (AD-29, AD-32).
/// </para>
/// <para>
/// El caso de uso NO toca la proyección de las INSTALACIONES ni la de las OPERACIONES:
/// un evento de conexión no sustituye ni vacía lo que el servidor ya proyectó de ese
/// cliente, así que un cliente que pierde su almacén local deja de ver sus operaciones
/// en curso, pero NO las ve desaparecer (AD-22).
/// </para>
/// <para>
/// Un cliente que NO está registrado ignora la publicación: el alta del equipo es la
/// operación de REGISTRO, así que un evento de conexión no crea ninguna fila
/// (AD-30, AD-32).
/// </para>
/// </summary>
public sealed class ReportClientConnection
{
    private readonly IClientRegistrationRepository _repository;
    private readonly ILogger<ReportClientConnection> _logger;

    /// <summary>Creates the use case that persists the connection of the client.</summary>
    /// <param name="repository">Port of the registry of the clients.</param>
    /// <param name="logger">Log of the connection of the client.</param>
    public ReportClientConnection(
        IClientRegistrationRepository repository,
        ILogger<ReportClientConnection> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Persists the connection state that the client declares, WITHOUT touching the
    /// installations nor the operations that the server already projected (AD-22,
    /// AD-29).
    /// </summary>
    /// <param name="changed">Connection state that the client declares.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the connection has been persisted.</returns>
    public async Task ExecuteAsync(
        ClientConnectionChanged changed,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changed);

        var existing = await _repository
            .FindByClientIdAsync(changed.ClientId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            // AD-30, AD-32: el alta del equipo es la operación de REGISTRO, así que un
            // evento de conexión de un cliente que no está registrado no crea ninguna fila.
            _logger.LogWarning(
                "El cliente {ClientId} declara su conexión pero no está registrado: la publicación se ignora.",
                changed.ClientId);

            return;
        }

        // AD-22, AD-29: SOLO cambia el estado de conexión. Las instalaciones
        // PERSISTIDAS y las operaciones EN MEMORIA que el servidor ya proyectó NO se
        // tocan: el evento de conexión no es una instantánea (AD-22).
        var updated = new ClientRegistrationRow(
            existing.ClientId,
            existing.MachineName,
            existing.ClientVersion,
            existing.ProtocolVersion,
            changed.ConnectionState,
            existing.LastSeenAt);

        await _repository.SaveAsync(updated, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "El cliente {ClientId} declara el estado de conexión {ConnectionState} sin publicar su estado completo.",
            existing.ClientId,
            changed.ConnectionState);
    }
}
