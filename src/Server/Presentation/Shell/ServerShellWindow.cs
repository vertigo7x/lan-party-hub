using Avalonia;
using Avalonia.Controls;
using LANPartyHub.Server.Presentation.Design;
using Avalonia.Layout;
using Avalonia.Media;
using LANPartyHub.Server.Presentation.Catalog;
using LANPartyHub.Server.Presentation.Clients;
using LANPartyHub.Server.Presentation.Packages;

namespace LANPartyHub.Server.Presentation.Shell;

/// <summary>
/// The THREE sections of the organizer (AC4, UX-DR-12).
/// </summary>
public enum ServerSection
{
    /// <summary>The CATALOG of the event.</summary>
    Catalogo,

    /// <summary>The CLIENTS connected to the event.</summary>
    Clientes,

    /// <summary>The PACKAGES published in the event.</summary>
    Paquetes,
}

/// <summary>
/// The SHELL of the organizer: a column of navigation on the left with the three
/// sections and the main area on the right (AC4, UX-DR-12).
///
/// <para>
/// El servidor tiene TRES secciones y una sola ventana: el organizador no pierde de
/// vista la tabla de clientes, y la navegación es una columna, igual que en el cliente.
/// </para>
/// </summary>
public sealed class ServerShellWindow : Window
{
    private readonly Dictionary<ServerSection, Control> _sections = [];
    private readonly Dictionary<ServerSection, Button> _buttons = [];
    private readonly Grid _area;

    /// <summary>
    /// The section that is in front right now.
    /// </summary>
    public ServerSection Current { get; private set; } = ServerSection.Catalogo;

    /// <summary>
    /// Creates the shell of the organizer.
    /// </summary>
    public ServerShellWindow()
    {
        Title = "LAN Party Hub — Organizador";
        Width = 1280;
        Height = 800;

        // El fondo de la ventana es la superficie base de la paleta del DISENO (AC1).
        Background = new SolidColorBrush(ServerDesignTokens.SurfaceBase);

        _area = new Grid();

        var navigation = new StackPanel { Spacing = 4 };

        foreach (var section in Sections)
        {
            Register(section, navigation);
        }

        var navigationColumn = new Border
        {
            Width = NavigationColumnWidth,
            Padding = new Thickness(8),
            BorderBrush = new SolidColorBrush(ServerDesignTokens.BorderHairline),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = navigation,
        };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
        };

        Grid.SetColumn(navigationColumn, 0);
        Grid.SetColumn(_area, 1);

        grid.Children.Add(navigationColumn);
        grid.Children.Add(_area);

        Content = grid;

        Show(ServerSection.Catalogo);
    }

    /// <summary>
    /// The sections of the organizer, in the order in which they appear in the column of
    /// navigation (AC4, UX-DR-12).
    /// </summary>
    public static IReadOnlyList<ServerSection> Sections { get; } =
    [
        ServerSection.Catalogo,
        ServerSection.Clientes,
        ServerSection.Paquetes,
    ];

    /// <summary>
    /// Fixed width of the column of the navigation of the organizer (AC4).
    /// </summary>
    public const double NavigationColumnWidth = 200;

    /// <summary>
    /// The name of the column of the navigation, para que las pruebas la encuentren sin
    /// depender del texto.
    /// </summary>
    public const string NavigationColumnName = "ServerNavigation";

    /// <summary>
    /// The name of the area of the section in front, para que las pruebas la encuentren
    /// sin depender del texto.
    /// </summary>
    public const string SectionAreaName = "ServerSectionArea";

    /// <summary>
    /// Registers the CONTENT of a section, para que la navegación la muestre y la
    /// esconda (AC4).
    /// </summary>
    /// <param name="section">The section to register.</param>
    /// <param name="content">The content of the section.</param>
    public void RegisterContent(ServerSection section, Control content)
    {
        ArgumentNullException.ThrowIfNull(content);

        _sections[section] = content;
        _area.Children.Add(content);

        content.IsVisible = section == Current;
    }

    /// <summary>
    /// Shows a section of the organizer and hides the others (AC4).
    /// </summary>
    /// <param name="section">The section to show.</param>
    public void Show(ServerSection section)
    {
        Current = section;

        foreach (var pair in _sections)
        {
            pair.Value.IsVisible = pair.Key == section;
        }

        foreach (var pair in _buttons)
        {
            var active = pair.Key == section;

            pair.Value.Background = active
                ? new SolidColorBrush(ServerDesignTokens.AccentAction)
                : Brushes.Transparent;

            pair.Value.Foreground = active
                ? new SolidColorBrush(ServerDesignTokens.AccentActionInk)
                : new SolidColorBrush(ServerDesignTokens.InkPrimary);
        }
    }

    /// <summary>
    /// Builds the entry of a section in the column of navigation (AC4).
    /// </summary>
    /// <param name="section">The section of the entry.</param>
    /// <param name="navigation">The column of the navigation.</param>
    private void Register(ServerSection section, StackPanel navigation)
    {
        var button = new Button
        {
            Content = LabelOf(section),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Height = 36,
        };

        button.Click += (_, _) => Show(section);

        _buttons[section] = button;

        navigation.Children.Add(button);
    }

    /// <summary>
    /// The label of a section, tal y como lo ve el organizador (AC4).
    /// </summary>
    /// <param name="section">The section to name.</param>
    /// <returns>The label of the section.</returns>
    public static string LabelOf(ServerSection section) => section switch
    {
        ServerSection.Catalogo => "Catálogo",
        ServerSection.Clientes => "Clientes",
        ServerSection.Paquetes => "Paquetes",
        _ => throw new ArgumentOutOfRangeException(nameof(section)),
    };
}
