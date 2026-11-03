using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Client.Application.Ports;

/// <summary>
/// Port of the RECONNECTION of the client after the server comes back (AC5, FR-3, NFR-9).
///
/// <para>
/// The port is declared in the Application layer and it is implemented by the composition
/// that owns the hub and the synchronization. The supervisor of the availability depends on
/// the port and never on the hub, así que la regla de la reconexión se puede ejercitar sin
/// una red y sin un servidor (AD-1).
/// </para>
/// <para>
/// La reconexión REPETE la puerta de versión (AD-6) y la validación del ancla fijado
/// (AD-27): el cliente que vuelve NO hereda la confianza de la sesión anterior, porque el
/// servidor que responde puede ser otro.
/// </para>
/// </summary>
public interface IServerReconnectHandler
{
    /// <summary>
    /// Re-establishes the connection with the server of the event and resynchronizes the
    /// catalog, WITHOUT reinstalling anything (AC5).
    ///
    /// <para>
    /// La reconexión re-suscribe al hub del estado del cliente y llama a la sincronización
    /// del catálogo: el cliente recupera la conexión y vuelve a sincronizar el catálogo
    /// (AC5, AD-5, AD-26).
    /// </para>
    /// </summary>
    /// <param name="endpoint">Endpoint of the server of the event.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>
    /// <c>true</c> when the connection was re-established and the catalog was
    /// resynchronized, or <c>false</c> when the server could not be reached again.
    /// </returns>
    Task<bool> ReconnectAsync(
        ServerEndpoint endpoint,
        CancellationToken cancellationToken);
}
