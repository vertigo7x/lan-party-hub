using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Client.Application.Ports;

/// <summary>
/// Port of the read of the signed installation manifest of a package (AD-2, AD-8, AD-20).
///
/// <para>
/// El manifiesto firmado viaja APARTE de los bytes del paquete, y el cliente lo pide
/// ANTES de descargar: la firma y la versión del esquema se validan antes de que exista
/// un archivo parcial (AD-8, AD-20).
/// </para>
/// <para>
/// El port entrega el SOBRE tal y como el servidor lo sirvió, con el texto canónico del
/// manifiesto, la firma y el identificador de la clave. Los bytes que se verifican son
/// exactamente los que la firma cubre: no hay dos canonicalizadores que puedan divergir
/// (AD-2).
/// </para>
/// </summary>
public interface ISignedManifestClient
{
    /// <summary>
    /// Reads the signed manifest of a package.
    /// </summary>
    /// <param name="packageId">Opaque identifier of the package whose manifest is read.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>
    /// The signed envelope of the manifest, or <c>null</c> when the server does not hold a
    /// published package with that identifier.
    /// </returns>
    Task<InstallationManifest.SignedEnvelope?> GetAsync(string packageId, CancellationToken cancellationToken);
}
