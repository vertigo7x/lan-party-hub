using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Client.Application.Ports;

/// <summary>
/// Result of the probe of the ancla de confianza del servidor (AD-27).
///
/// <para>
/// AD-27: la sonda NO acepta el ancla. Solo lee lo que el servidor publica en
/// <c>GET /api/v1/server/info</c> para que el caso de uso lo COMPARE con el fingerprint
/// esperado, y el <c>code</c> del rechazo vive en el catálogo de errores (AD-27).
/// </para>
/// </summary>
/// <param name="Anchor">
/// The ancla that the server published, or <c>null</c> when the server did not publish
/// any.
/// </param>
/// <param name="Reachable">
/// Whether the server answered the probe at all. A server that does not answer is NOT a
/// rejection of the ancla: el jugador tiene que poder reintentar (AC3).
/// </param>
public sealed record ServerTrustAnchorProbeResult(TrustAnchor? Anchor, bool Reachable);

/// <summary>
/// Port of the probe of the ancla de confianza del servidor (AD-27).
///
/// <para>
/// El port es declarado en la capa de Application y lo implementa el adaptador que habla
/// HTTP con el servidor. El caso de uso de la fijación depende del port y nunca del
/// adaptador, así que la regla del rechazo previo a persistir se puede ejercitar sin una
/// red (AD-1).
/// </para>
/// <para>
/// AD-27: el port NO decide si el ancla es la buena. Eso es del caso de uso, que
/// compara el fingerprint calculado con el esperado. La sonda solo lee (AD-27).
/// </para>
/// </summary>
public interface IServerTrustAnchorProbe
{
    /// <summary>
    /// Probes the ancla de confianza that the server publishes (AD-27).
    /// </summary>
    /// <param name="endpoint">Endpoint of the server of the event.</param>
    /// <param name="timeout">Time that the probe lasts at most.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The result of the probe.</returns>
    Task<ServerTrustAnchorProbeResult> ProbeAsync(
        Shared.Contracts.ServerEndpoint endpoint,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}
