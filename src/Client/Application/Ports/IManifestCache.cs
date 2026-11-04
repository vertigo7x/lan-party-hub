namespace LANPartyHub.Client.Application.Ports;

/// <summary>
/// Port of the cache of the signed manifests of the client (AD-9, AD-20).
///
/// <para>
/// La caché vive en la carpeta <c>manifests/</c> de la carpeta de datos del cliente, que
/// es una de las cuatro entradas que AD-9 fija (AD-9).
/// </para>
/// <para>
/// La caché se escribe SOLO después de que la firma y la versión del esquema hayan pasado:
/// un manifiesto que no valida no se persiste, porque el rechazo precede a persistir
/// (AD-2, AD-8, AD-27).
/// </para>
/// </summary>
public interface IManifestCache
{
    /// <summary>
    /// Writes the signed manifest of a package in the cache.
    ///
    /// <para>
    /// La escritura es idempotente: el manifiesto de un paquete es inmutable, así que
    /// escribirlo dos veces deja el mismo contenido (AD-7).
    /// </para>
    /// </summary>
    /// <param name="packageId">Opaque identifier of the package whose manifest is cached.</param>
    /// <param name="canonicalManifest">Canonical JSON text of the manifest, as it was signed.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the manifest is cached.</returns>
    Task StoreAsync(
        string packageId,
        string canonicalManifest,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads the cached canonical manifest of a package, or <c>null</c> when the cache does
    /// not hold one for that package.
    /// </summary>
    /// <param name="packageId">Opaque identifier of the package whose manifest is read.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task with the canonical text of the manifest, or with <c>null</c>.</returns>
    Task<string?> LoadAsync(string packageId, CancellationToken cancellationToken);
}
