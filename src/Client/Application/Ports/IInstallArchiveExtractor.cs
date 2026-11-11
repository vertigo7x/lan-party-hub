namespace LANPartyHub.Client.Application.Ports;

/// <summary>
/// Port of the extraction of an archive of the package into the installation root
/// (AD-7, AD-9, AC1).
///
/// <para>
/// El port existe para que el caso de uso de la instalación pueda pedir la extracción
/// sin conocer el FORMATO del archivo: el caso de uso decide CUÁNDO se extrae y el
/// adaptador decide CÓMO (AD-1, AD-16).
/// </para>
/// <para>
/// El extractor escribe SÓLO dentro de la raíz configurada: el destino se resuelve contra
/// la raíz y la ruta canónica se comprueba antes de CADA entrada del archivo, así que un
/// archivo con una entrada que sale de la raíz no escribe nada fuera (AD-9, AC1).
/// </para>
/// </summary>
public interface IInstallArchiveExtractor
{
    /// <summary>
    /// Extracts an entry of the package into the installation root (AD-7, AD-9, AC1).
    /// </summary>
    /// <param name="rootPath">
    /// Canonical absolute path of the root that confines the write. It is NOT
    /// necessarily the configured root: during an UPDATE the tree is prepared in a
    /// STAGING root and the previous one is not touched until the new one is certified
    /// (AD-9, AC4, FR-11).
    /// </param>
    /// <param name="packagePath">Absolute path of the file that holds the bytes of the package.</param>
    /// <param name="archivePath">
    /// Path of the entry INSIDE the package, as the manifest declares it.
    /// </param>
    /// <param name="destinationPath">
    /// Destination path RELATIVE to the installation root, as the manifest declares it.
    /// </param>
    /// <param name="destinationIsDirectory">
    /// Whether the destination is a DIRECTORY �the semantics of the <c>extract</c>
    /// operation, que escribe la entrada DENTRO de ella con su propio nombre� or an
    /// exact FILE �the semantics of the <c>copyFile</c> operation (AD-7).
    /// </param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The number of bytes that were really written.</returns>
    /// <exception cref="InstallPathOutsideRootException">
    /// An entry of the archive resolves OUTSIDE the installation root (AD-9, AC1).
    /// </exception>
    Task<long> ExtractAsync(
        string rootPath,
        string packagePath,
        string archivePath,
        string destinationPath,
        bool destinationIsDirectory,
        CancellationToken cancellationToken);
}
