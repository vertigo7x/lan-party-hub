using LANPartyHub.Client.Application.Clients;
using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Client.Application.Ports;

/// <summary>
/// Port of the registration of the client against the event server (AD-32).
///
/// <para>
/// El registro es la ÚNICA operación de entrada del cliente al servidor: el alta, la
/// reconexión y el renombrado del nombre del equipo ocurren en ella, y el servidor no
/// expone ningún otro punto de entrada (AD-32).
/// </para>
/// <para>
/// La respuesta lleva SIEMPRE el nombre efectivo del equipo, así que el cliente lo
/// adopta para el resto del evento y no vuelve a proponer el nombre que colisionó
/// (AD-30, AD-31).
/// </para>
/// </summary>
public interface IClientRegistrationClient
{
    /// <summary>
    /// Registers the client against the event server.
    /// </summary>
    /// <param name="clientId">Canonical identifier of the client.</param>
    /// <param name="machineName">Name proposed for the team.</param>
    /// <param name="clientVersion">Version of the binary of the client.</param>
    /// <param name="protocolVersion">Protocol version that the client speaks.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The answer of the server, with the effective name of the team.</returns>
    Task<ClientRegistrationOutcome> RegisterAsync(
        string clientId,
        string machineName,
        string clientVersion,
        int protocolVersion,
        CancellationToken cancellationToken);
}
