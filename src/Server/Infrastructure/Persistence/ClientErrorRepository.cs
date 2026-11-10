using LANPartyHub.Server.Application.Ports;
using LANPartyHub.Server.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace LANPartyHub.Server.Infrastructure.Persistence;

/// <summary>
/// Adapter of the registry of the ERRORS that the clients reported (AD-15, AD-22).
///
/// <para>
/// El adaptador LEE y ESCRIBE la tabla <c>CLIENT_ERROR</c> del archivo SQLite del
/// servidor. La fila conserva la forma de la única forma de error del sistema: el
/// código estable del catálogo, el mensaje, la causa y el siguiente paso (AD-15).
/// </para>
/// <para>
/// La tabla YA EXISTE desde la migración inicial: esta historia escribe en ella y NO
/// añade ninguna migración de esquema (AD-15, AD-22).
/// </para>
/// <para>
/// La escritura es un UPSERT idempotente por la clave primaria <c>PK_CLIENT_ERROR</c>:
/// la republicación del mismo error NO crea una segunda fila, y dos reportes
/// CONCURRENTES del mismo error NO compiten por la clave (AD-15, AD-22).
/// </para>
/// <para>
/// El resultado terminal de una operación retirada del catálogo NUNCA llega a esta
/// tabla: es un RESULTADO y no un fallo (AD-33, R-MVP-8).
/// </para>
/// </summary>
public sealed class ClientErrorRepository : IClientErrorRepository
{
    private readonly CatalogDbContext _context;

    /// <summary>Creates the adapter of the registry of the errors.</summary>
    /// <param name="context">Context of the database of the server.</param>
    public ClientErrorRepository(CatalogDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ClientErrorRow>> ListByClientAsync(
        string clientId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

        var entities = await _context.ClientErrors
            .AsNoTracking()
            .Where(candidate => candidate.ClientId == clientId)
            .OrderBy(candidate => candidate.OccurredAt)
            .ThenBy(candidate => candidate.ErrorId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. entities.Select(entity => new ClientErrorRow(
                entity.ErrorId,
                entity.OperationId,
                entity.Code,
                entity.Message,
                entity.Cause,
                entity.NextStep,
                ParseOccurredAt(entity.OccurredAt)))
        ];
    }

    /// <inheritdoc />
    public async Task SaveAsync(ClientErrorWrite error, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(error);

        // AD-15, AD-22: la escritura es IDEMPOTENTE por `errorId`. El mismo error
        // reportado dos veces NO crea una segunda fila, así que una republicación del
        // cliente no duplica el diagnóstico que el organizador ve.
        //
        // La escritura es un UPSERT `INSERT OR IGNORE` sobre la clave primaria
        // `PK_CLIENT_ERROR`: la fila que ya existe se CONSERVA y la que no existe se
        // inserta. NO es un "consultar y después insertar", porque dos reportes del
        // mismo error que llegan CONCURRENTES pasarían los dos la consulta y el
        // segundo insertaría sobre una clave que ya existe, con un `DbUpdateException`
        // que rompería la publicación del cliente.
        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT OR IGNORE INTO CLIENT_ERROR
                 (errorId, clientId, operationId, code, message, cause, nextStep, occurredAt)
             VALUES
                 ({error.ErrorId}, {error.ClientId}, {error.OperationId}, {error.Code},
                  {error.Message}, {error.Cause}, {error.NextStep}, {FormatOccurredAt(error.OccurredAt)})
             """,
            cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the instant of the error of the row. A row whose instant is absent or
    /// unreadable reads as the EPOCH, que ordena antes que cualquier error real
    /// (AD-22).
    /// </summary>
    /// <param name="value">Text of the instant, in UTC ISO-8601.</param>
    /// <returns>The instant of the error.</returns>
    private static DateTimeOffset ParseOccurredAt(string value) =>
        DateTimeOffset.TryParse(
            value,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind,
            out var instant)
            ? instant.ToUniversalTime()
            : DateTimeOffset.UnixEpoch;

    /// <summary>
    /// Writes the instant of the error en UTC ISO-8601, el mismo formato que la fila
    /// lee, así que el instante que se registra y el que se lee son el MISMO (AD-22).
    /// </summary>
    /// <param name="instant">Instant of the error.</param>
    /// <returns>The text of the instant.</returns>
    private static string FormatOccurredAt(DateTimeOffset instant) =>
        instant.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture);
}
