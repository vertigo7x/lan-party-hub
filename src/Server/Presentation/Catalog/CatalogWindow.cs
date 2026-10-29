using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

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

    /// <summary>
    /// Creates the window of the section of the catalog.
    /// </summary>
    /// <param name="viewModel">View model of the section.</param>
    public CatalogWindow(CatalogViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        Title = "Catálogo";
        Width = 960;
        Height = 600;

        ViewModel = viewModel;

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

        var rows = new StackPanel
        {
            Spacing = 8,
        };

        foreach (var row in viewModel.Rows)
        {
            rows.Children.Add(BuildRow(row));
        }

        var add = new Button { Content = "Registrar videojuego" };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };

        add.Click += (_, _) => status.Text = "El formulario de alta se abre desde el catálogo.";

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
                new TextBlock
                {
                    Text = $"{viewModel.Count} videojuegos publicados.",
                    FontFamily = MetaFontFamily,
                    FontSize = GameFormView.MetaFontSize,
                },
                new ScrollViewer
                {
                    Content = rows,
                },
                add,
                status,
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
    }

    /// <summary>View model of the section.</summary>
    public CatalogViewModel ViewModel { get; }

    /// <summary>
    /// Builds the row of a videogame of the list. The name, the version and the size are
    /// the data that the organizer compares, so the version goes in a monospaced font and
    /// the size carries the numeric value and the unit apart (AC4).
    /// </summary>
    /// <param name="row">Row of the videogame.</param>
    /// <returns>The control of the row.</returns>
    private static Border BuildRow(CatalogGameRow row)
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
