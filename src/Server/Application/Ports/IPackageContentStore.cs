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
    /// repository of the server.
    /// </returns>
    Task<byte[]?> ReadBytesAsync(string packageId, CancellationToken cancellationToken);
}
