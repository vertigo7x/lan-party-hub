using LANPartyHub.Server.Application.Ports;
using LANPartyHub.Server.Domain.Catalog;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Server.Application.Catalog;

/// <summary>
/// Use case of the modification of a videogame of the catalog (AD-1, AD-5, AD-34).
///
/// <para>
/// The use case validates the eighteen mandatory metadata of FR-1 through the domain,
/// keeps the identifier of the videogame that the body carries, delegates the persistence
/// in the port of the repository and returns the contract of <c>Shared</c> with the new
/// metadata. A videogame that is not in the catalog is rejected (AD-22).
/// </para>
/// </summary>
public sealed class ModifyGame
{
    private readonly IGameRepository _repository;
    private readonly ILogger<ModifyGame> _logger;

    /// <summary>Creates the use case of the modification of a videogame.</summary>
    /// <param name="repository">Port of the repository of the videogames.</param>
    /// <param name="logger">Log of the use case.</param>
    public ModifyGame(IGameRepository repository, ILogger<ModifyGame> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Modifies a videogame of the catalog of the event. The identifier of the videogame is
    /// never changed (AD-34).
    /// </summary>
    /// <param name="gameId">Identifier of the videogame to modify.</param>
    /// <param name="request">Metadata of the videogame.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The contract of the videogame with the new metadata.</returns>
    /// <exception cref="GameMetadataMissingException">A mandatory metadata is empty or absent.</exception>
    /// <exception cref="GamePlayerRangeException">The minimum of players is greater than the maximum.</exception>
    /// <exception cref="GameNotFoundException">The catalog does not hold that videogame.</exception>
    public async Task<GameDescriptor> ExecuteAsync(
        string gameId,
        GameMetadata request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var existing = await _repository.FindAsync(gameId, cancellationToken).ConfigureAwait(false);

        if (existing is null)
        {
            throw new GameNotFoundException(gameId);
        }

        var game = new GameRegistration(
            existing.GameId,
            request.Name,
            request.Description,
            request.Categories,
            request.Tags,
            request.CoverImage,
            request.Images,
            request.Version,
            request.SizeBytes,
            request.MinPlayers,
            request.MaxPlayers,
            request.OperatingSystem,
            request.DistributionType,
            request.InstallationType,
            request.LaunchConfiguration.ExecutablePath,
            request.LaunchConfiguration.Arguments,
            request.LaunchConfiguration.WorkingDirectory,
            request.Dependencies,
            request.License,
            request.DistributionRestrictions);

        var updated = await _repository.UpdateAsync(game, cancellationToken).ConfigureAwait(false);

        if (!updated)
        {
            throw new GameNotFoundException(gameId);
        }

        _logger.LogInformation(
            "Videojuego modificado en el catálogo con el identificador {GameId} y la versión {Version}.",
            game.GameId,
            game.Version);

        return RegisterGame.ToDescriptor(game);
    }
}
