using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Client.Application.Ports;

/// <summary>
/// Publication of the CONNECTION state of the client ALONE (AD-18, AD-29, AD-32).
///
/// <para>
/// El port existe para el cliente que NO puede sostener una INSTANTÁNEA COMPLETA de su
/// estado —porque su almacén local no se abre—. En ese caso el cliente declara SOLO la
/// conexión, y el servidor SOLO actualiza el <c>connectionState</c> de la fila, sin tocar
/// las instalaciones ni las operaciones que ya proyectó de ese cliente (AD-22).
/// </para>
/// <para>
/// Publicar una instantánea VACÍA en ese caso BORRARÍA por error la proyección que el
/// servidor ya tiene, así que el cliente NO lo hace: declara la conexión y conserva lo
/// que el servidor ya sabe (AD-22, AD-29).
/// </para>
/// </summary>
public interface IClientConnectionPublisher
{
    /// <summary>
    /// Publishes the connection state of the client, WITHOUT games nor operations.
    /// </summary>
    /// <param name="changed">Connection state that the client declares.</param>
    /// <param name="cancellationToken">Token that cancels the publication.</param>
    /// <returns>A task that completes when the publication has been attempted.</returns>
    Task PublishAsync(ClientConnectionChanged changed, CancellationToken cancellationToken);
}
