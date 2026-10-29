using LANPartyHub.Server.Presentation.Formatting;
using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Server.Presentation.Catalog;

/// <summary>
/// View model of the section of the catalog of the event (AD-1, AC1).
///
/// <para>
/// The view model holds the list of the videogames that the catalog publishes and it
/// offers the alta and the modification of them. It holds no rule of the domain: the
/// validation of the metadata belongs to the domain (AD-1).
/// </para>
/// </summary>
public sealed class CatalogViewModel
{
    private readonly List<GameDescriptor> _games = [];

    /// <summary>Creates the view model of the section of the catalog.</summary>
    public CatalogViewModel()
    {
    }

    /// <summary>
    /// Creates the view model of the section of the catalog with the videogames that the
    /// catalog publishes.
    /// </summary>
    /// <param name="games">Videojames of the catalog.</param>
    public CatalogViewModel(IEnumerable<GameDescriptor> games)
    {
        ArgumentNullException.ThrowIfNull(games);

        _games.AddRange(games);
    }

    /// <summary>
    /// The videogames of the catalog, as the list shows them. The order is the one of the
    /// catalog, and two videogames with the same name are told apart by their identifier
    /// and their version (FR-1).
    /// </summary>
    public IReadOnlyList<GameDescriptor> Games => _games;

    /// <summary>
    /// The videogames of the catalog with the size already derived for the interface
    /// (AD-7).
    /// </summary>
    public IReadOnlyList<CatalogGameRow> Rows =>
        _games
            .Select(game => new CatalogGameRow(
                game.GameId,
                game.Name,
                game.Version,
                SizeFormatter.FormatValue(game.SizeBytes),
                SizeFormatter.Unit,
                game.MinPlayers,
                game.MaxPlayers))
            .ToArray();

    /// <summary>
    /// The number of videogames that the catalog publishes.
    /// </summary>
    public int Count => _games.Count;

    /// <summary>
    /// Replaces the videogames of the catalog with the ones that the section shows.
    /// </summary>
    /// <param name="games">Videojames of the catalog.</param>
    public void Replace(IEnumerable<GameDescriptor> games)
    {
        ArgumentNullException.ThrowIfNull(games);

        _games.Clear();
        _games.AddRange(games);
    }

    /// <summary>
    /// The view model of the form of the alta de un videogame.
    /// </summary>
    /// <returns>The view model of the form, empty.</returns>
    public GameFormViewModel CreateFormViewModel() => new();

    /// <summary>
    /// The view model of the form of la modificación of a videogame that is already in the
    /// catalog.
    /// </summary>
    /// <param name="game">Contract of the videogame to modify.</param>
    /// <returns>The view model of the form, filled with the metadata of the videogame.</returns>
    public GameFormViewModel CreateFormViewModel(GameDescriptor game)
    {
        ArgumentNullException.ThrowIfNull(game);

        return new GameFormViewModel(game);
    }
}

/// <summary>
/// A videogame of the catalog as the list of the section shows it (AC1).
///
/// <para>
/// The row carries the values that the list shows, and the size already derived: the
/// numeric value in decimal gigabytes and the unit apart, so the view can put each one
/// in its own token (AD-7, AC4).
/// </para>
/// </summary>
/// <param name="GameId">Opaque UUID v7 identifier of the videogame.</param>
/// <param name="Name">Human readable name.</param>
/// <param name="Version">Version published in the catalog, as an opaque string.</param>
/// <param name="SizeValue">Numeric value of the size, in decimal gigabytes.</param>
/// <param name="SizeUnit">Unit of the size.</param>
/// <param name="MinPlayers">Minimum number of players.</param>
/// <param name="MaxPlayers">Maximum number of players.</param>
public sealed record CatalogGameRow(
    string GameId,
    string Name,
    string Version,
    string SizeValue,
    string SizeUnit,
    int MinPlayers,
    int MaxPlayers);
