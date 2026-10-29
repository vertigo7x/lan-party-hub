using LANPartyHub.Server.Application.Ports;
using LANPartyHub.Server.Domain.Packages;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Server.Application.Packages;

/// <summary>
/// Use case of the withdrawal of a videogame from the catalog of the event
/// (AC2, AD-26).
///
/// <para>
/// The use case withdraws a videogame that the organizer retires. The withdrawal MARKS
/// and never deletes: no row of the packages, of the videogames or of the projection of
/// the installations of the clients is removed, so the installation that a client already
/// made is not invalidated and the videogame can be published again with a new version
/// (AC2, AD-26).
/// </para>
/// <para>
/// The version of the catalog grows in the same transaction as the marking, because the
/// version announces the change of the catalog to the clients that are about to receive
/// the snapshot (AC3, AD-26).
/// </para>
/// <para>
/// The videogame that does not exist is rejected with a typed exception of the domain,
/// and the rejection happens BEFORE any write: nothing is marked and the version does
/// not grow (AD-1, AD-15, AD-25).
/// </para>
/// </summary>
public sealed class WithdrawGame
{
    private readonly IGameRepository _gameRepository;
    private readonly IPackageRepository _packageRepository;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<WithdrawGame> _logger;

    /// <summary>Creates the use case of the withdrawal of a videogame.</summary>
    /// <param name="gameRepository">Port of the repository of the videogames.</param>
    /// <param name="packageRepository">Port of the repository of the published packages.</param>
    /// <param name="logger">Log of the use case.</param>
    /// <param name="timeProvider">
    /// Source of the instant of the withdrawal. It defaults to the real one and it exists
    /// so that the mark of the withdrawal can be exercised with a known instant.
    /// </param>
    public WithdrawGame(
        IGameRepository gameRepository,
        IPackageRepository packageRepository,
        ILogger<WithdrawGame> logger,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(gameRepository);
        ArgumentNullException.ThrowIfNull(packageRepository);
        ArgumentNullException.ThrowIfNull(logger);

        _gameRepository = gameRepository;
        _packageRepository = packageRepository;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger;
    }

    /// <summary>
    /// Withdraws a videogame from the catalog of the event.
    /// </summary>
    /// <param name="gameId">Identifier of the videogame to withdraw.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The outcome of the withdrawal.</returns>
    /// <exception cref="GameWithdrawalNotFoundException">
    /// The videogame is not in the catalog of the event.
    /// </exception>
    public async Task<WithdrawalOutcome> ExecuteAsync(
        string gameId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);

        // The videogame that does not exist is rejected BEFORE any write, so the
        // withdrawal of a videogame that was never registered leaves the catalog exactly
        // as it was (AD-1, AD-25).
        var game = await _gameRepository.FindAsync(gameId, cancellationToken).ConfigureAwait(false);

        if (game is null)
        {
            throw new GameWithdrawalNotFoundException(gameId);
        }

        // The mark of the withdrawal is an instant in UTC ISO-8601, the same convention
        // that the rest of the persisted instants of the server follow.
        var withdrawnAt = _timeProvider.GetUtcNow().ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ");

        var result = await _packageRepository
            .WithdrawGameAsync(gameId, withdrawnAt, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Se han retirado {Count} paquetes publicados del videogame {GameId}. La versión del catálogo es {CatalogVersion}.",
            result.WithdrawnPackages,
            gameId,
            result.CatalogVersion);

        return new WithdrawalOutcome(
            gameId,
            result.WithdrawnPackages,
            result.CatalogVersion,
            withdrawnAt);
    }
}

/// <summary>
/// The outcome of the withdrawal of a videogame (AC2, AD-26).
///
/// <para>
/// The outcome carries the videogame that was withdrawn, how many packages the
/// withdrawal marked, the version of the catalog after the withdrawal and the instant of
/// the mark, so the interface can tell the organizer what happened without reading the
/// database again.
/// </para>
/// </summary>
/// <param name="GameId">Identifier of the videogame that was withdrawn.</param>
/// <param name="WithdrawnPackages">Number of packages that the withdrawal marked.</param>
/// <param name="CatalogVersion">Version of the catalog after the withdrawal.</param>
/// <param name="WithdrawnAt">Instant of the withdrawal, in UTC ISO-8601.</param>
public sealed record WithdrawalOutcome(
    string GameId,
    int WithdrawnPackages,
    long CatalogVersion,
    string WithdrawnAt);
