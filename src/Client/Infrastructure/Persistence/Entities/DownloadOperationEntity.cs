namespace LANPartyHub.Client.Infrastructure.Persistence.Entities;

/// <summary>
/// Row of the <c>DOWNLOAD_OPERATION</c> table (AD-22, AD-24).
///
/// <para>
/// The operation declares the engine that transfers its bytes, and the format of its progress
/// is deduced from that engine: one engine, one format. The client is the only owner of the
/// state of the operation and the server only projects it (AD-10).
/// </para>
/// </summary>
public sealed class DownloadOperationEntity
{
    /// <summary>Opaque identifier of the operation.</summary>
    public string OperationId { get; set; } = string.Empty;

    /// <summary>Opaque identifier of the package that the operation transfers.</summary>
    public string PackageId { get; set; } = string.Empty;

    /// <summary>Name of the state of the operation.</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>Family of the engine that transfers the bytes of the operation.</summary>
    public string TransferEngine { get; set; } = string.Empty;
}
