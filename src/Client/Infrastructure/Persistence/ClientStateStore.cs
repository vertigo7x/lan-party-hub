using LANPartyHub.Client.Application;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Domain;
using LANPartyHub.Client.Domain.Trust;
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
public sealed class ClientStateStore : IClientStateStore, ITrustAnchorReader
{
    private readonly ClientStateDbContext _context;

    /// <summary>
    /// Gate that serializes the access to the context (AD-11, AD-24).
    ///
    /// <para>
    /// Un <see cref="DbContext"/> NO es seguro entre hilos: EF Core lo rechaza con
    /// «A second operation was started on this context instance before a previous operation
    /// completed» cuando dos operaciones se solapan. La descarga PERSISTE el offset
    /// mientras corre (AD-11, SM-2) y la publicación del progreso LEE la identidad al
    /// mismo tiempo, así que las dos operaciones comparten el contexto y se solapan de
    /// verdad.
    /// </para>
    /// <para>
    /// La puerta serializa el acceso: cada operación espera a que la anterior termine,
    /// así que el contexto nunca lleva dos operaciones a la vez. La puerta se toma sobre
    /// el contexto COMPLETO, no sobre una parte, porque la restricción de EF Core es del
    /// contexto entero.
    /// </para>
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

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
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // AD-11: the migrations are versioned and they are applied automatically at startup.
            // EnsureCreated is never used, because it skips the history of the migrations.
            await _context.Database.MigrateAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<ClientIdentity?> LoadIdentityAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
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
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task SaveIdentityAsync(ClientIdentity identity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
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
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PersistedDownloadOperation>> LoadDownloadOperationsAsync(
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
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
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task SaveProgressAsync(
        string operationId,
        long bytesPersisted,
        long? totalBytes,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
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
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task SaveOperationAsync(
        string operationId,
        string packageId,
        string state,
        string transferEngine,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(transferEngine);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // AD-10, AD-24: la operación declara el motor que transfiere sus bytes, y el
            // formato de su progreso se deduce de ese valor. El registro se escribe una vez
            // y su estado se ACTUALIZA: el estado de la máquina es del cliente (AD-10).
            var operation = await _context.DownloadOperations
                .FirstOrDefaultAsync(row => row.OperationId == operationId, cancellationToken);

            if (operation is null)
            {
                _context.DownloadOperations.Add(new DownloadOperationEntity
                {
                    OperationId = operationId,
                    PackageId = packageId,
                    State = state,
                    TransferEngine = transferEngine,
                });
            }
            else
            {
                operation.State = state;
            }

            await _context.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<PinnedTrustAnchor?> LoadTrustAnchorAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var row = await _context.TrustAnchors
                .AsNoTracking()
                .OrderBy(anchor => anchor.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (row is null)
            {
                return null;
            }

            return new PinnedTrustAnchor(row.PublicKey, row.Fingerprint, row.FixedAt);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task SaveTrustAnchorAsync(PinnedTrustAnchor anchor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(anchor);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // AD-27: el ancla fijada es INMUTABLE. Una fila que ya existe NO se sustituye por
            // otra clave: el emparejamiento nuevo es una operación explícita del organizador y
            // no un efecto secundario de una conexión.
            var existing = await _context.TrustAnchors
                .AnyAsync(cancellationToken);

            if (existing)
            {
                throw new InvalidOperationException(
                    "El ancla de confianza ya está fijada y no puede sustituirse por otra clave sin un emparejamiento fuera de banda nuevo.");
            }

            _context.TrustAnchors.Add(new TrustAnchorEntity
            {
                Id = TrustAnchorEntity.SingleRowKey,
                PublicKey = anchor.PublicKey,
                Fingerprint = anchor.Fingerprint,
                FixedAt = anchor.FixedAt,
            });

            await _context.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }
}
