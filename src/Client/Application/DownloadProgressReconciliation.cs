namespace LANPartyHub.Client.Application;

/// <summary>
/// Result of the reconciliation of the persisted progress against the partial files of the
/// disk (AD-11, AD-24).
/// </summary>
public sealed class DownloadProgressReconciliation
{
    /// <summary>
    /// Operations that were rejected because their engine does not exist in the active version.
    /// The records stay in the database, so another version can read them.
    /// </summary>
    public required IReadOnlyList<RejectedDownloadOperation> RejectedOperations { get; init; }

    /// <summary>Operations whose persisted offset was corrected against the partial file.</summary>
    public required IReadOnlyList<ReconciledDownloadProgress> CorrectedProgresses { get; init; }
}

/// <summary>
/// An operation that was rejected because its engine does not exist in the active version of
/// the client (AD-24).
/// </summary>
/// <param name="OperationId">Opaque identifier of the operation.</param>
/// <param name="PackageId">Opaque identifier of the package.</param>
/// <param name="TransferEngine">Engine that the record declares.</param>
public sealed record RejectedDownloadOperation(string OperationId, string PackageId, string TransferEngine);

/// <summary>
/// An operation whose persisted offset was corrected against the bytes that its partial file
/// really holds (AD-11).
/// </summary>
/// <param name="OperationId">Opaque identifier of the operation.</param>
/// <param name="PersistedOffset">Offset that was persisted before the reconciliation.</param>
/// <param name="EffectiveOffset">Effective offset after the reconciliation.</param>
public sealed record ReconciledDownloadProgress(string OperationId, long PersistedOffset, long EffectiveOffset);
