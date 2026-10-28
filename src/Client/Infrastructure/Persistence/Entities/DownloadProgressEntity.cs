namespace LANPartyHub.Client.Infrastructure.Persistence.Entities;

/// <summary>
/// Row of the <c>DOWNLOAD_PROGRESS</c> table (AD-11, AD-24).
///
/// <para>
/// The progress of the HTTP path is an accumulated offset: the column <c>bytesPersisted</c>
/// holds the contiguous valid bytes from the beginning of the partial file. There is no map of
/// fragments in the schema of the MVP, neither as a column nor as a value (AD-28).
/// </para>
/// </summary>
public sealed class DownloadProgressEntity
{
    /// <summary>Opaque identifier of the operation that owns the progress.</summary>
    public string OperationId { get; set; } = string.Empty;

    /// <summary>Accumulated offset in bytes.</summary>
    public long BytesPersisted { get; set; }

    /// <summary>Total bytes of the package.</summary>
    public long TotalBytes { get; set; }
}
