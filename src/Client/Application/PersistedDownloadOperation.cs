namespace LANPartyHub.Client.Application;

/// <summary>
/// A download operation as it is persisted in the database of the client, with the progress
/// that goes with it (AD-11, AD-24).
///
/// <para>
/// The progress of the HTTP path is an accumulated offset with the columns
/// <c>bytesPersisted</c> and <c>totalBytes</c>. There is no map of fragments in the schema of
/// the MVP (AD-24, AD-28).
/// </para>
/// </summary>
/// <param name="OperationId">Opaque identifier of the operation.</param>
/// <param name="PackageId">Opaque identifier of the package.</param>
/// <param name="State">Name of the state of the operation.</param>
/// <param name="TransferEngine">Family of the engine that the operation declares.</param>
/// <param name="BytesPersisted">Accumulated offset that is persisted, or <c>null</c> when the operation has no progress row.</param>
/// <param name="TotalBytes">Total bytes of the package, or <c>null</c> when the operation has no progress row.</param>
public sealed record PersistedDownloadOperation(
    string OperationId,
    string PackageId,
    string State,
    string TransferEngine,
    long? BytesPersisted,
    long? TotalBytes);
