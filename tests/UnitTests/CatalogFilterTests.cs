using LANPartyHub.Client.Application.Catalog;
using LANPartyHub.Shared.Contracts;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The filter of the catalog of the client (AC1, AC2, AC5, FR-2).
///
/// <para>
/// The filter is the PURE rule of the search and of the filter by category: it works over
/// the snapshot that the client already synchronized and it NEVER goes back to the server
/// (AD-5, AD-26). The tests fix the name matching, the intersection with the category,
/// the order, the pagination and the list of the categories of the snapshot.
/// </para>
/// </summary>
public sealed class CatalogFilterTests
{
    [Fact]
    public void An_empty_search_matches_every_videogame()
    {
        var games = new[] { CreateGame("Counter-Strike 2"), CreateGame("Minecraft") };

        var result = CatalogFilter.Apply(games, string.Empty, null);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void A_blank_search_matches_every_videogame()
    {
        var games = new[] { CreateGame("Counter-Strike 2"), CreateGame("Minecraft") };

        var result = CatalogFilter.Apply(games, "   ", null);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void A_null_search_matches_every_videogame()
    {
        var games = new[] { CreateGame("Counter-Strike 2"), CreateGame("Minecraft") };

        var result = CatalogFilter.Apply(games, null, null);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void The_search_matches_a_substring_of_the_name()
    {
        var games = new[] { CreateGame("Counter-Strike 2"), CreateGame("Minecraft") };

        var result = CatalogFilter.Apply(games, "strike", null);

        var game = Assert.Single(result);

        Assert.Equal("Counter-Strike 2", game.Name);
    }

    [Fact]
    public void The_search_does_not_distinguish_between_uppercase_and_lowercase()
    {
        var games = new[] { CreateGame("Counter-Strike 2") };

        var result = CatalogFilter.Apply(games, "COUNTER-strike", null);

        Assert.Single(result);
    }

    [Fact]
    public void A_search_that_matches_nothing_returns_no_videogame()
    {
        var games = new[] { CreateGame("Counter-Strike 2") };

        var result = CatalogFilter.Apply(games, "quake", null);

        Assert.Empty(result);
    }

    [Fact]
    public void The_filter_by_category_keeps_only_the_videogames_of_that_category()
    {
        var games = new[]
        {
            CreateGame("Counter-Strike 2", "Shooter"),
            CreateGame("Minecraft", "Sandbox"),
        };

        var result = CatalogFilter.Apply(games, null, "Shooter");

        var game = Assert.Single(result);

        Assert.Equal("Counter-Strike 2", game.Name);
    }

    [Fact]
    public void The_filter_by_category_does_not_distinguish_between_uppercase_and_lowercase()
    {
        var games = new[] { CreateGame("Counter-Strike 2", "Shooter") };

        var result = CatalogFilter.Apply(games, null, "shooter");

        Assert.Single(result);
    }

    [Fact]
    public void A_category_that_no_videogame_has_returns_no_videogame()
    {
        var games = new[] { CreateGame("Counter-Strike 2", "Shooter") };

        var result = CatalogFilter.Apply(games, null, "Deporte");

        Assert.Empty(result);
    }

    [Fact]
    public void The_name_and_the_category_intersect()
    {
        var games = new[]
        {
            CreateGame("Counter-Strike 2", "Shooter"),
            CreateGame("Counter-Strike: Legacy", "Shooter"),
            CreateGame("Counter-Strike: Field", "Tactico"),
            CreateGame("Minecraft", "Shooter"),
        };

        var result = CatalogFilter.Apply(games, "counter-strike", "Shooter");

        Assert.Equal(2, result.Count);
        Assert.All(result, game => Assert.Equal("Shooter", game.Categories[0]));
        Assert.DoesNotContain(result, game => game.Name == "Minecraft");
    }

    [Fact]
    public void The_result_is_ordered_by_name()
    {
        var games = new[]
        {
            CreateGame("Zelda"),
            CreateGame("Minecraft"),
            CreateGame("Counter-Strike 2"),
        };

        var result = CatalogFilter.Apply(games, null, null);

        Assert.Equal(["Counter-Strike 2", "Minecraft", "Zelda"], result.Select(game => game.Name));
    }

    [Fact]
    public void The_order_of_the_result_does_not_depend_on_the_order_of_the_snapshot()
    {
        var first = new[]
        {
            CreateGame("Zelda", "a", []),
            CreateGame("Minecraft", "b", []),
            CreateGame("Counter-Strike 2", "c", []),
        };

        var second = new[]
        {
            CreateGame("Counter-Strike 2", "c", []),
            CreateGame("Zelda", "a", []),
            CreateGame("Minecraft", "b", []),
        };

        var one = CatalogFilter.Apply(first, null, null);
        var other = CatalogFilter.Apply(second, null, null);

        Assert.Equal(one.Select(game => game.GameId), other.Select(game => game.GameId));
    }

    [Fact]
    public void The_order_of_the_result_is_stable_for_two_videogames_with_the_same_name()
    {
        var games = new[]
        {
            CreateGame("Counter-Strike 2", "b", []),
            CreateGame("Counter-Strike 2", "a", []),
        };

        var result = CatalogFilter.Apply(games, null, null);

        Assert.Equal(["a", "b"], result.Select(game => game.GameId));
    }

    [Fact]
    public void The_pages_have_a_fixed_size()
    {
        var games = Enumerable
            .Range(0, 25)
            .Select(index => CreateGame($"Juego {index:00}"))
            .ToArray();

        var result = CatalogFilter.Apply(games, null, null);

        Assert.Equal(3, CatalogFilter.PageCount(result.Count));
        Assert.Equal(CatalogFilter.PageSize, CatalogFilter.Page(result, 1).Count);
        Assert.Equal(CatalogFilter.PageSize, CatalogFilter.Page(result, 2).Count);
        Assert.Single(CatalogFilter.Page(result, 3));
    }

    [Fact]
    public void The_last_page_holds_the_videogames_that_are_left()
    {
        var games = Enumerable
            .Range(0, 13)
            .Select(index => CreateGame($"Juego {index:00}"))
            .ToArray();

        var filtered = CatalogFilter.Apply(games, null, null);

        var page = CatalogFilter.Page(filtered, 2);

        Assert.Single(page);
        Assert.Equal("Juego 12", page[0].Name);
    }

    [Fact]
    public void A_page_that_is_beyond_the_last_one_is_clamped_to_the_last_one()
    {
        var games = Enumerable
            .Range(0, 13)
            .Select(index => CreateGame($"Juego {index:00}"))
            .ToArray();

        var filtered = CatalogFilter.Apply(games, null, null);

        var page = CatalogFilter.Page(filtered, 99);

        Assert.Single(page);
        Assert.Equal("Juego 12", page[0].Name);
    }

    [Fact]
    public void A_page_that_is_below_the_first_one_is_clamped_to_the_first_one()
    {
        var games = Enumerable
            .Range(0, 3)
            .Select(index => CreateGame($"Juego {index:00}"))
            .ToArray();

        var filtered = CatalogFilter.Apply(games, null, null);

        var page = CatalogFilter.Page(filtered, 0);

        Assert.Equal("Juego 00", page[0].Name);
    }

    [Fact]
    public void A_filter_that_returns_nothing_has_no_page()
    {
        var games = new[] { CreateGame("Counter-Strike 2") };

        var filtered = CatalogFilter.Apply(games, "quake", null);

        Assert.Equal(0, CatalogFilter.PageCount(filtered.Count));
        Assert.Empty(CatalogFilter.Page(filtered, 1));
    }

    [Fact]
    public void The_categories_are_the_distinct_ones_of_the_snapshot()
    {
        var games = new[]
        {
            CreateGame("Counter-Strike 2", "Shooter", "Multijugador"),
            CreateGame("Minecraft", "Sandbox", "Multijugador"),
        };

        var categories = CatalogFilter.Categories(games);

        Assert.Equal(["Multijugador", "Sandbox", "Shooter"], categories);
    }

    [Fact]
    public void A_category_that_repeats_does_not_appear_twice()
    {
        var games = new[]
        {
            CreateGame("Counter-Strike 2", "Shooter"),
            CreateGame("Minecraft", "Shooter"),
        };

        var categories = CatalogFilter.Categories(games);

        Assert.Equal(["Shooter"], categories);
    }

    [Fact]
    public void The_categories_of_a_videogame_that_has_none_are_empty()
    {
        var games = new[] { CreateGame("Counter-Strike 2") };

        var categories = CatalogFilter.Categories(games);

        Assert.Empty(categories);
    }

    [Fact]
    public void The_filter_does_not_modify_the_snapshot_that_it_receives()
    {
        var games = new[] { CreateGame("Zelda"), CreateGame("Minecraft") };

        CatalogFilter.Apply(games, null, null);

        Assert.Equal(["Zelda", "Minecraft"], games.Select(game => game.Name));
    }

    /// <summary>
    /// Builds the contract of a videogame for the filter.
    /// </summary>
    /// <param name="name">Human readable name.</param>
    /// <param name="categories">Categories of the videogame.</param>
    /// <param name="id">Identifier of the videogame.</param>
    /// <returns>The contract of the videogame.</returns>
    private static GameDescriptor CreateGame(
        string name,
        params string[] categories) =>
        CreateGame(name, Guid.NewGuid().ToString("N"), categories);

    private static GameDescriptor CreateGame(string name, string id, string[] categories) => new(
        id,
        name,
        "Descripción del videojuego.",
        categories,
        [],
        "cover.png",
        [],
        "1.0.0",
        1_000_000,
        1,
        4,
        "Windows",
        "Servidor",
        "Portable",
        new InstallationManifest.LaunchSettings("game.exe", [], "game"),
        [],
        "Propietaria",
        "Solo para el evento");
}
