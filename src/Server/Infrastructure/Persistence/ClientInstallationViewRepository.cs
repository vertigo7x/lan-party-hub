using LANPartyHub.Server.Application.Ports;
using LANPartyHub.Server.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace LANPartyHub.Server.Infrastructure.Persistence;

/// <summary>
/// Adapter of the projection of the installations of the clients (AD-22, FR-10).
///
/// <para>
/// El adaptador escribe en la tabla <c>CLIENT_INSTALLATION_VIEW</c> del archivo SQLite
/// del servidor. La fila se deduplica SIEMPRE por la clave <c>(clientId, gameId)</c>: la
/// proyección se COMPLETA y nunca se duplica, así que publicar dos veces la misma
/// instalación deja UNA fila (AD-22).
/// </para>
/// <para>
/// Una instalación RETIRADA del catálogo NO se borra: la fila se conserva mientras el
/// cliente la siga reportando, porque la proyección refleja lo que el cliente TIENE y no
/// lo que el catálogo publica (AD-26).
/// </para>
/// <para>
/// El estado que se guarda es el TEXTO de presentación copiado del
/// <c>StateDisplayMap</c> de <c>Shared</c>, y NUNCA el enum del cliente (AD-10, AD-22).
/// </para>
/// </summary>
public sealed class ClientInstallationViewRepository : IClientInstallationViewRepository
{
    private readonly CatalogDbContext _context;

    /// <summary>Creates the adapter of the projection of the installations.</summary>
    /// <param name="context">Context of the database of the server.</param>
    public ClientInstallationViewRepository(CatalogDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
    }

    /// <inheritdoc />
    public async Task SaveAsync(ClientInstallationViewRow row, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentException.ThrowIfNullOrWhiteSpace(row.ClientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(row.GameId);
        ArgumentException.ThrowIfNullOrWhiteSpace(row.InstalledVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(row.ValidationStateText);

        var entity = await _context.ClientInstallationViews
            .FirstOrDefaultAsync(
                candidate => candidate.ClientId == row.ClientId && candidate.GameId == row.GameId,
                cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            _context.ClientInstallationViews.Add(new ClientInstallationView
            {
                ClientId = row.ClientId,
                GameId = row.GameId,
                InstalledVersion = row.InstalledVersion,
                ValidationStateText = row.ValidationStateText,
            });
        }
        else
        {
            // AD-22: la fila que ya existe se COMPLETA, nunca se sustituye ni se
            // duplica.
            entity.InstalledVersion = row.InstalledVersion;
            entity.ValidationStateText = row.ValidationStateText;
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RemoveAbsentAsync(
        string clientId,
        IReadOnlySet<string> reportedGameIds,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentNullException.ThrowIfNull(reportedGameIds);

        var absent = await _context.ClientInstallationViews
            .Where(candidate =>
                candidate.ClientId == clientId && !reportedGameIds.Contains(candidate.GameId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (absent.Count == 0)
        {
            return;
        }

        _context.ClientInstallationViews.RemoveRange(absent);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ClientInstallationViewRow>> ListByClientAsync(
        string clientId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

        var entities = await _context.ClientInstallationViews
            .AsNoTracking()
            .Where(candidate => candidate.ClientId == clientId)
            .OrderBy(candidate => candidate.GameId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. entities.Select(entity => new ClientInstallationViewRow(
                entity.ClientId,
                entity.GameId,
                entity.InstalledVersion,
                entity.ValidationStateText))
        ];
    }
}
