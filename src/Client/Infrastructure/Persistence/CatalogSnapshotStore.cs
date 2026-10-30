using System.Text.Json;
using LANPartyHub.Client.Application;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Infrastructure.Persistence.Entities;
using LANPartyHub.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace LANPartyHub.Client.Infrastructure.Persistence;

/// <summary>
/// Adapter of the persistence of the snapshot of the catalog of the client
/// (AD-11, AD-22, AD-26).
///
/// <para>
/// The adapter owns the table <c>CATALOG_SNAPSHOT</c> of the file <c>state.sqlite</c> and
/// it holds a SINGLE row: the complete snapshot that the client has applied, with the
/// version that produced it and the instant of the synchronization. The snapshot survives
/// the restart of the client, so a player that starts the client without the server still
/// sees the last catalog that was synchronized (AC3).
/// </para>
/// <para>
/// The write happens inside a transaction, so the version and the payload that travel
/// together are the ones of the same instant: a client that restarts never reads a version
/// that announces a publication that is not in the payload (AD-26).
/// </para>
/// <para>
/// The table belongs to the client and it never replicates a table of the server (AD-22).
/// </para>
/// </summary>
public sealed class CatalogSnapshotStore : ICatalogSnapshotStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly ClientStateDbContext _context;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates the adapter of the persistence of the snapshot of the catalog.</summary>
    /// <param name="context">Context of the database of the client.</param>
    /// <param name="timeProvider">
    /// Source of the instant of the synchronization. It defaults to the real one and it
    /// exists so that the age of the catalog can be exercised with a known instant.
    /// </param>
    public CatalogSnapshotStore(ClientStateDbContext context, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<CatalogSnapshot?> LoadAsync(CancellationToken cancellationToken)
    {
        var row = await _context.CatalogSnapshots
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == CatalogSnapshotEntity.SingleRowKey, cancellationToken)
            .ConfigureAwait(false);

        if (row is null || string.IsNullOrWhiteSpace(row.Payload))
        {
            return null;
        }

        return JsonSerializer.Deserialize<CatalogSnapshot>(row.Payload, SerializerOptions);
    }

    /// <inheritdoc />
    public async Task<long> GetAppliedVersionAsync(CancellationToken cancellationToken)
    {
        var row = await _context.CatalogSnapshots
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == CatalogSnapshotEntity.SingleRowKey, cancellationToken)
            .ConfigureAwait(false);

        return row?.CatalogVersion ?? 0L;
    }

    /// <inheritdoc />
    public async Task<DateTimeOffset?> GetLastSynchronizedAtAsync(CancellationToken cancellationToken)
    {
        var row = await _context.CatalogSnapshots
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == CatalogSnapshotEntity.SingleRowKey, cancellationToken)
            .ConfigureAwait(false);

        if (row is null || string.IsNullOrWhiteSpace(row.SynchronizedAt))
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            row.SynchronizedAt,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind,
            out var instant)
            ? instant
            : null;
    }

    /// <inheritdoc />
    public async Task SaveAsync(CatalogSnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        // La versión y el payload que viajan juntos se escriben en la MISMA transacción:
        // un cliente que reinicia nunca lee una versión que anuncie una publicación que
        // no está en el payload (AD-26).
        await using var transaction = await _context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var row = await _context.CatalogSnapshots
            .FirstOrDefaultAsync(
                candidate => candidate.Id == CatalogSnapshotEntity.SingleRowKey,
                cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            row = new CatalogSnapshotEntity { Id = CatalogSnapshotEntity.SingleRowKey };

            _context.CatalogSnapshots.Add(row);
        }

        row.CatalogVersion = snapshot.CatalogVersion;
        row.SynchronizedAt = _timeProvider.GetUtcNow().ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ");
        row.Payload = JsonSerializer.Serialize(snapshot, SerializerOptions);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InstalledGame>> LoadInstalledGamesAsync(CancellationToken cancellationToken)
    {
        var rows = await _context.Installations
            .AsNoTracking()
            .OrderBy(installation => installation.GameId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .Select(row => new InstalledGame(
                row.GameId,
                row.InstalledVersion,
                ParseInstallationState(row.ValidationState)))
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ActiveOperation>> LoadActiveOperationsAsync(CancellationToken cancellationToken)
    {
        var rows = await _context.DownloadOperations
            .AsNoTracking()
            .OrderBy(operation => operation.OperationId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .Select(row => new ActiveOperation(
                row.OperationId,
                row.PackageId,
                ParseDownloadState(row.State)))
            .ToArray();
    }

    /// <summary>
    /// Reads the state of a download operation. A state that the enumeration does not know
    /// is read as <see cref="DownloadState.Queued"/>, because it is the state that every
    /// operation starts in and it is the only one from which the operation can move
    /// (AD-10).
    /// </summary>
    /// <param name="value">Name of the state as it is persisted.</param>
    /// <returns>The state of the operation.</returns>
    private static DownloadState ParseDownloadState(string value) =>
        Enum.TryParse<DownloadState>(value, ignoreCase: false, out var state)
            ? state
            : DownloadState.Queued;

    /// <summary>
    /// Reads the state of an installation. A state that the enumeration does not know is
    /// read as <see cref="InstallationState.Pending"/>, because it is the state that every
    /// installation starts in (AD-10).
    /// </summary>
    /// <param name="value">Name of the state as it is persisted.</param>
    /// <returns>The state of the installation.</returns>
    private static InstallationState ParseInstallationState(string value) =>
        Enum.TryParse<InstallationState>(value, ignoreCase: false, out var state)
            ? state
            : InstallationState.Pending;
}
