using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Client.Application.Ports;

/// <summary>
/// Publication of the state of the connection of the client (AD-18, AD-22, AD-29).
///
/// <para>
/// La publicación va en el hub del servidor, que es el canal sancionado por AD-18 y
/// AD-29 para el estado que declara el cliente. El servidor la PERSISTE, y así la
/// tabla de clientes del organizador refleja la conexión y la desconexión de cada
/// equipo (AD-29, FR-10).
/// </para>
/// </summary>
public interface IClientStatePublisher
{
    /// <summary>
    /// Publishes the state of the connection of the client.
    /// </summary>
    /// <param name="state">State that the client declares.</param>
    /// <param name="cancellationToken">Token that cancels the publication.</param>
    /// <returns>A task that completes when the publication has been attempted.</returns>
    Task PublishAsync(ClientStateChanged state, CancellationToken cancellationToken);
}
