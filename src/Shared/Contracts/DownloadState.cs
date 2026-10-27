namespace LANPartyHub.Shared.Contracts;

/// <summary>
/// Closed enumeration of the download machine states (AD-10, AD-18).
/// The values are the same on the client and on the server: the client owns the local
/// operation and the server only projects it.
/// </summary>
public enum DownloadState
{
    /// <summary>The operation is registered and waiting for the transfer to start.</summary>
    Queued,

    /// <summary>Bytes are being transferred from the server.</summary>
    Downloading,

    /// <summary>The transfer is paused and can be resumed from the persisted offset.</summary>
    Paused,

    /// <summary>The downloaded bytes are being verified against the signed manifest.</summary>
    Validating,

    /// <summary>Terminal state: the package is complete and validated.</summary>
    Completed,

    /// <summary>Terminal state: the operation was cancelled by the user.</summary>
    Cancelled,

    /// <summary>Terminal state: the operation failed and is not resumable from this state.</summary>
    Failed,
}
