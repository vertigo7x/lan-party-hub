using LANPartyHub.Client.Domain.Trust;

namespace LANPartyHub.Client.Application.Ports;

/// <summary>
/// Port of the read of the ancla de confianza que ya está fijada (AD-27).
///
/// <para>
/// AD-27: las conexiones posteriores se validan contra el valor YA PERSISTIDO y no contra
/// uno recién recibido. El port existe para que el caso de uso de la conexión pueda
/// pronunciarse sobre esa distinción sin conocer el disco (AD-1).
/// </para>
/// </summary>
public interface ITrustAnchorReader
{
    /// <summary>
    /// Reads the ancla de confianza that is already fixed, or <c>null</c> when the client
    /// has not fixed any (AD-27).
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task with the fixed ancla, or with <c>null</c>.</returns>
    Task<PinnedTrustAnchor?> LoadTrustAnchorAsync(CancellationToken cancellationToken);
}
