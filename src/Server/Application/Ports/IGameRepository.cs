using LANPartyHub.Server.Domain.Catalog;

namespace LANPartyHub.Server.Application.Ports;

/// <summary>
/// Port of the repository of the videogames of the catalog (AD-1, AD-22).
///
/// <para>
/// The port is declared in the Application layer and it is implemented by the adapter that
/// writes in the SQLite file of the server. The mutation of what is persisted happens only
/// there, through this port, and the use cases never reach the adapter (AD-1).
/// </para>
/// <para>
/// The videogame is owned by the server and it lives in <c>catalog.sqlite</c>. No module
/// of the server reads the table of another module (AR-13, AD-22).
/// </para>
/// </summary>
public interface IGameRepository
{
    /// <summary>
    /// Registers a videogame in the catalog. The identifier of the videogame is the one of
    /// the registration, and it is the key of the row.
    /// </summary>
    /// <param name="game">Registration of the videogame.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the videogame has been persisted.</returns>
    Task AddAsync(GameRegistration game, CancellationToken cancellationToken);

    /// <summary>
    /// Reads a videogame of the catalog by its identifier.
    /// </summary>
    /// <param name="gameId">Identifier of the videogame.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>
    /// The registration of the videogame, or <c>null</c> when the catalog does not hold a
    /// videogame with that identifier.
    /// </returns>
    Task<GameRegistration?> FindAsync(string gameId, CancellationToken cancellationToken);

    /// <summary>
    /// Reads every videogame of the catalog. The reading is what fills the list of the
    /// section of the catalog of the organizer, and it is not a route of the API: the
    /// routes that publish the catalog belong to the stories that follow (AD-1, AD-5).
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The registrations of the videogames of the catalog.</returns>
    Task<IReadOnlyList<GameRegistration>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the metadata of a videogame of the catalog. The identifier of the
    /// videogame is never changed (AD-34).
    /// </summary>
    /// <param name="game">Registration of the videogame with the new metadata.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>
    /// A task that completes when the videogame has been persisted, or when the catalog
    /// does not hold a videogame with that identifier.
    /// </returns>
    Task<bool> UpdateAsync(GameRegistration game, CancellationToken cancellationToken);
}
