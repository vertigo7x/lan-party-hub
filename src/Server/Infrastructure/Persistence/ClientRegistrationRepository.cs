using LANPartyHub.Server.Application.Ports;
using LANPartyHub.Server.Domain.Clients;
using LANPartyHub.Server.Infrastructure.Persistence.Entities;
using LANPartyHub.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace LANPartyHub.Server.Infrastructure.Persistence;

/// <summary>
/// Adapter of the registry of the clients of the event (AD-22, AD-23, AD-30).
///
/// <para>
/// El adaptador escribe en la tabla <c>CLIENT_REGISTRATION</c> del archivo SQLite del
/// servidor. La fila se deduplica SIEMPRE por su <c>clientId</c>, que es la clave
/// técnica: el nombre del equipo es una etiqueta, y su unicidad la garantiza el
/// SERVIDOR, que es el validador y el que la hace única (AD-23, AD-30).
/// </para>
/// <para>
/// La unicidad del nombre NO la exige un índice de la base de datos, porque la
/// comparación es por FORMA CANÓNICA y no por el texto guardado: SQLite no normaliza,
/// así que un índice sobre la columna compararía el texto y no la forma. El adaptador
/// resuelve la colisión por la forma canónica, que es la regla de AD-30, y el
/// esquema de la tabla es el del ERD de la espina, sin columnas añadidas (AD-22,
/// AD-30).
/// </para>
/// <para>
/// Una fila que ya existe NUNCA se sustituye: se COMPLETA. La historia del registro
/// de la intención de una descarga crea la fila en su forma mínima para que la clave
/// foránea se cumpla, y esta historia la completa con el nombre efectivo y las
/// versiones del cliente (AD-30).
/// </para>
/// </summary>
public sealed class ClientRegistrationRepository : IClientRegistrationRepository
{
    private readonly CatalogDbContext _context;

    /// <summary>Creates the adapter of the registry of the clients.</summary>
    /// <param name="context">Context of the database of the server.</param>
    public ClientRegistrationRepository(CatalogDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
    }

    /// <inheritdoc />
    public async Task<ClientRegistrationRow?> FindByClientIdAsync(
        string clientId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

        var entity = await _context.ClientRegistrations
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.ClientId == clientId, cancellationToken)
            .ConfigureAwait(false);

        return entity is null ? null : ToRow(entity);
    }

    /// <inheritdoc />
    public async Task<ClientRegistrationRow?> FindByCanonicalMachineNameAsync(
        string canonicalMachineName,
        string excludedClientId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalMachineName);
        ArgumentException.ThrowIfNullOrWhiteSpace(excludedClientId);

        // AD-30: la comparación de unicidad es por FORMA CANÓNICA, así que la fila se
        // busca entre las que hay y se compara su forma canónica, no su texto. La
        // búsqueda es sobre el REGISTRO y no sobre un índice, porque el índice de la
        // tabla no puede expresar una comparación por forma canónica (AD-30).
        var entities = await _context.ClientRegistrations
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var entity in entities)
        {
            // AD-30: la fila del propio cliente NO cuenta como ocupante del nombre, y
            // sin esta exclusión taparía la de otro cliente con el mismo nombre.
            if (string.Equals(entity.ClientId, excludedClientId, StringComparison.Ordinal))
            {
                continue;
            }

            if (string.Equals(
                    MachineNamePolicy.Canonicalize(entity.MachineName),
                    canonicalMachineName,
                    StringComparison.Ordinal))
            {
                return ToRow(entity);
            }
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ClientRegistrationRow>> ListAsync(CancellationToken cancellationToken)
    {
        // El ORDEN lo fija el caso de uso, que es el que lo aplica sobre la lista
        // completa: el repositorio devuelve las filas sin ordenarlos para no ordenar
        // dos veces lo mismo (AD-29, UX-DR-14).
        var entities = await _context.ClientRegistrations
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. entities.Select(ToRow)];
    }

    /// <inheritdoc />
    public async Task SaveAsync(ClientRegistrationRow row, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(row);

        var entity = await _context.ClientRegistrations
            .FirstOrDefaultAsync(candidate => candidate.ClientId == row.ClientId, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            _context.ClientRegistrations.Add(new ClientRegistrationEntity
            {
                ClientId = row.ClientId,
                MachineName = row.MachineName,
                ClientVersion = row.ClientVersion,
                ProtocolVersion = row.ProtocolVersion,
                ConnectionState = row.ConnectionState.ToString(),
                LastSeenAt = row.LastSeenAt.ToUniversalTime().ToString("O"),
            });
        }
        else
        {
            // AD-30: la fila que ya existe se COMPLETA, nunca se sustituye ni se
            // duplica. El nombre efectivo es el que se conserva.
            entity.MachineName = row.MachineName;
            entity.ClientVersion = row.ClientVersion;
            entity.ProtocolVersion = row.ProtocolVersion;
            entity.ConnectionState = row.ConnectionState.ToString();
            entity.LastSeenAt = row.LastSeenAt.ToUniversalTime().ToString("O");
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Projects the row of the table onto the row that the use case sees. The state of
    /// the connection is TEXT in the table and the instant is ISO-8601, so both are
    /// read back (AD-22).
    /// </summary>
    /// <param name="entity">Row of the table.</param>
    /// <returns>The row of the use case.</returns>
    private static ClientRegistrationRow ToRow(ClientRegistrationEntity entity) => new(
        entity.ClientId,
        entity.MachineName,
        entity.ClientVersion,
        entity.ProtocolVersion,
        ParseConnectionState(entity.ConnectionState),
        ParseLastSeenAt(entity.LastSeenAt));

    /// <summary>
    /// Reads the state of the connection of the row. A row whose state is absent or
    /// unknown reads as DISCONNECTED, porque un cliente del que no se sabe nada no
    /// puede presentarse como conectado (AD-22).
    /// </summary>
    /// <param name="value">Text of the state of the connection.</param>
    /// <returns>The state of the connection.</returns>
    private static ConnectionState ParseConnectionState(string value) =>
        Enum.TryParse<ConnectionState>(value, ignoreCase: false, out var state)
            ? state
            : ConnectionState.Disconnected;

    /// <summary>
    /// Reads the instant of the last contact of the row. A row whose instant is absent
    /// or unreadable reads as the EPOCH, que ordena antes que cualquier contacto real
    /// (AD-22).
    /// </summary>
    /// <param name="value">Text of the instant, in UTC ISO-8601.</param>
    /// <returns>The instant of the last contact.</returns>
    private static DateTimeOffset ParseLastSeenAt(string value) =>
        DateTimeOffset.TryParse(
            value,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind,
            out var instant)
            ? instant.ToUniversalTime()
            : DateTimeOffset.UnixEpoch;
}
