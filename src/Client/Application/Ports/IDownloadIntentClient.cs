namespace LANPartyHub.Client.Application.Ports;

/// <summary>
/// Port of the registration of the intention of a download (AD-5, AD-10).
///
/// <para>
/// The port is declared in the Application layer and it is implemented by the adapter that
/// speaks HTTP with the event server. The coordinator of the downloads depends on the port
/// and never on the adapter, so it can be exercised without a server (AD-1).
/// </para>
/// </summary>
public interface IDownloadIntentClient
{
    /// <summary>
    /// Registers the intention of downloading a package and returns the identifier of the
    /// operation (AC1, AD-10).
    ///
    /// <para>
    /// El identificador de la operación es la UNIDAD DE TRABAJO del cliente: nombra su
    /// fila local, su log y su progreso (AD-15, AD-24).
    /// </para>
    /// </summary>
    /// <param name="packageId">Opaque identifier of the package that the client wants.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The identifier of the registered operation.</returns>
    Task<string> RegisterAsync(string packageId, CancellationToken cancellationToken);
}
