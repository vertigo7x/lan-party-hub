using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LANPartyHub.Server.Application.Catalog;
using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Server.Presentation.Catalog;

/// <summary>
/// Window of the section of the catalog of the event (AD-1, AC1).
///
/// <para>
/// The window has the shape that the design fixes for the server: a left column of
/// navigation with the catalog, the clients and the packages, and a main area that holds
/// the list of the videogames and the access to the form of the alta y la modificación.
/// </para>
/// <para>
/// The window is built in code, like the whole interface of the product: the repository
/// has no XAML file (AD-1).
/// </para>
/// <para>
/// The list is filled with the videogames that the read port of the repository returns, and
/// the button of the alta opens the form, which hands the videogame to the use case of the
/// catalog: the server and its interface live in the same process (AD-1).
/// </para>
/// </summary>
public sealed class CatalogWindow : Window
{
    /// <summary>
    /// Font of the literals of the list: the identifier and the version of the videogame
    /// (AC4).
    /// </summary>
    public static readonly FontFamily MonospaceFontFamily = GameFormView.MonospaceFontFamily;

    /// <summary>
    /// Font of the small text of the list (AC4).
    /// </summary>
    public static readonly FontFamily MetaFontFamily = GameFormView.MetaFontFamily;

    private readonly RegisterGame _registerGame;
    private readonly ModifyGame _modifyGame;
    private readonly ListGames _listGames;
    private readonly StackPanel _rows;
    private readonly TextBlock _summary;
    private readonly TextBlock _status;

    /// <summary>
    /// Creates the window of the section of the catalog.
    /// </summary>
    /// <param name="viewModel">View model of the section.</param>
    /// <param name="registerGame">Use case of the registration of a videogame.</param>
    /// <param name="modifyGame">Use case of the modification of a videogame.</param>
    /// <param name="listGames">Use case that reads the videogames of the catalog.</param>
    public CatalogWindow(
        CatalogViewModel viewModel,
        RegisterGame registerGame,
        ModifyGame modifyGame,
        ListGames listGames)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(registerGame);
        ArgumentNullException.ThrowIfNull(modifyGame);
        ArgumentNullException.ThrowIfNull(listGames);

        Title = "Catálogo";
        Width = 960;
        Height = 600;

        ViewModel = viewModel;

        _registerGame = registerGame;
        _modifyGame = modifyGame;
        _listGames = listGames;

        var navigation = new StackPanel
        {
            Margin = new Thickness(12),
            Spacing = 8,
            Width = 200,
            Children =
            {
                new TextBlock
                {
                    Text = "LAN Party Hub",
                    FontSize = 22,
                    FontWeight = FontWeight.SemiBold,
                },
                new TextBlock { Text = "Catálogo" },
                new TextBlock { Text = "Clientes" },
                new TextBlock { Text = "Paquetes" },
            },
        };

        _rows = new StackPanel
        {
            Spacing = 8,
        };

        var add = new Button { Content = "Registrar videojuego" };
        var refresh = new Button { Content = "Actualizar catálogo" };
        _summary = new TextBlock
        {
            FontFamily = MetaFontFamily,
            FontSize = GameFormView.MetaFontSize,
        };
        _status = new TextBlock { TextWrapping = TextWrapping.Wrap };

        add.Click += async (_, _) => await OpenFormAsync().ConfigureAwait(true);
        refresh.Click += async (_, _) => await RefreshAsync().ConfigureAwait(true);

        var main = new StackPanel
        {
            Margin = new Thickness(12),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = "Catálogo",
                    FontSize = 22,
                    FontWeight = FontWeight.SemiBold,
                },
                _summary,
                new ScrollViewer
                {
                    Content = _rows,
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        add,
                        refresh,
                    },
                },
                _status,
            },
        };

        Content = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(224) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            },
            Children =
            {
                navigation,
                main,
            },
        };

        Grid.SetColumn(navigation, 0);
        Grid.SetColumn(main, 1);

        // The list is read when the window opens, so the section never shows an empty
        // catalog while the database holds videogames. The organizer can still refresh it
        // with the button.
        Opened += async (_, _) => await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>View model of the section.</summary>
    public CatalogViewModel ViewModel { get; }

    /// <summary>
    /// Reads the videogames of the catalog and fills the list of the section with them.
    /// </summary>
    /// <returns>A task that completes when the list has been filled.</returns>
    public async Task RefreshAsync()
    {
        try
        {
            var games = await _listGames.ExecuteAsync(CancellationToken.None).ConfigureAwait(true);

            ViewModel.Replace(games);

            RebuildRows();

            _summary.Text = $"{ViewModel.Count} videojuegos publicados.";
        }
        catch (Exception exception)
        {
            // The exception never reaches the interface as a stack trace: the organizer
            // sees a line and the operator finds the detail in the log (AD-15).
            _status.Text = $"No se pudo leer el catálogo: {exception.Message}";
        }
    }

    /// <summary>
    /// Opens the form of the alta. The form hands the videogame to the use case of the
    /// catalog, and the list is refreshed when the videogame has been saved.
    /// </summary>
    /// <returns>A task that completes when the form has been closed.</returns>
    private async Task OpenFormAsync()
    {
        var form = new GameFormView(ViewModel.CreateFormViewModel(), _registerGame, _modifyGame);

        await form.ShowDialog(this).ConfigureAwait(true);

        if (form.SavedGame is not null)
        {
            await RefreshAsync().ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Opens the form of the modificación of one of the videogames of the list.
    /// </summary>
    /// <param name="game">Contract of the videogame to modify.</param>
    /// <returns>A task that completes when the form has been closed.</returns>
    private async Task OpenFormAsync(GameDescriptor game)
    {
        var form = new GameFormView(ViewModel.CreateFormViewModel(game), _registerGame, _modifyGame);

        await form.ShowDialog(this).ConfigureAwait(true);

        if (form.SavedGame is not null)
        {
            await RefreshAsync().ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Builds the rows of the list again, with the videogames that the view model holds.
    /// </summary>
    private void RebuildRows()
    {
        _rows.Children.Clear();

        foreach (var row in ViewModel.Rows)
        {
            var game = ViewModel.Games.FirstOrDefault(candidate => candidate.GameId == row.GameId);

            var edit = new Button { Content = "Editar" };

            if (game is not null)
            {
                var captured = game;

                edit.Click += async (_, _) => await OpenFormAsync(captured).ConfigureAwait(true);
            }

            _rows.Children.Add(BuildRow(row, edit));
        }
    }

    /// <summary>
    /// Builds the row of a videogame of the list. The name, the version and the size are
    /// the data that the organizer compares, so the version goes in a monospaced font and
    /// the size carries the numeric value and the unit apart (AC4).
    /// </summary>
    /// <param name="row">Row of the videogame.</param>
    /// <param name="edit">Button that opens the form of the modificación.</param>
    /// <returns>The control of the row.</returns>
    private static Border BuildRow(CatalogGameRow row, Button edit)
    {
        var size = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children =
            {
                new TextBlock
                {
                    Text = row.SizeValue,
                    FontFamily = MonospaceFontFamily,
                    FontSize = GameFormView.MonospaceFontSize,
                },
                new TextBlock
                {
                    Text = row.SizeUnit,
                    FontFamily = MetaFontFamily,
                    FontSize = GameFormView.MetaFontSize,
                },
            },
        };

        var content = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = row.Name },
                new TextBlock
                {
                    Text = row.Version,
                    FontFamily = MonospaceFontFamily,
                    FontSize = GameFormView.MonospaceFontSize,
                },
                size,
                new TextBlock
                {
                    Text = $"{row.MinPlayers}-{row.MaxPlayers} jugadores",
                    FontFamily = MetaFontFamily,
                    FontSize = GameFormView.MetaFontSize,
                },
                edit,
            },
        };

        return new Border
        {
            Padding = new Thickness(12),
            BorderThickness = new Thickness(0, 0, 0, 1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x28, 0x2C, 0x37, 0xFF)),
            Child = content,
        };
    }
}
