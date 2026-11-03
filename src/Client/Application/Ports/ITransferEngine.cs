namespace LANPartyHub.Client.Application.Ports;

/// <summary>
/// The only door to the transfer of bytes of the client (AD-4, AD-20, AD-24, AD-28).
///
/// <para>
/// The port is declared in the Application layer and it is the single entry point of the
/// byte transfer: the domain (<c>DownloadOperation</c>, <c>PackageIntegrity</c>) never
/// mentions HTTP, BitTorrent, trackers nor sockets.
/// </para>
/// <para>
/// The MVP delivers the port with ONE single implementation, <c>HttpRangeTransferEngine</c>,
/// which asks for the bytes of a range over HTTP. The caller brings the absolute URL of the
/// content and the range, so the engine knows no route and resolves no host: it cannot
/// download from the Internet on its own.
/// </para>
/// </summary>
public interface ITransferEngine
{
    /// <summary>
    /// Transfers the bytes of a range of the content that lives in the given URL.
    /// </summary>
    /// <param name="contentUri">Absolute URL of the content to transfer.</param>
    /// <param name="startInclusive">First byte of the range, included.</param>
    /// <param name="endInclusive">Last byte of the range, included.</param>
    /// <param name="destination">Stream that receives the bytes of the range.</param>
    /// <param name="cancellationToken">Token that cancels the transfer.</param>
    /// <returns>The outcome of the transfer.</returns>
    Task<TransferOutcome> TransferAsync(
        Uri contentUri,
        long startInclusive,
        long endInclusive,
        Stream destination,
        CancellationToken cancellationToken);
}

/// <summary>
/// Outcome of a transfer of bytes (AD-4, AD-24).
///
/// <para>
/// The outcome says whether the server honoured the range or answered with the whole
/// content. One engine, one format: the progress of the HTTP path is an accumulated offset,
/// and the two outcomes are the only ones that the offset can be derived from.
/// </para>
/// </summary>
/// <param name="BytesTransferred">Bytes that the transfer wrote in the destination.</param>
/// <param name="TotalBytes">
/// Total bytes of the content when the server declares them, or <c>null</c> when it does not.
/// </param>
/// <param name="RangeHonoured">
/// <c>true</c> when the server answered with the requested range (<c>206</c>), and
/// <c>false</c> when it answered with the whole content (<c>200</c>).
/// </param>
public sealed record TransferOutcome(
    long BytesTransferred,
    long? TotalBytes,
    bool RangeHonoured);
