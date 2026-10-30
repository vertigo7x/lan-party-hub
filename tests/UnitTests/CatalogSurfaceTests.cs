using Avalonia.Controls;
using Avalonia.VisualTree;
using LANPartyHub.Client.Application.Catalog;
using LANPartyHub.Client.Presentation.Catalog;
using LANPartyHub.Shared.Contracts;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The surface of the catalog of the player (AC1, AC2, AC3, AC4, AC5, AC6, UX-DR-9,
/// UX-DR-27, UX-DR-28).
///
/// <para>
/// The tests fix the behaviour of the surface: the search filters AS THE PLAYER TYPES with
/// a delay of 200 ms and without a «buscar» button, the category intersects with the
/// search, the two empty states carry their EXACT text, «Quitar filtros» reverses the
/// search without leaving the surface, and the pagination is explicit (AC1, AC2, AC3,
/// AC4, AC5).
/// </para>
/// </summary>
public sealed class CatalogSurfaceTests
{
    [Fact]
    public void The_search_does_not_filter_before_the_delay_of_200_ms_elapses()
    {
        var clock = new ManualTimeProvider();
        var viewModel = new CatalogSurfaceViewModel(clock);

        viewModel.ApplySnapshot(CreateSnapshot(), synchronized: true);

        viewModel.SearchText = "min";

        // El retardo de 200 ms es lo que evita que la rejilla se redibuje en cada tecla
        // (AC1, UX-DR-9).
        Assert.Equal(2, viewModel.Games.Count);

        clock.Advance(TimeSpan.FromMilliseconds(199));

        Assert.Equal(2, viewModel.Games.Count);
    }

    [Fact]
    public void The_search_filters_when_the_delay_of_200_ms_elapses()
    {
        var clock = new ManualTimeProvider();
        var viewModel = new CatalogSurfaceViewModel(clock);

        viewModel.ApplySnapshot(CreateSnapshot(), synchronized: true);

        viewModel.SearchText = "min";

        clock.Advance(TimeSpan.FromMilliseconds(200));

        var game = Assert.Single(viewModel.Games);

        Assert.Equal("Minecraft", game.Name);
    }

    [Fact]
    public void Each_keystroke_replaces_the_pending_one()
    {
        var clock = new ManualTimeProvider();
        var viewModel = new CatalogSurfaceViewModel(clock);

        viewModel.ApplySnapshot(CreateSnapshot(), synchronized: true);

        viewModel.SearchText = "m";
        clock.Advance(TimeSpan.FromMilliseconds(150));

        viewModel.SearchText = "min";
        clock.Advance(TimeSpan.FromMilliseconds(150));

        // La búsqueda todavía NO se aplicó: la segunda pulsación sustituyó al primer
        // retardo (AC1, UX-DR-9).
        Assert.Equal(2, viewModel.Games.Count);

        clock.Advance(TimeSpan.FromMilliseconds(50));

        Assert.Single(viewModel.Games);
    }

    [Fact]
    public void The_surface_has_no_search_button()
    {
        // El filtro se aplica al escribir, con 200 ms de retardo, y SIN botón «buscar»
        // (AC1, UX-DR-9).
        var surface = new CatalogSurface(new CatalogSurfaceViewModel());

        var buttons = FindDescendants(surface)
            .OfType<Button>()
            .Select(button => button.Content as string)
            .ToArray();

        Assert.DoesNotContain("Buscar", buttons);
        Assert.DoesNotContain("buscar", buttons);
    }

    [Fact]
    public void The_cover_of_a_card_keeps_the_proportion_of_three_by_two()
    {
        // La carátula es 3:2 en el diseño, y el esqueleto tiene la MISMA proporción que la
        // tarjeta real, para que la rejilla NO salte cuando llegan las tarjetas (AC6).
        Assert.Equal(3d / 2d, CatalogCardSkeleton.CoverRatio);
        Assert.Equal(
            CatalogCardSkeleton.CoverWidth / CatalogCardSkeleton.CoverRatio,
            CatalogCardSkeleton.CoverHeight);
    }

    [Fact]
    public void The_grid_has_three_columns_at_1280_pixels_and_up_to_six()
    {
        // El diseño fija un mínimo de 3 columnas a 1280 px y hasta 6 (DESIGN).
        Assert.Equal(3, CatalogGrid.ColumnsFor(1280));
        Assert.Equal(6, CatalogGrid.ColumnsFor(1920));
        Assert.True(CatalogGrid.ColumnsFor(2560) <= 6);
    }

    [Fact]
    public void The_grid_goes_to_two_columns_when_three_do_not_fit()
    {
        Assert.Equal(2, CatalogGrid.ColumnsFor(1024));
        Assert.Equal(2, CatalogGrid.ColumnsFor(720));
    }

    [Fact]
    public void The_grid_goes_to_a_list_of_one_column_below_two()
    {
        Assert.Equal(1, CatalogGrid.ColumnsFor(640));
        Assert.Equal(1, CatalogGrid.ColumnsFor(320));
    }

    [Fact]
    public void The_filter_by_category_keeps_only_the_videogames_of_that_category()
    {
        var viewModel = new CatalogSurfaceViewModel();

        viewModel.ApplySnapshot(CreateSnapshot(), synchronized: true);

        viewModel.SelectedCategory = "Sandbox";

        var game = Assert.Single(viewModel.Games);

        Assert.Equal("Minecraft", game.Name);
    }

    [Fact]
    public void The_categories_of_the_surface_are_the_ones_of_the_snapshot()
    {
        var viewModel = new CatalogSurfaceViewModel();

        viewModel.ApplySnapshot(CreateSnapshot(), synchronized: true);

        Assert.Equal(["Sandbox", "Shooter"], viewModel.Categories);
    }

    [Fact]
    public void The_category_and_the_search_intersect()
    {
        var clock = new ManualTimeProvider();
        var viewModel = new CatalogSurfaceViewModel(clock);

        viewModel.ApplySnapshot(CreateSnapshot(), synchronized: true);

        viewModel.SelectedCategory = "Shooter";
        viewModel.SearchText = "counter";

        clock.Advance(TimeSpan.FromMilliseconds(200));

        var game = Assert.Single(viewModel.Games);

        Assert.Equal("Counter-Strike 2", game.Name);
    }

    [Fact]
    public void A_search_without_results_shows_the_exact_text_with_the_query()
    {
        var clock = new ManualTimeProvider();
        var viewModel = new CatalogSurfaceViewModel(clock);

        viewModel.ApplySnapshot(CreateSnapshot(), synchronized: true);

        viewModel.SearchText = "quake";

        clock.Advance(TimeSpan.FromMilliseconds(200));

        Assert.True(viewModel.ShowsNoResults);
        Assert.Equal("Sin resultados para «quake».", viewModel.NoResultsMessage);
        Assert.Equal("Quitar filtros", viewModel.ClearFiltersActionText);
    }

    [Fact]
    public void A_search_without_results_offers_the_action_of_clearing_the_filters()
    {
        var clock = new ManualTimeProvider();
        var viewModel = new CatalogSurfaceViewModel(clock);

        viewModel.ApplySnapshot(CreateSnapshot(), synchronized: true);

        viewModel.SearchText = "quake";

        clock.Advance(TimeSpan.FromMilliseconds(200));

        Assert.True(viewModel.HasFilters);
    }

    [Fact]
    public void The_clearing_of_the_filters_reverses_the_search_without_leaving_the_surface()
    {
        var clock = new ManualTimeProvider();
        var viewModel = new CatalogSurfaceViewModel(clock);

        viewModel.ApplySnapshot(CreateSnapshot(), synchronized: true);

        viewModel.SelectedCategory = "Shooter";
        viewModel.SearchText = "quake";

        clock.Advance(TimeSpan.FromMilliseconds(200));

        viewModel.ClearFilters();

        Assert.Equal(string.Empty, viewModel.SearchText);
        Assert.Null(viewModel.SelectedCategory);
        Assert.False(viewModel.HasFilters);
        Assert.False(viewModel.ShowsNoResults);
        Assert.Equal(2, viewModel.Games.Count);
    }

    [Fact]
    public void An_empty_catalog_shows_the_exact_text_and_the_action_of_synchronizing()
    {
        var viewModel = new CatalogSurfaceViewModel();

        viewModel.ApplySnapshot(CreateSnapshot(empty: true), synchronized: true);

        Assert.True(viewModel.IsCatalogEmpty);
        Assert.Equal("El catálogo está vacío.", viewModel.EmptyCatalogMessage);
        Assert.Equal("Sincronizar ahora", viewModel.EmptyCatalogActionText);
    }

    [Fact]
    public void An_empty_catalog_shows_the_action_of_connecting_when_the_server_is_not_connected()
    {
        var viewModel = new CatalogSurfaceViewModel();

        viewModel.ApplySnapshot(CreateSnapshot(empty: true), synchronized: false);

        Assert.True(viewModel.IsCatalogEmpty);
        Assert.Equal("El catálogo está vacío.", viewModel.EmptyCatalogMessage);
        Assert.Equal("Conectar al servidor", viewModel.EmptyCatalogActionText);
    }

    [Fact]
    public void A_catalog_with_videogames_is_not_empty()
    {
        var viewModel = new CatalogSurfaceViewModel();

        viewModel.ApplySnapshot(CreateSnapshot(), synchronized: true);

        Assert.False(viewModel.IsCatalogEmpty);
    }

    [Fact]
    public void The_grid_shows_the_skeletons_while_there_are_no_cards()
    {
        var viewModel = new CatalogSurfaceViewModel();

        // Todavía NO hay snapshot: la rejilla muestra los esqueletos de las tarjetas con
        // la proporción real, y no un texto «cargando» giratorio (AC6, UX-DR-28).
        Assert.True(viewModel.ShowsSkeletons);
    }

    [Fact]
    public void The_grid_does_not_show_the_skeletons_once_the_cards_are_there()
    {
        var viewModel = new CatalogSurfaceViewModel();

        viewModel.ApplySnapshot(CreateSnapshot(), synchronized: true);

        Assert.False(viewModel.ShowsSkeletons);
    }

    [Fact]
    public void The_pagination_is_explicit_and_the_pages_have_a_fixed_size()
    {
        var viewModel = new CatalogSurfaceViewModel();

        viewModel.ApplySnapshot(CreateSnapshot(many: true), synchronized: true);

        Assert.Equal(2, viewModel.PageCount);
        Assert.Equal(1, viewModel.CurrentPage);
        Assert.Equal(CatalogFilter.PageSize, viewModel.Games.Count);
        Assert.True(viewModel.CanGoForward);
        Assert.False(viewModel.CanGoBack);
    }

    [Fact]
    public void The_pagination_moves_to_the_next_page_and_to_the_previous_one()
    {
        var viewModel = new CatalogSurfaceViewModel();

        viewModel.ApplySnapshot(CreateSnapshot(many: true), synchronized: true);

        viewModel.GoForward();

        Assert.Equal(2, viewModel.CurrentPage);
        Assert.True(viewModel.CanGoBack);
        Assert.False(viewModel.CanGoForward);

        viewModel.GoBack();

        Assert.Equal(1, viewModel.CurrentPage);
    }

    [Fact]
    public void The_pagination_does_not_move_beyond_the_first_page_or_the_last_one()
    {
        var viewModel = new CatalogSurfaceViewModel();

        viewModel.ApplySnapshot(CreateSnapshot(many: true), synchronized: true);

        viewModel.GoBack();

        Assert.Equal(1, viewModel.CurrentPage);

        viewModel.GoForward();
        viewModel.GoForward();

        Assert.Equal(2, viewModel.CurrentPage);
    }

    [Fact]
    public void A_filter_that_narrows_the_result_returns_the_pagination_to_the_first_page()
    {
        var clock = new ManualTimeProvider();
        var viewModel = new CatalogSurfaceViewModel(clock);

        viewModel.ApplySnapshot(CreateSnapshot(many: true), synchronized: true);

        viewModel.GoForward();

        Assert.Equal(2, viewModel.CurrentPage);

        viewModel.SearchText = "juego 01";

        clock.Advance(TimeSpan.FromMilliseconds(200));

        // La búsqueda vuelve a la primera página: el jugador no aterriza en una página que
        // ya no existe (AC1, AC5).
        Assert.Equal(1, viewModel.CurrentPage);
    }

    [Fact]
    public void The_category_that_the_snapshot_lost_is_not_a_filter_anymore()
    {
        var viewModel = new CatalogSurfaceViewModel();

        viewModel.ApplySnapshot(CreateSnapshot(), synchronized: true);

        viewModel.SelectedCategory = "Shooter";

        Assert.Equal("Shooter", viewModel.SelectedCategory);

        // El snapshot nuevo ya no tiene la categoría: el filtro deja de ser válido en vez
        // de mostrar un filtro que no puede matchear nada (AC2).
        viewModel.ApplySnapshot(
            new CatalogSnapshot(2, [CreateGame("Minecraft", "Sandbox")], []),
            synchronized: true);

        Assert.Null(viewModel.SelectedCategory);
        Assert.Single(viewModel.Games);
    }

    [Fact]
    public void The_surface_shows_the_snapshot_that_the_client_applied()
    {
        var viewModel = new CatalogSurfaceViewModel();

        viewModel.ApplySnapshot(CreateSnapshot(), synchronized: true);

        Assert.Equal(2, viewModel.Games.Count);
        Assert.Equal(["Sandbox", "Shooter"], viewModel.Categories);
    }

    [Fact]
    public void The_surface_shows_the_last_snapshot_when_the_server_is_down()
    {
        var viewModel = new CatalogSurfaceViewModel();

        // El cliente conserva el último catálogo aplicado: la búsqueda sigue funcionando
        // sobre él (AC3, UX-DR-32).
        viewModel.ApplySnapshot(CreateSnapshot(), synchronized: false);

        Assert.Equal(2, viewModel.Games.Count);
        Assert.False(viewModel.IsServerConnected);
    }

    /// <summary>
    /// Builds a snapshot for the surface.
    /// </summary>
    /// <param name="empty">Whether the snapshot holds no videogame.</param>
    /// <param name="many">Whether the snapshot holds more videogames than one page.</param>
    /// <returns>The snapshot of the catalog.</returns>
    private static CatalogSnapshot CreateSnapshot(bool empty = false, bool many = false)
    {
        if (empty)
        {
            return new CatalogSnapshot(1, [], []);
        }

        if (many)
        {
            var games = Enumerable
                .Range(0, CatalogFilter.PageSize + 1)
                .Select(index => CreateGame($"Juego {index:00}", "Shooter"))
                .ToArray();

            return new CatalogSnapshot(1, games, []);
        }

        return new CatalogSnapshot(
            1,
            [CreateGame("Counter-Strike 2", "Shooter"), CreateGame("Minecraft", "Sandbox")],
            []);
    }

    /// <summary>
    /// Builds the contract of a videogame for the surface.
    /// </summary>
    /// <param name="name">Human readable name.</param>
    /// <param name="categories">Categories of the videogame.</param>
    /// <returns>The contract of the videogame.</returns>
    private static GameDescriptor CreateGame(string name, params string[] categories) => new(
        Guid.NewGuid().ToString("N"),
        name,
        "Descripción del videojuego.",
        categories,
        [],        "cover.png",
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

    /// <summary>
    /// Finds the controls of a surface, so the test can check that the surface does NOT
    /// have a «buscar» button (AC1, UX-DR-9).
    /// </summary>
    /// <param name="root">Root of the tree of controls.</param>
    /// <returns>The controls of the tree.</returns>
    private static IEnumerable<Control> FindDescendants(Control root)
    {
        foreach (var child in root.GetVisualChildren().OfType<Control>())
        {
            yield return child;

            foreach (var descendant in FindDescendants(child))
            {
                yield return descendant;
            }
        }
    }

    /// <summary>
    /// Clock that only moves when the test moves it, so the delay of 200 ms of the search
    /// can be exercised without waiting (AC1, UX-DR-9).
    /// </summary>
    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;
        private DateTimeOffset _wallClock = DateTimeOffset.UnixEpoch;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _timestamp;

        public override DateTimeOffset GetUtcNow() => _wallClock;

        /// <summary>
        /// Moves the clock forward and fires the timers that the move makes due.
        /// </summary>
        /// <param name="delta">Time that the clock advances.</param>
            public void Advance(TimeSpan delta)
            {
                _timestamp += delta.Ticks;
                _wallClock += delta;

                foreach (var timer in _timers)
                {
                    timer.OnTime(delta);
                }
            }

            private readonly List<ManualTimer> _timers = [];

            public override ITimer CreateTimer(
                TimerCallback callback,
                object? state,
                TimeSpan dueTime,
                TimeSpan period)
            {
                var timer = new ManualTimer(callback, state, dueTime);

                _timers.Add(timer);

                return timer;
            }

            /// <summary>
            /// Timer of the manual clock, which only fires when the test moves the clock.
            /// </summary>
            private sealed class ManualTimer : ITimer
            {
                private readonly TimerCallback _callback;
                private readonly object? _state;
                private TimeSpan _remaining;
                private bool _disposed;

                public ManualTimer(TimerCallback callback, object? state, TimeSpan dueTime)
                {
                    _callback = callback;
                    _state = state;
                    _remaining = dueTime;
                }

                public bool Change(TimeSpan dueTime, TimeSpan period) => true;

                public void Dispose() => _disposed = true;

                public ValueTask DisposeAsync()
                {
                    _disposed = true;

                    return ValueTask.CompletedTask;
                }

                /// <summary>
                /// Advances the timer and fires the callback when the time is due.
                /// </summary>
                /// <param name="delta">Time that the clock advances.</param>
                public void OnTime(TimeSpan delta)
                {
                    // Un timer que la vista descarta NO vuelve a dispararse: el retardo
                    // pendiente que sustituye a otro lo cancela (AC1, UX-DR-9).
                    if (_disposed)
                    {
                        return;
                    }

                    _remaining -= delta;

                    if (_remaining > TimeSpan.Zero)
                    {
                        return;
                    }

                    _remaining = TimeSpan.FromMilliseconds(-1);

                    _callback(_state);
                }
            }
    }
}
