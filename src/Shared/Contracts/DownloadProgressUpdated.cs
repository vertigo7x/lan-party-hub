namespace LANPartyHub.Shared.Contracts;

/// <summary>
/// Download progress pushed by the client to the server (AD-18, AD-22).
/// </summary>
/// <param name="ClientId">Opaque UUID v7 identifier of the client.</param>
/// <param name="OperationId">Opaque UUID v7 identifier of the download operation.</param>
/// <param name="BytesTransferred">Bytes transferred so far.</param>
/// <param name="TotalBytes">Total bytes of the package, in exact bytes.</param>
/// <param name="State">Current download machine state.</param>
public sealed record DownloadProgressUpdated(
    string ClientId,
    string OperationId,
    long BytesTransferred,
    long TotalBytes,
    DownloadState State);
