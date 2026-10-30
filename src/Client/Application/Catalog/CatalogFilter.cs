using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Client.Application.Catalog;

/// <summary>
/// Filter of the catalog of the client over the snapshot that was already synchronized
/// (AC1, AC2, AC5, FR-2, AD-5, AD-26).
///
/// <para>
/// The filter is PURE and it lives in the Application layer: it does NOT know the
/// framework of the interface and it does NOT talk to the server. The filtering is LOCAL
/// over the snapshot that the client already holds, so the search NEVER goes back to the
/// server and the catalog of the event keeps being the single source of truth (AD-5,
/// AD-26).
/// </para>
/// <para>
/// The filter applies the criteria in a fixed order: the name matches as a case
/// insensitive substring of <see cref="GameDescriptor.Name"/>, the category intersects
/// with the categories of the game, and the result is ordered by name with a stable
/// tiebreak on the identifier of the game, so the order of the grid NEVER changes
/// between two renderings of the same snapshot (AC1, AC5).
/// </para>
/// <para>
/// The pages have a FIXED size, and the pagination is EXPLICIT: the catalog never scrolls
/// infinitely, so the player can always come back to the same position (AC5, UX-DR-27).
/// </para>
/// </summary>
public static class CatalogFilter
{
    /// <summary>
    /// Number of videogames that a page of the grid holds (AC5, UX-DR-27).
    ///
    /// <para>
    /// The size of the page is fixed and it does NOT depend on the size of the window: the
    /// pagination is explicit and the player can go back to the same page (AC5, UX-DR-27).
    /// </para>
    /// </summary>
    public const int PageSize = 12;

    /// <summary>
    /// Filters the videogames of the snapshot by name and by category (AC1, AC2).
    ///
    /// <para>
    /// A name that is empty or blank does NOT filter: every videogame matches it. A
    /// category that is <c>null</c> or empty does NOT filter either: the category filter
    /// is only active when the player picks one (AC2).
    /// </para>
    /// </summary>
    /// <param name="games">Videogames of the snapshot that the client holds.</param>
    /// <param name="searchText">Text that the player typed, or <c>null</c>.</param>
    /// <param name="category">Category that the player picked, or <c>null</c>.</param>
    /// <returns>The videogames that match both criteria, ordered by name.</returns>
    public static IReadOnlyList<GameDescriptor> Apply(
        IReadOnlyList<GameDescriptor> games,
        string? searchText,
        string? category)
    {
        ArgumentNullException.ThrowIfNull(games);

        var name = searchText?.Trim() ?? string.Empty;

        var matches = games
            .Where(game => MatchesName(game, name) && MatchesCategory(game, category))
            .OrderBy(game => game.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(game => game.GameId, StringComparer.Ordinal)
            .ToArray();

        return matches;
    }

    /// <summary>
    /// Reads the distinct categories of the snapshot, ordered by name (AC2).
    ///
    /// <para>
    /// The categories of the snapshot are the ONLY source of the filter: the client does
    /// NOT invent a list of its own and does NOT ask the server for one (AC2).
    /// </para>
    /// </summary>
    /// <param name="games">Videogames of the snapshot that the client holds.</param>
    /// <returns>The distinct categories, ordered by name.</returns>
    public static IReadOnlyList<string> Categories(IReadOnlyList<GameDescriptor> games)
    {
        ArgumentNullException.ThrowIfNull(games);

        return games
            .SelectMany(game => game.Categories)
            .Where(category => !string.IsNullOrWhiteSpace(category))
            .Select(category => category.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(category => category, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Reads the page of the filtered videogames (AC5, UX-DR-27).
    ///
    /// <para>
    /// The page is one-based. A page that is beyond the last one is CLAMPED to the last
    /// one, so a filter that narrows the results NEVER leaves the player on an empty page
    /// (AC3, AC5).
    /// </para>
    /// </summary>
    /// <param name="games">Videogames that the filter produced.</param>
    /// <param name="page">Number of the page, one-based.</param>
    /// <returns>The videogames of the page.</returns>
    public static IReadOnlyList<GameDescriptor> Page(
        IReadOnlyList<GameDescriptor> games,
        int page)
    {
        ArgumentNullException.ThrowIfNull(games);

        var pageCount = PageCount(games.Count);

        if (pageCount == 0)
        {
            return [];
        }

        var current = Math.Clamp(page, 1, pageCount);

        return games
            .Skip((current - 1) * PageSize)
            .Take(PageSize)
            .ToArray();
    }

    /// <summary>
    /// Counts the pages that the filtered videogames fill (AC5, UX-DR-27).
    /// </summary>
    /// <param name="count">Number of videogames that the filter produced.</param>
    /// <returns>The number of pages, which is zero when there is no videogame.</returns>
    public static int PageCount(int count) =>
        count <= 0 ? 0 : (count + PageSize - 1) / PageSize;

    /// <summary>
    /// Tells whether a videogame matches the name that the player typed (AC1).
    /// </summary>
    /// <param name="game">Videogame that the filter examines.</param>
    /// <param name="searchText">Text that the player typed.</param>
    /// <returns>Whether the name of the videogame matches.</returns>
    private static bool MatchesName(GameDescriptor game, string searchText)
    {
        if (searchText.Length == 0)
        {
            return true;
        }

        return game.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Tells whether a videogame matches the category that the player picked (AC2).
    /// </summary>
    /// <param name="game">Videogame that the filter examines.</param>
    /// <param name="category">Category that the player picked, or <c>null</c>.</param>
    /// <returns>Whether the videogame belongs to the category.</returns>
    private static bool MatchesCategory(GameDescriptor game, string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return true;
        }

        return game.Categories.Any(
            candidate => string.Equals(candidate.Trim(), category.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
