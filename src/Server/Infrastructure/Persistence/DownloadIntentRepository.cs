using LANPartyHub.Server.Application.Ports;
using LANPartyHub.Server.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace LANPartyHub.Server.Infrastructure.Persistence;

/// <summary>
/// Adapter of the registry of the download intentions (AD-10, AD-22).
///
/// <para>
/// The adapter writes in the table <c>DOWNLOAD_INTENT</c> of the SQLite file of the
/// server. The row it writes is the REFLECTION of the intention of the client and NOT the
/// state of the operation: the operation is executed and owned by the client, and the
/// server only projects the state that the client publishes (AD-10, AD-22).
/// </para>
/// <para>
/// La tabla <c>DOWNLOAD_INTENT</c> declara una clave foránea hacia
/// <c>CLIENT_REGISTRATION</c>, así que la intención exige una fila de registro del
/// cliente. Esta historia crea esa fila en su forma MÍNIMA cuando el cliente todavía no
/// está registrado, y la historia del registro la completa después sin duplicarla
/// (AD-22, AD-30).
/// </para>
/// </summary>
public sealed class DownloadIntentRepository : IDownloadIntentRepository
{
    private readonly CatalogDbContext _context;

    /// <summary>Creates the adapter of the registry of the download intentions.</summary>
    /// <param name="context">Context of the database of the server.</param>
    public DownloadIntentRepository(CatalogDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
    }

    /// <inheritdoc />
    public async Task AddAsync(DownloadIntentRow intent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(intent);

        await EnsureClientRegistrationAsync(intent.ClientId, cancellationToken).ConfigureAwait(false);

        _context.DownloadIntents.Add(new DownloadIntentEntity
        {
            OperationId = intent.OperationId,
            ClientId = intent.ClientId,
            PackageId = intent.PackageId,
            State = intent.State,
            Outcome = intent.Outcome,
        });

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<DownloadIntentRow?> FindAsync(string operationId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);

        var entity = await _context.DownloadIntents
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.OperationId == operationId, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return null;
        }

        return new DownloadIntentRow(
            entity.OperationId,
            entity.ClientId,
            entity.PackageId,
            entity.State,
            entity.Outcome);
    }

    /// <inheritdoc />
    public async Task<bool> KnowsClientAsync(string clientId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

        return await _context.ClientRegistrations
            .AnyAsync(candidate => candidate.ClientId == clientId, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SaveOutcomeAsync(DownloadIntentRow intent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentException.ThrowIfNullOrWhiteSpace(intent.OperationId);

        var entity = await _context.DownloadIntents
            .FirstOrDefaultAsync(
                candidate => candidate.OperationId == intent.OperationId,
                cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            // La operación NO tiene intención registrada: no hay fila que actualizar.
            return;
        }

        // AD-33: `outcome` NO sustituye a `state`. El estado sigue siendo el de la
        // máquina y el resultado es el terminal. La escritura es idempotente: el mismo
        // resultado reportado dos veces deja la misma fila (AD-22).
        entity.Outcome = intent.Outcome;

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates the row of the registration of the client when the server does not know it
    /// yet (AD-22, AD-30).
    ///
    /// <para>
    /// La tabla <c>DOWNLOAD_INTENT</c> declara una clave foránea hacia
    /// <c>CLIENT_REGISTRATION</c>, así que la intención no se puede escribir sin la fila
    /// del cliente. Esta historia la crea en su forma MÍNIMA —el identificador y la
    /// etiqueta de la máquina— y la historia del registro la COMPLETA sin duplicarla: la
    /// fila se busca antes de crearla, y una fila que ya existe nunca se sustituye
    /// (AD-30).
    /// </para>
    /// </summary>
    /// <param name="clientId">Opaque identifier of the client.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the registration exists.</returns>
    private async Task EnsureClientRegistrationAsync(string clientId, CancellationToken cancellationToken)
    {
        var existing = await _context.ClientRegistrations
            .AnyAsync(candidate => candidate.ClientId == clientId, cancellationToken)
            .ConfigureAwait(false);

        if (existing)
        {
            return;
        }

        _context.ClientRegistrations.Add(new ClientRegistrationEntity
        {
            ClientId = clientId,

            // La etiqueta de la máquina es la del propio identificador hasta que la
            // historia del registro la fije: la fila existe para que la clave foránea se
            // cumpla, y no para afirmar el nombre del equipo (AD-30).
            MachineName = clientId,
            ClientVersion = string.Empty,
            ProtocolVersion = 0,
            ConnectionState = string.Empty,
            LastSeenAt = string.Empty,
        });
    }
}
