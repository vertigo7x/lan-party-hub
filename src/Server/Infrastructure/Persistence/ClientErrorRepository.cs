using LANPartyHub.Server.Application.Ports;
using LANPartyHub.Server.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace LANPartyHub.Server.Infrastructure.Persistence;

/// <summary>
/// Adapter of the registry of the ERRORS that the clients reported (AD-15, AD-22).
///
/// <para>
/// El adaptador LEE la tabla <c>CLIENT_ERROR</c> del archivo SQLite del servidor. La
/// fila conserva la forma de la única forma de error del sistema: el código estable
/// del catálogo, el mensaje, la causa y el siguiente paso (AD-15).
/// </para>
/// <para>
/// El adaptador NO escribe: la producción de los errores es de otra historia, y el
/// resultado terminal de una operación retirada del catálogo NUNCA se registra aquí
/// (AD-33, R-MVP-8).
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
}
