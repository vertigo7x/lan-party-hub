using LANPartyHub.Server.Application.Ports;
using LANPartyHub.Server.Domain.Catalog;
using LANPartyHub.Server.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace LANPartyHub.Server.Infrastructure.Persistence;

/// <summary>
/// Adapter of the repository of the videogames of the catalog (AD-1, AD-22, AD-34).
///
/// <para>
/// The adapter writes in the table <c>GAME</c> of the SQLite file of the server and it
/// maps the eighteen metadata of FR-1 with the same name and in the same order of the
/// contract of <c>Shared</c>, so the table and the contract cannot diverge (AD-34).
/// </para>
/// <para>
/// The launch configuration is stored in its canonical JSON form through the converter of
/// the context, and the collections are stored as JSON text the same way (AD-7).
/// </para>
/// </summary>
public sealed class GameRepository : IGameRepository
{
    private readonly CatalogDbContext _context;

    /// <summary>Creates the adapter of the repository of the videogames.</summary>
    /// <param name="context">Context of the database of the server.</param>
    public GameRepository(CatalogDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
    }

    /// <inheritdoc />
    public async Task AddAsync(GameRegistration game, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);

        _context.Games.Add(ToEntity(game));

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<GameRegistration?> FindAsync(string gameId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);

        var entity = await _context.Games
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.GameId == gameId, cancellationToken)
            .ConfigureAwait(false);

        return entity is null ? null : ToRegistration(entity);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GameRegistration>> ListAsync(CancellationToken cancellationToken)
    {
        var entities = await _context.Games
            .AsNoTracking()
            .OrderBy(candidate => candidate.Name)
            .ThenBy(candidate => candidate.GameId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return entities.Select(ToRegistration).ToArray();
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAsync(GameRegistration game, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);

        var entity = await _context.Games
            .FirstOrDefaultAsync(candidate => candidate.GameId == game.GameId, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return false;
        }

        entity.Name = game.Name;
        entity.Description = game.Description;
        entity.Categories = [.. game.Categories];
        entity.Tags = [.. game.Tags];
        entity.CoverImage = game.CoverImage;
        entity.Images = [.. game.Images];
        entity.Version = game.Version;
        entity.SizeBytes = game.SizeBytes;
        entity.MinPlayers = game.MinPlayers;
        entity.MaxPlayers = game.MaxPlayers;
        entity.OperatingSystem = game.OperatingSystem;
        entity.DistributionType = game.DistributionType;
        entity.InstallationType = game.InstallationType;
        entity.LaunchConfiguration = new Shared.Contracts.InstallationManifest.LaunchSettings(
            game.LaunchExecutablePath,
            [.. game.LaunchArguments],
            game.LaunchWorkingDirectory);
        entity.Dependencies = [.. game.Dependencies];
        entity.License = game.License;
        entity.DistributionRestrictions = game.DistributionRestrictions;

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return true;
    }

    /// <summary>
    /// Projects the registration of a videogame onto the row of the table <c>GAME</c>.
    /// </summary>
    /// <param name="game">Registration of the videogame.</param>
    /// <returns>The row of the videogame.</returns>
    private static GameEntity ToEntity(GameRegistration game) => new()
    {
        GameId = game.GameId,
        Name = game.Name,
        Description = game.Description,
        Categories = [.. game.Categories],
        Tags = [.. game.Tags],
        CoverImage = game.CoverImage,
        Images = [.. game.Images],
        Version = game.Version,
        SizeBytes = game.SizeBytes,
        MinPlayers = game.MinPlayers,
        MaxPlayers = game.MaxPlayers,
        OperatingSystem = game.OperatingSystem,
        DistributionType = game.DistributionType,
        InstallationType = game.InstallationType,
        LaunchConfiguration = new Shared.Contracts.InstallationManifest.LaunchSettings(
            game.LaunchExecutablePath,
            [.. game.LaunchArguments],
            game.LaunchWorkingDirectory),
        Dependencies = [.. game.Dependencies],
        License = game.License,
        DistributionRestrictions = game.DistributionRestrictions,
    };

    /// <summary>
    /// Projects the row of the table <c>GAME</c> onto the registration of a videogame.
    ///
    /// <para>
    /// The projection uses the factory of the domain that rebuilds a persisted row
    /// without demanding the license and the restrictions of distribution: a row whose
    /// license is empty has to stay observable, porque la publicación de un paquete es
    /// el momento en que el catálogo los exige y nombra el que falta (AC1).
    /// </para>
    /// </summary>
    /// <param name="entity">Row of the videogame.</param>
    /// <returns>The registration of the videogame.</returns>
    private static GameRegistration ToRegistration(GameEntity entity) => GameRegistration.FromPersisted(
        entity.GameId,
        entity.Name,
        entity.Description,
        entity.Categories,
        entity.Tags,
        entity.CoverImage,
        entity.Images,
        entity.Version,
        entity.SizeBytes,
        entity.MinPlayers,
        entity.MaxPlayers,
        entity.OperatingSystem,
        entity.DistributionType,
        entity.InstallationType,
        entity.LaunchConfiguration.ExecutablePath,
        entity.LaunchConfiguration.Arguments,
        entity.LaunchConfiguration.WorkingDirectory,
        entity.Dependencies,
        entity.License,
        entity.DistributionRestrictions);
}
