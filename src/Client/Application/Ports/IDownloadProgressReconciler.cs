namespace LANPartyHub.Client.Application.Ports;

/// <summary>
/// Reconciliation of the persisted progress against the partial files of the disk (AD-11,
/// AD-24).
///
/// <para>
/// The port is declared in the Application layer and it is implemented by the adapter that owns
/// both the file system and the database.
/// </para>
/// </summary>
public interface IDownloadProgressReconciler
{
    /// <summary>
    /// Reconciles the persisted offset of every operation of the active engine against the
    /// bytes that its partial file really holds, and rejects the operations whose engine does
    /// not exist in the active version.
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task with the result of the reconciliation.</returns>
    Task<DownloadProgressReconciliation> ReconcileAsync(CancellationToken cancellationToken);
}
