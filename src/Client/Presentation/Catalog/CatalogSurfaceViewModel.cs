using LANPartyHub.Client.Application.Catalog;
using LANPartyHub.Client.Application.CatalogSync;
using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Client.Presentation.Catalog;

/// <summary>
/// View model of the surface of the catalog of the player (AC1, AC2, AC3, AC4, AC5, AC6,
/// FR-2, UX-DR-9, UX-DR-27, UX-DR-28).
///
/// <para>
/// The view model filters the snapshot that the client already synchronized: the search is
/// LOCAL and it NEVER goes back to the server (AD-5, AD-26). The rule of the filter lives
/// in the Application layer (<see cref="CatalogFilter"/>) and this view model only holds
/// the state of the surface and the microcopy of its empty states (AD-1).
/// </para>
/// <para>
/// The search filters AS THE PLAYER TYPES, with a delay of 200 ms and WITHOUT a «buscar»
/// button (AC1, UX-DR-9). The delay is measured with an injected clock, so the test can
/// exercise it without waiting and without a new package in the solution.
/// </para>
/// <para>
/// The pagination is EXPLICIT: the grid pages by fixed scrolls and the catalog never
/// scrolls infinitely, so the player can come back to the same position (AC5, UX-DR-27).
/// </para>
/// </summary>
public sealed class CatalogSurfaceViewModel
{
    /// <summary>
    /// Delay between the last keystroke and the application of the filter, in milliseconds
    /// (AC1, UX-DR-9).
    /// </summary>
    public const int SearchDelayMilliseconds = 200;

    /// <summary>
    /// Text that the surface shows when a search has no results (AC3, UX-DR-28).
    /// </summary>
    public const string NoResultsText = "Sin resultados";

    /// <summary>
    /// Text of the action that reverses the filters of a search without results
    /// (AC3, UX-DR-28).
    /// </summary>
    public const string ClearFiltersText = "Quitar filtros";

    /// <summary>
    /// Text that the surface shows when the catalog holds no videogame (AC4, UX-DR-28).
    /// </summary>
    public const string EmptyCatalogText = "El catálogo está vacío.";

    /// <summary>
    /// Text of the action that synchronizes the catalog when the server is connected
    /// (AC4, UX-DR-28).
    /// </summary>
    public const string SynchronizeNowText = "Sincronizar ahora";

    /// <summary>
    /// Text of the action that connects to the server when it is not connected
    /// (AC4, UX-DR-28).
    /// </summary>
    public const string ConnectToServerText = "Conectar al servidor";

    private readonly TimeProvider _timeProvider;
    private readonly List<GameDescriptor> _games = [];
    private readonly List<string> _categories = [];
    private readonly List<GameDescriptor> _filtered = [];

    private ITimer? _searchTimer;
    private string _searchText = string.Empty;
    private string? _selectedCategory;
    private int _currentPage = 1;
    private bool _synchronized = true;
    private bool _loading = true;

    /// <summary>
    /// Creates the view model of the surface of the catalog.
    /// </summary>
    /// <param name="timeProvider">
    /// Clock that measures the delay of the search. It defaults to the real one and it
    /// exists so that the delay of 200 ms can be exercised with a known clock.
    /// </param>
    public CatalogSurfaceViewModel(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Raised when the surface has to be rendered again, because the filters, the page or
    /// the state of the synchronization changed.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// Text that the player typed in the search field (AC1, UX-DR-9).
    ///
    /// <para>
    /// Writing it does NOT filter immediately: the filter is applied 200 ms after the last
    /// keystroke, and there is NO «buscar» button (AC1, UX-DR-9).
    /// </para>
    /// </summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (string.Equals(_searchText, value, StringComparison.Ordinal))
            {
                return;
            }

            _searchText = value ?? string.Empty;

            ScheduleSearch();
        }
    }

    /// <summary>
    /// Category that the player picked, or <c>null</c> when no category is filtered
    /// (AC2).
    /// </summary>
    public string? SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            var category = string.IsNullOrWhiteSpace(value) ? null : value;

            if (string.Equals(_selectedCategory, category, StringComparison.Ordinal))
            {
                return;
            }

            _selectedCategory = category;

            // La categoría cambia la CANCHA del resultado, así que la página vuelve a la
            // primera: el jugador no aterriza en una página que ya no existe (AC2, AC5).
            _currentPage = 1;

            Refilter();
        }
    }

    /// <summary>
    /// Categories that the player can pick, taken from the snapshot that the client holds
    /// (AC2).
    /// </summary>
    public IReadOnlyList<string> Categories => _categories;

    /// <summary>
    /// Videogames of the current page (AC5, UX-DR-27).
    /// </summary>
    public IReadOnlyList<GameDescriptor> Games { get; private set; } = [];

    /// <summary>
    /// Number of the page that the player is seeing, one-based (AC5, UX-DR-27).
    /// </summary>
    public int CurrentPage => _currentPage;

    /// <summary>
    /// Number of pages that the filtered videogames fill (AC5, UX-DR-27).
    /// </summary>
    public int PageCount { get; private set; }

    /// <summary>
    /// Whether the player can go to the previous page (AC5, UX-DR-27).
    /// </summary>
    public bool CanGoBack => _currentPage > 1;

    /// <summary>
    /// Whether the player can go to the next page (AC5, UX-DR-27).
    /// </summary>
    public bool CanGoForward => _currentPage < PageCount;

    /// <summary>
    /// Whether the grid shows the skeletons of the cards, because there is no card to show
    /// yet (AC6, UX-DR-28).
    ///
    /// <para>
    /// The skeletons have the REAL proportion of the grid, so the layout does NOT jump when
    /// the cards arrive, and there is NO «cargando» spinner (AC6, UX-DR-28).
    /// </para>
    /// </summary>
    public bool ShowsSkeletons => _loading || (Games.Count == 0 && HasFilters);

    /// <summary>
    /// Whether the catalog holds no videogame at all (AC4, UX-DR-28).
    /// </summary>
    public bool IsCatalogEmpty => !_loading && _games.Count == 0;

    /// <summary>
    /// Whether the server is connected, which decides between «Sincronizar ahora» and
    /// «Conectar al servidor» (AC4, UX-DR-28).
    ///
    /// <para>
    /// The connection is DEDUCED from the state of the synchronization: the catalog of the
    /// event is up to date when the last synchronization succeeded (AC3, AC4).
    /// </para>
    /// </summary>
    public bool IsServerConnected => _synchronized;

    /// <summary>
    /// Whether there is a filter active, which is the condition for offering «Quitar
    /// filtros» (AC3, UX-DR-28).
    /// </summary>
    public bool HasFilters =>
        !string.IsNullOrWhiteSpace(_searchText) || !string.IsNullOrWhiteSpace(_selectedCategory);

    /// <summary>
    /// Text that the surface shows when the catalog holds no videogame (AC4, UX-DR-28).
    /// </summary>
    public string EmptyCatalogMessage => EmptyCatalogText;

    /// <summary>
    /// Text of the action of the empty catalog: «Sincronizar ahora» when the server is
    /// connected and «Conectar al servidor» when it is not (AC4, UX-DR-28).
    /// </summary>
    public string EmptyCatalogActionText =>
        IsServerConnected ? SynchronizeNowText : ConnectToServerText;

    /// <summary>
    /// Text that the surface shows when a search has no results (AC3, UX-DR-28).
    /// </summary>
    public string NoResultsMessage =>
        $"Sin resultados para «{_searchText.Trim()}».";

    /// <summary>
    /// Text of the action that reverses the filters of a search without results
    /// (AC3, UX-DR-28).
    /// </summary>
    public string ClearFiltersActionText => ClearFiltersText;

    /// <summary>
    /// Whether the surface shows the state of «no results» for the current search
    /// (AC3, UX-DR-28).
    /// </summary>
    public bool ShowsNoResults => !_loading && HasFilters && _filtered.Count == 0;

    /// <summary>
    /// Replaces the snapshot that the surface shows and applies the filters over it
    /// (AC1, AC2).
    ///
    /// <para>
    /// The snapshot that arrives is the one that the client applied, so with the server
    /// down the surface keeps showing the last catalog that was synchronized (AC3).
    /// </para>
    /// </summary>
    /// <param name="snapshot">Snapshot that the client applied, or <c>null</c>.</param>
    /// <param name="synchronized">Whether the server is connected.</param>
    public void ApplySnapshot(CatalogSnapshot? snapshot, bool synchronized)
    {
        _synchronized = synchronized;
        _loading = false;

        _games.Clear();
        _games.AddRange(snapshot?.Games ?? []);

        _categories.Clear();
        _categories.AddRange(CatalogFilter.Categories(_games));

        // Una categoría que el snapshot ya no tiene deja de ser un filtro válido: el
        // selector vuelve a «todas» en lugar de mostrar un filtro que no puede matchear
        // nada (AC2).
        if (_selectedCategory is not null &&
            !_categories.Any(category =>
                string.Equals(category, _selectedCategory, StringComparison.OrdinalIgnoreCase)))
        {
            _selectedCategory = null;
        }

        Refilter();
    }

    /// <summary>
    /// Reverses the filters of a search without results, WITHOUT leaving the surface
    /// (AC3, UX-DR-28).
    /// </summary>
    public void ClearFilters()
    {
        _selectedCategory = null;
        _searchText = string.Empty;
        _currentPage = 1;

        CancelScheduledSearch();

        Refilter();
    }

    /// <summary>
    /// Goes to the previous page of the grid (AC5, UX-DR-27).
    /// </summary>
    public void GoBack()
    {
        if (!CanGoBack)
        {
            return;
        }

        _currentPage--;

        RefreshPage();
    }

    /// <summary>
    /// Goes to the next page of the grid (AC5, UX-DR-27).
    /// </summary>
    public void GoForward()
    {
        if (!CanGoForward)
        {
            return;
        }

        _currentPage++;

        RefreshPage();
    }

    /// <summary>
    /// Applies the filter over the snapshot that the client holds (AC1, AC2).
    /// </summary>
    private void Refilter()
    {
        _filtered.Clear();
        _filtered.AddRange(CatalogFilter.Apply(_games, _searchText, _selectedCategory));

        PageCount = CatalogFilter.PageCount(_filtered.Count);

        if (_currentPage > PageCount)
        {
            _currentPage = Math.Max(1, PageCount);
        }

        RefreshPage();
    }

    /// <summary>
    /// Puts the videogames of the current page in the grid (AC5, UX-DR-27).
    /// </summary>
    private void RefreshPage()
    {
        Games = CatalogFilter.Page(_filtered, _currentPage);

        Changed?.Invoke();
    }

    /// <summary>
    /// Schedules the application of the filter 200 ms after the last keystroke
    /// (AC1, UX-DR-9).
    ///
    /// <para>
    /// Each keystroke REPLACES the pending one, so the filter is applied once and only
    /// once after the player stops writing (AC1, UX-DR-9).
    /// </para>
    /// </summary>
    private void ScheduleSearch()
    {
        CancelScheduledSearch();

        _searchTimer = _timeProvider.CreateTimer(
            static state => ((CatalogSurfaceViewModel)state!).OnSearchDelayElapsed(),
            this,
            TimeSpan.FromMilliseconds(SearchDelayMilliseconds),
            Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Cancels the pending application of the filter.
    /// </summary>
    private void CancelScheduledSearch()
    {
        _searchTimer?.Dispose();
        _searchTimer = null;
    }

    /// <summary>
    /// Applies the filter once the delay of the search has elapsed (AC1, UX-DR-9).
    /// </summary>
    private void OnSearchDelayElapsed()
    {
        _searchTimer = null;

        // La búsqueda vuelve a la primera página: un resultado que no cabe en la página
        // anterior no puede dejar al jugador en una página vacía (AC1, AC5).
        _currentPage = 1;

        Refilter();
    }
}
