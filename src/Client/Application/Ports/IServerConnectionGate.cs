using LANPartyHub.Client.Application.Connection;
using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Client.Application.Ports;

/// <summary>
/// Port of the PUERTA DE VERSIÓN y del ANCLA FIJADO que toda reconexión repite
/// (AC5, AD-6, AD-27).
///
/// <para>
/// El port es declarado en la capa de Application y lo implementa el caso de uso de la
/// conexión. La reconexión NO hereda la confianza de la sesión anterior: el servidor que
/// responde puede ser otro, así que la puerta de versión (AD-6) y la validación del ancla
/// fijado (AD-27) se repiten SIEMPRE (AC5).
/// </para>
/// </summary>
public interface IServerConnectionGate
{
    /// <summary>
    /// Opens the connection with the server of the event and passes its gate: the version
    /// and the ancla de confianza fijada (AD-6, AD-27).
    ///
    /// <param name="endpoint">Endpoint of the server of the event.</param>
    /// <param name="timeout">Time that the opening lasts at most.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The attempt of the connection.</returns>
    Task<ConnectionAttempt> OpenAsync(
        ServerEndpoint endpoint,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}
