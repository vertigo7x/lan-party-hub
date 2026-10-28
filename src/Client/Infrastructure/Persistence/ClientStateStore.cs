using LANPartyHub.Client.Application;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Domain;
using LANPartyHub.Client.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace LANPartyHub.Client.Infrastructure.Persistence;

/// <summary>
/// Access to the database of the client (AD-11, AD-22, AD-23).
///
/// <para>
/// The adapter owns the schema of the file <c>state.sqlite</c>. The table of the identity
/// holds a single row: the identifier that the client emitted once. The identifier is never
/// rewritten, so a restart and the replacement of the binary keep the same identity while the
/// data folder survives (AD-23).
/// </para>
/// </summary>
public sealed class ClientStateStore : IClientStateStore
{
    private readonly ClientStateDbContext _context;

    /// <summary>Creates the adapter over the database of the client.</summary>
    /// <param name="context">Database of the client.</param>
    public ClientStateStore(ClientStateDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
    }

    /// <inheritdoc />
    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        // AD-11: the migrations are versioned and they are applied automatically at startup.
        // EnsureCreated is never used, because it skips the history of the migrations.
        await _context.Database.MigrateAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ClientIdentity?> LoadIdentityAsync(CancellationToken cancellationToken)
    {
        var row = await _context.Identities
            .AsNoTracking()
            .OrderBy(identity => identity.ClientId)
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        if (!ClientIdentity.IsCanonical(row.ClientId))
        {
            throw new InvalidOperationException(
                $"La identidad persistida del cliente no está en su forma canónica. "
                + "Restaura la copia de la carpeta de datos o borra la fila 'CLIENT_IDENTITY' para emitir un identificador nuevo.");
        }

        return ClientIdentity.FromCanonical(row.ClientId);
    }

    /// <inheritdoc />
    public async Task SaveIdentityAsync(ClientIdentity identity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);

        // AD-23: the identifier is emitted once. A row that already exists is never
        // rewritten, so a second startup cannot change the identity of the team.
        var existing = await _context.Identities
            .AnyAsync(cancellationToken);

        if (existing)
        {
            throw new InvalidOperationException(
                "La identidad del cliente ya está persistida y no puede volver a emitirse.");
        }

        _context.Identities.Add(new ClientIdentityEntity { ClientId = identity.CanonicalId });

        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PersistedDownloadOperation>> LoadDownloadOperationsAsync(
        CancellationToken cancellationToken)
    {
        var operations = await _context.DownloadOperations
            .AsNoTracking()
            .OrderBy(operation => operation.OperationId)
            .ToListAsync(cancellationToken);

        var progresses = await _context.DownloadProgresses
            .AsNoTracking()
            .ToDictionaryAsync(progress => progress.OperationId, StringComparer.Ordinal, cancellationToken);

        return operations
            .Select(operation => new PersistedDownloadOperation(
                operation.OperationId,
                operation.PackageId,
                operation.State,
                operation.TransferEngine,
                progresses.TryGetValue(operation.OperationId, out var progress) ? progress.BytesPersisted : null,
                progresses.TryGetValue(operation.OperationId, out var same) ? same.TotalBytes : null))
            .ToArray();
    }

    /// <inheritdoc />
    public async Task SaveProgressAsync(
        string operationId,
        long bytesPersisted,
        long? totalBytes,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);

        var progress = await _context.DownloadProgresses
            .FirstOrDefaultAsync(row => row.OperationId == operationId, cancellationToken);

        if (progress is null)
        {
            progress = new DownloadProgressEntity { OperationId = operationId, TotalBytes = totalBytes ?? 0L };
            _context.DownloadProgresses.Add(progress);
        }
        else if (totalBytes is { } total)
        {
            // A total that is unknown does not replace the persisted one.
            progress.TotalBytes = total;
        }

        progress.BytesPersisted = bytesPersisted;

        await _context.SaveChangesAsync(cancellationToken);
    }
}
