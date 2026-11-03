namespace LANPartyHub.Client.Application.Ports;

/// <summary>
/// Result of the probe of the versions of the server (AD-6, FR-4).
///
/// <para>
/// El sondeo NO decide la compatibilidad: la matriz vive SOLO en el servidor (AD-6). El
/// cliente se limita a declarar su versión e interpretar la respuesta: un
/// <c>426 Upgrade Required</c> con las cabeceras de la versión del servidor y del rango
/// mínimo (AD-6).
/// </para>
/// <para>
/// El diagnóstico NO es un estado: <see cref="Shared.Contracts.ConnectionState"/> sigue
/// con sus tres valores, y la incompatibilidad es un diagnóstico con la versión del
/// cliente, la del servidor y el siguiente paso (AD-27).
/// </para>
/// </summary>
/// <param name="Compatible">
/// Whether the server accepted the versions that the client declared.
/// </param>
/// <param name="ServerVersion">
/// Version of the server, or <c>null</c> when the server did not publish it.
/// </param>
/// <param name="MinimumProtocolVersion">
/// Oldest protocol version that the server accepts, or <c>null</c> when the server did
/// not publish it.
/// </param>
/// <param name="ServerName">
/// Name of the server of the event, or <c>null</c> when the server did not publish it.
/// </param>
/// <param name="Reachable">
/// Whether the server answered the probe at all.
///
/// <para>
/// A server that does not answer is NOT a rejection of the version: el jugador tiene que
/// poder reintentar, y la disponibilidad del servidor es un estado DEL CLIENTE (AC3,
/// AC4).
/// </para>
/// </param>
public sealed record ServerVersionProbeResult(
    bool Compatible,
    string? ServerVersion,
    int? MinimumProtocolVersion,
    string? ServerName = null,
    bool Reachable = true);

/// <summary>
/// Port of the probe of the versions of the event server (AD-6, FR-4).
///
/// <para>
/// El port es declarado en la capa de Application y lo implementa el adaptador que
/// habla HTTP con el servidor. El caso de uso de la conexión nunca alcanza el adaptador
/// y nunca conoce el transporte, así que la regla del diagnóstico se puede ejercitar
/// sin una red (AD-1).
/// </para>
/// <para>
/// El port NO lanza una excepción cuando el servidor no responde: una sonda que no
/// obtiene respuesta NO significa que el cliente sea incompatible, y el jugador tiene
/// que poder reintentar (AC3, UX-DR-36).
/// </para>
/// </summary>
public interface IServerVersionProbe
{
    /// <summary>
    /// Probes the versions of the server and tells whether the client is compatible
    /// (AD-6).
    /// </summary>
    /// <param name="endpoint">Endpoint of the server of the event.</param>
    /// <param name="timeout">Time that the probe lasts at most.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The result of the probe.</returns>
    Task<ServerVersionProbeResult> ProbeAsync(
        Shared.Contracts.ServerEndpoint endpoint,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}
