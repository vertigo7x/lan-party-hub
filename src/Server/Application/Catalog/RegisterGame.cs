using LANPartyHub.Server.Application.Ports;
using LANPartyHub.Server.Domain.Catalog;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Server.Application.Catalog;

/// <summary>
/// Use case of the registration of a videogame in the catalog (AD-1, AD-5, AD-34).
///
/// <para>
/// The use case validates the eighteen mandatory metadata of FR-1 through the domain,
/// generates the identifier of the videogame, delegates the persistence in the port of the
/// repository and returns the contract of <c>Shared</c> that the API publishes. It does not
/// know the adapter that persists it (AD-1).
/// </para>
/// <para>
/// The input is the contract of <c>Shared</c> itself and not a type of the server that
/// repeats its eighteen fields: <c>GameDescriptor</c> is the single contract of the
/// metadata of a videogame, and a second type with the same fields would be able to
/// diverge from it (AD-34).
/// </para>
/// </summary>
public sealed class RegisterGame
{
    private readonly IGameRepository _repository;
    private readonly ILogger<RegisterGame> _logger;

    /// <summary>Creates the use case of the registration of a videogame.</summary>
    /// <param name="repository">Port of the repository of the videogames.</param>
    /// <param name="logger">Log of the use case.</param>
    public RegisterGame(IGameRepository repository, ILogger<RegisterGame> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Registers a videogame in the catalog of the event.
    /// </summary>
    /// <param name="metadata">Metadata of the videogame to register.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The contract of the videogame that has been registered.</returns>
    /// <exception cref="GameMetadataMissingException">A mandatory metadata is empty or absent.</exception>
    /// <exception cref="GamePlayerRangeException">The minimum of players is greater than the maximum.</exception>
    public async Task<GameDescriptor> ExecuteAsync(GameDescriptor metadata, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var game = new GameRegistration(
            GameRegistration.GenerateGameId(),
            metadata.Name,
            metadata.Description,
            metadata.Categories,
            metadata.Tags,
            metadata.CoverImage,
            metadata.Images,
            metadata.Version,
            metadata.SizeBytes,
            metadata.MinPlayers,
            metadata.MaxPlayers,
            metadata.OperatingSystem,
            metadata.DistributionType,
            metadata.InstallationType,
            metadata.LaunchConfiguration.ExecutablePath,
            metadata.LaunchConfiguration.Arguments,
            metadata.LaunchConfiguration.WorkingDirectory,
            metadata.Dependencies,
            metadata.License,
            metadata.DistributionRestrictions);

        await _repository.AddAsync(game, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Videojuego registrado en el catálogo con el identificador {GameId} y la versión {Version}.",
            game.GameId,
            game.Version);

        return ToDescriptor(game);
    }

    /// <summary>
    /// Projects the registration of a videogame onto the contract of <c>Shared</c>, which
    /// has the eighteen fields in the fixed order of AD-34.
    /// </summary>
    /// <param name="game">Registration of the videogame.</param>
    /// <returns>The contract of the videogame.</returns>
    public static GameDescriptor ToDescriptor(GameRegistration game)
    {
        ArgumentNullException.ThrowIfNull(game);

        return new GameDescriptor(
            game.GameId,
            game.Name,
            game.Description,
            game.Categories,
            game.Tags,
            game.CoverImage,
            game.Images,
            game.Version,
            game.SizeBytes,
            game.MinPlayers,
            game.MaxPlayers,
            game.OperatingSystem,
            game.DistributionType,
            game.InstallationType,
            new InstallationManifest.LaunchSettings(
                game.LaunchExecutablePath,
                game.LaunchArguments,
                game.LaunchWorkingDirectory),
            game.Dependencies,
            game.License,
            game.DistributionRestrictions);
    }
}
