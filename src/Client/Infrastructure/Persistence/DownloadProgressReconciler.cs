using LANPartyHub.Client.Application;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Domain;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Client.Infrastructure.Persistence;

/// <summary>
/// Reconciliation of the persisted progress against the partial files of the disk (AD-11,
/// AD-24).
///
/// <para>
/// The effective progress of an operation is the intersection of the offset that is persisted
/// and the bytes that its partial file really holds. When the two disagree, the offset that
/// is persisted is corrected and written back.
/// </para>
/// <para>
/// An operation whose engine does not exist in the active version is rejected: it is not
/// interpreted as an active operation and its record stays in the database, so another version
/// can read it. The rejection never stops the startup (AD-24).
/// </para>
/// </summary>
public sealed class DownloadProgressReconciler : IDownloadProgressReconciler
{
    private readonly IClientDataLayout _layout;
    private readonly IClientStateStore _stateStore;
    private readonly ILogger<DownloadProgressReconciler> _logger;

    /// <summary>Creates the adapter that reconciles the progress.</summary>
    /// <param name="layout">Layout of the data folder of the client.</param>
    /// <param name="stateStore">Access to the database of the client.</param>
    /// <param name="logger">Log of the reconciliation.</param>
    public DownloadProgressReconciler(
        IClientDataLayout layout,
        IClientStateStore stateStore,
        ILogger<DownloadProgressReconciler> logger)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(stateStore);
        ArgumentNullException.ThrowIfNull(logger);

        _layout = layout;
        _stateStore = stateStore;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<DownloadProgressReconciliation> ReconcileAsync(CancellationToken cancellationToken)
    {
        var operations = await _stateStore.LoadDownloadOperationsAsync(cancellationToken);

        var rejected = new List<RejectedDownloadOperation>();
        var corrected = new List<ReconciledDownloadProgress>();

        foreach (var operation in operations)
        {
            if (!TransferEngines.IsActive(operation.TransferEngine))
            {
                // The record is rejected and not reinterpreted (AD-24). It is not deleted, so
                // another version of the client can read it.
                rejected.Add(new RejectedDownloadOperation(
                    operation.OperationId,
                    operation.PackageId,
                    operation.TransferEngine));

                _logger.LogWarning(
                    "Se rechaza la operación {OperationId}: el motor de transferencia {TransferEngine} no existe en la versión activa.",
                    operation.OperationId,
                    operation.TransferEngine);

                continue;
            }

            if (operation.BytesPersisted is not { } persistedOffset)
            {
                continue;
            }

            string partialPath;

            try
            {
                partialPath = _layout.PartialFilePath(operation.PackageId);
            }
            catch (ArgumentException exception)
            {
                _logger.LogWarning(
                    "No se reconcilia la operación {OperationId}: {Reason}",
                    operation.OperationId,
                    exception.Message);

                continue;
            }

            var partialLength = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0L;

            // The effective progress is the intersection of the offset that is persisted and
            // the bytes that the partial file holds, and it never leaves the range that the
            // file can back.
            var effectiveOffset = Math.Clamp(persistedOffset, 0, partialLength);

            if (effectiveOffset == persistedOffset)
            {
                continue;
            }

            await _stateStore.SaveProgressAsync(
                operation.OperationId,
                effectiveOffset,
                operation.TotalBytes,
                cancellationToken);

            corrected.Add(new ReconciledDownloadProgress(operation.OperationId, persistedOffset, effectiveOffset));

            _logger.LogInformation(
                "Se corrige el offset persistido de la operación {OperationId}: {PersistedOffset} bytes pasan a ser {EffectiveOffset} bytes.",
                operation.OperationId,
                persistedOffset,
                effectiveOffset);
        }

        return new DownloadProgressReconciliation
        {
            RejectedOperations = rejected,
            CorrectedProgresses = corrected,
        };
    }
}
