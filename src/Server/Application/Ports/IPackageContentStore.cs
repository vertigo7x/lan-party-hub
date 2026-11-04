namespace LANPartyHub.Server.Application.Ports;

/// <summary>
/// Port of the reading of the bytes of a published package (AD-1, AD-7, AD-22).
///
/// <para>
/// The port is declared in the Application layer and it is implemented by the adapter that
/// owns the repository of packages on disk, the folder that the data directory of the
/// server holds. The use case that publishes a package receives the <em>bytes</em> through
/// this port and it never learns where they live (AD-1).
/// </para>
/// <para>
/// The hash of the bytes is calculated by the use case over the bytes that this port
/// returns, so the value that is signed and the value that is persisted come from one
/// single reading and they cannot diverge (AD-2, AD-7).
/// </para>
/// </summary>
public interface IPackageContentStore
{
    /// <summary>
    /// Reads the bytes of a published package.
    /// </summary>
    /// <param name="packageId">Identifier of the package whose bytes are read.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>
    /// The bytes of the package, or <c>null</c> when the package does not have bytes in the
    /// repository of the server, and also when the identifier is not the plain name of a
    /// file of that repository: an identifier that carries a directory part or a relative
    /// path does not name a package of the repository, and it never makes the server read
    /// a file that is outside of it (AD-9).
    /// </returns>
    Task<byte[]?> ReadBytesAsync(string packageId, CancellationToken cancellationToken);

    /// <summary>
    /// Opens the bytes of a published package as a STREAM, with the exact length of the
    /// content (AD-20).
    ///
    /// <para>
    /// La lectura por <see cref="Stream"/> es la que usa el servidor para SERVIR el
    /// paquete por HTTP con <c>Range</c> requests: el contenido de varios gigabytes no
    /// cabe en la memoria del servidor, y una lectura entera lo traería a ella de todos
    /// modos (AD-20).
    /// </para>
    /// <para>
    /// La longitud se devuelve JUNTO con el flujo porque el servidor la necesita para
    /// declarar el tamaño total del contenido en la cabecera <c>Content-Range</c> y para
    /// calcular la respuesta <c>416</c> cuando el rango no es satisfacible (AD-20).
    /// </para>
    /// </summary>
    /// <param name="packageId">Identifier of the package whose bytes are opened.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>
    /// The stream of the bytes of the package and its exact length, or <c>null</c> when the
    /// package does not have bytes in the repository of the server, and also when the
    /// identifier is not the plain name of a file of that repository (AD-9).
    /// </returns>
    Task<PackageContentStream?> OpenAsync(string packageId, CancellationToken cancellationToken);
}

/// <summary>
/// The stream of the bytes of a published package and its exact length (AD-20).
///
/// <param name="Content">Stream that reads the bytes of the package from the beginning.</param>
/// <param name="Length">Exact length of the content in bytes.</param>
public sealed record PackageContentStream(Stream Content, long Length);
