using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LANPartyHub.Client.Application.CatalogSync;

namespace LANPartyHub.Client.Presentation.Catalog;

/// <summary>
/// Surface of the catalog of the player (AC1, AC2, AC3, AC4, AC5, AC6, FR-2, UX-DR-9,
/// UX-DR-27, UX-DR-28).
///
/// <para>
/// The surface has a search field WITHOUT a «buscar» button, a selector of category, the
/// grid of the cards, the explicit pagination and the two empty states: the one of a
/// search without results and the one of an empty catalog (AC1, AC2, AC3, AC4, AC5).
/// </para>
/// <para>
/// The surface NEVER leaves the player to reverse a search: «Quitar filtros» restores the
/// query and the category in place (AC3, UX-DR-28).
/// </para>
/// </summary>
public sealed class CatalogSurface : UserControl
{
    private readonly CatalogSurfaceViewModel _viewModel;
    private readonly CatalogGrid _grid;
    private readonly TextBox _search;
    private readonly ComboBox _category;
    private readonly Button _clearFilters;
    private readonly Button _emptyAction;
    private readonly TextBlock _emptyText;
    private readonly TextBlock _pageText;
    private readonly Button _previous;
    private readonly Button _next;

    /// <summary>
    /// Creates the surface of the catalog.
    /// </summary>
    /// <param name="viewModel">View model of the surface.</param>
    /// <param name="onEmptyAction">
    /// Action that the player triggers with «Sincronizar ahora» or «Conectar al servidor»
    /// when the catalog is empty, or <c>null</c> when the surface is built without it.
    /// </param>
    public CatalogSurface(CatalogSurfaceViewModel viewModel, Action? onEmptyAction = null)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        _viewModel = viewModel;

        // El campo de búsqueda NO lleva botón «buscar»: el filtro se aplica al escribir,
        // con 200 ms de retardo (AC1, UX-DR-9).
        _search = new TextBox
        {
            PlaceholderText = "Buscar juegos",
            MinWidth = 240,
        };

        _category = new ComboBox
        {
            MinWidth = 160,
            PlaceholderText = "Todas las categorías",
        };

        _grid = new CatalogGrid();

        _clearFilters = new Button { Content = CatalogSurfaceViewModel.ClearFiltersText };
        _emptyAction = new Button();
        _emptyText = new TextBlock { TextWrapping = TextWrapping.Wrap };

        _previous = new Button { Content = "Anterior" };
        _next = new Button { Content = "Siguiente" };
        _pageText = new TextBlock { VerticalAlignment = VerticalAlignment.Center };

        _search.TextChanged += (_, _) => _viewModel.SearchText = _search.Text ?? string.Empty;

        _category.SelectionChanged += (_, _) =>
            _viewModel.SelectedCategory = _category.SelectedItem as string;

        _clearFilters.Click += (_, _) => ClearFilters();

        _emptyAction.Click += (_, _) => onEmptyAction?.Invoke();

        _previous.Click += (_, _) => _viewModel.GoBack();
        _next.Click += (_, _) => _viewModel.GoForward();

        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = "Catálogo",
                    FontSize = 22,
                    FontWeight = Avalonia.Media.FontWeight.SemiBold,
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        _search,
                        _category,
                    },
                },
                _emptyText,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        _emptyAction,
                        _clearFilters,
                    },
                },
                _grid,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Spacing = 12,
                    Children =
                    {
                        _previous,
                        _pageText,
                        _next,
                    },
                },
            },
        };

        _viewModel.Changed += Render;

        Render();
    }

    /// <summary>
    /// Shows the state of the synchronization of the catalog in the surface (AC3, AC4).
    ///
    /// <para>
    /// The snapshot that the client applied is the one that the surface filters, so with
    /// the server down the player keeps seeing the last catalog that he had (AC3).
    /// </para>
    /// </summary>
    /// <param name="state">State of the synchronization of the catalog.</param>
    public void ApplySyncState(CatalogSyncState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        _viewModel.ApplySnapshot(state.Snapshot, state.Synchronized);
    }

    /// <summary>
    /// Reverses the filters of a search without results, without leaving the surface
    /// (AC3, UX-DR-28).
    /// </summary>
    private void ClearFilters()
    {
        _viewModel.ClearFilters();

        _search.Text = string.Empty;
        _category.SelectedItem = null;
    }

    /// <summary>
    /// Renders the state of the view model in the surface (AC1, AC2, AC3, AC4, AC5, AC6).
    /// </summary>
    private void Render()
    {
        _search.Text = _viewModel.SearchText;
        _category.ItemsSource = _viewModel.Categories;

        if (_viewModel.SelectedCategory is not null &&
            !_category.Items.OfType<string>().Contains(_viewModel.SelectedCategory, StringComparer.OrdinalIgnoreCase))
        {
            _category.SelectedItem = null;
        }
        else
        {
            _category.SelectedItem = _viewModel.SelectedCategory;
        }

        // Los dos estados vacíos son EXCLUYENTES: el catálogo vacío manda sobre el estado
        // de «sin resultados», porque no hay nada que filtrar todavía (AC3, AC4).
        if (_viewModel.IsCatalogEmpty)
        {
            _emptyText.Text = _viewModel.EmptyCatalogMessage;
            _emptyAction.Content = _viewModel.EmptyCatalogActionText;
            _emptyAction.IsVisible = true;
            _clearFilters.IsVisible = false;
        }
        else if (_viewModel.ShowsNoResults)
        {
            _emptyText.Text = _viewModel.NoResultsMessage;
            _emptyAction.IsVisible = false;
            _clearFilters.IsVisible = true;
        }
        else
        {
            _emptyText.Text = string.Empty;
            _emptyAction.IsVisible = false;
            _clearFilters.IsVisible = false;
        }

        _grid.Render(
            _viewModel.Games,
            _viewModel.ShowsSkeletons,
            _viewModel.IsCatalogEmpty || _viewModel.ShowsNoResults ? _emptyText.Text : null);

        _previous.IsEnabled = _viewModel.CanGoBack;
        _next.IsEnabled = _viewModel.CanGoForward;
        _pageText.Text = _viewModel.PageCount == 0
            ? string.Empty
            : $"Página {_viewModel.CurrentPage} de {_viewModel.PageCount}";
    }
}
