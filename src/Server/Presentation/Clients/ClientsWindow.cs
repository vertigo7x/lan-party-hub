using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using LANPartyHub.Server.Application.Clients;
using LANPartyHub.Server.Presentation.Catalog;

namespace LANPartyHub.Server.Presentation.Clients;

/// <summary>
/// Window of the section of the clients of the event (AD-1, AD-29, AC1).
///
/// <para>
/// The section shows a DENSE TABLE, not a collection of cards: comparing teams
/// requires alignment. The table has one row per client, 40 pixels high, the data
/// cells in the strong body typography and the units in the metadata typography, and
/// a hairline separator between rows (UX-DR-14, DESIGN <c>data-table-row</c>).
/// </para>
/// <para>
/// The order of the rows is STABLE by team name, and the row is selectable so the
/// organizer can open its detail without leaving the table (UX-DR-14).
/// </para>
/// <para>
/// The column of the team name always shows the EFFECTIVE name, the one under which
/// the row is registered, and never the proposed name that collided (AD-30).
/// </para>
/// <para>
/// The window is built in code, like the whole interface of the product: the
/// repository has no XAML file (AD-1).
/// </para>
/// </summary>
public sealed class ClientsWindow : Window
{
    /// <summary>
    /// Name that the row of the dense table carries, so the row is identifiable among
    /// the borders of the visual tree (UX-DR-14).
    /// </summary>
    public const string RowName = "ClientRow";

    /// <summary>
    /// Height of a row of the dense table, in pixels (UX-DR-14).
    /// </summary>
    public const double RowHeight = 40;

    /// <summary>
    /// Font of the literals of the table: the identifier and the version of the client.
    /// </summary>
    public static readonly FontFamily MonospaceFontFamily = GameFormView.MonospaceFontFamily;

    /// <summary>
    /// Font of the small text of the table: the units and the metadata (UX-DR-14).
    /// </summary>
    public static readonly FontFamily MetaFontFamily = GameFormView.MetaFontFamily;

    /// <summary>
    /// Size of the strong body typography, the one of the data cells (UX-DR-14).
    /// </summary>
    public const double BodyStrongFontSize = 14;

    /// <summary>
    /// Size of the metadata typography, the one of the units (UX-DR-14).
    /// </summary>
    public const double MetaFontSize = GameFormView.MetaFontSize;

    /// <summary>
    /// Thickness of the hairline that separates two rows of the table (DESIGN).
    /// </summary>
    public const double SeparatorThickness = 1;

    private readonly ListClients _listClients;
    private readonly StackPanel _rows;
    private readonly TextBlock _summary;
    private readonly TextBlock _status;
    private readonly Border _detail;
    private readonly TextBlock _detailText;
    private ClientRow? _selected;

    /// <summary>
    /// Creates the window of the section of the clients.
    /// </summary>
    /// <param name="viewModel">View model of the section.</param>
    /// <param name="listClients">Use case that reads the registered clients.</param>
    public ClientsWindow(ClientsViewModel viewModel, ListClients listClients)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(listClients);

        Title = "Clientes";
        Width = 1100;
        Height = 620;

        ViewModel = viewModel;

        _listClients = listClients;

        _rows = new StackPanel
        {
            Spacing = 0,
        };

        var refresh = new Button { Content = "Actualizar clientes" };
        _summary = new TextBlock
        {
            FontFamily = MetaFontFamily,
            FontSize = MetaFontSize,
        };
        _status = new TextBlock { TextWrapping = TextWrapping.Wrap };

        refresh.Click += async (_, _) => await RefreshAsync().ConfigureAwait(true);

        _detailText = new TextBlock
        {
            FontFamily = MetaFontFamily,
            FontSize = MetaFontSize,
            TextWrapping = TextWrapping.Wrap,
        };

        _detail = new Border
        {
            Padding = new Thickness(12),
            BorderThickness = new Thickness(0, SeparatorThickness, 0, 0),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x28, 0x2C, 0x37, 0xFF)),
            Child = _detailText,
            IsVisible = false,
        };

        var main = new StackPanel
        {
            Margin = new Thickness(12),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = "Clientes",
                    FontSize = 22,
                    FontWeight = FontWeight.SemiBold,
                },
                _summary,
                new ScrollViewer
                {
                    HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                    Content = new StackPanel
                    {
                        Spacing = 0,
                        Children =
                        {
                            BuildHeader(),
                            _rows,
                        },
                    },
                },
                _detail,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        refresh,
                    },
                },
                _status,
            },
        };

        Content = main;

        // The table is read when the window opens, so the section never shows an empty
        // table while the server holds registered clients. The organizer can still
        // refresh it with the button.
        Opened += async (_, _) => await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>View model of the section.</summary>
    public ClientsViewModel ViewModel { get; }

    /// <summary>
    /// Reads the registered clients and fills the table of the section with them.
    /// </summary>
    /// <returns>A task that completes when the table has been filled.</returns>
    public async Task RefreshAsync()
    {
        try
        {
            var clients = await _listClients.ExecuteAsync(CancellationToken.None).ConfigureAwait(true);

            ViewModel.Replace(clients);

            RebuildRows();

            _summary.Text = $"{ViewModel.Count} clientes registrados.";

            // La línea de estado dice cómo fue la última lectura, así que una lectura
            // que funciona la BORRA: el organizador no debe leer un error viejo debajo
            // de una tabla correcta.
            _status.Text = string.Empty;
        }
        catch (Exception exception)
        {
            // The exception never reaches the interface as a stack trace: the organizer
            // sees a line and the operator finds the detail in the log (AD-15).
            _status.Text = $"No se pudo leer la vista de clientes: {exception.Message}";
        }
    }

    /// <summary>
    /// Builds the header of the table. The columns of the games installed, the active
    /// downloads, the progress of the installations and the registered errors are
    /// RESERVED: their heading is already the final one, and their data arrives with
    /// the stories 9.2 and 9.3 (FR-10).
    /// </summary>
    /// <returns>The header of the table.</returns>
    private static Grid BuildHeader()
    {
        var header = new Grid
        {
            ColumnDefinitions = BuildColumnDefinitions(),
        };

        for (var column = 0; column < ColumnHeaders.Length; column++)
        {
            var cell = new TextBlock
            {
                Text = ColumnHeaders[column],
                FontFamily = MetaFontFamily,
                FontSize = MetaFontSize,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            };

            Grid.SetColumn(cell, column);

            header.Children.Add(cell);
        }

        return header;
    }

    /// <summary>
    /// Builds the rows of the table again, with the clients that the view model holds.
    /// </summary>
    private void RebuildRows()
    {
        _rows.Children.Clear();

        // La fila SELECCIONADA puede desaparecer con el refresco, así que el detalle
        // se cierra: el organizador nunca ve el detalle de una fila que ya no está en
        // la tabla (AD-29).
        _selected = null;
        _detail.IsVisible = false;

        foreach (var row in ViewModel.Rows)
        {
            _rows.Children.Add(BuildRow(row));
        }
    }

    /// <summary>
    /// Builds the row of a client of the table. The row is 40 pixels high, the data
    /// cells use the strong body typography and the units the metadata one, and the
    /// identifier and the version go in the monospaced typography because they are
    /// compared between them (UX-DR-14).
    /// </summary>
    /// <param name="row">Row of the client.</param>
    /// <returns>The control of the row.</returns>
    private Border BuildRow(ClientRow row)
    {
        var cells = new[]
        {
            BuildMonospaceCell(row.ClientId),
            BuildDataCell(row.MachineName),
            BuildDataCell(row.ConnectionStateText),
            BuildMonospaceCell(row.ClientVersion),
            BuildMonospaceCell(row.ProtocolVersion),
            BuildMetaCell(row.LastSeenAt),
            BuildReservedCell(),
            BuildReservedCell(),
            BuildReservedCell(),
            BuildReservedCell(),
        };

        var grid = new Grid
        {
            ColumnDefinitions = BuildColumnDefinitions(),
            Height = RowHeight,
        };

        for (var column = 0; column < cells.Length; column++)
        {
            Grid.SetColumn(cells[column], column);

            grid.Children.Add(cells[column]);
        }

        var border = new Border
        {
            Name = RowName,
            Height = RowHeight,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0, 0, 0, SeparatorThickness),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x28, 0x2C, 0x37, 0xFF)),
            Child = grid,
        };

        // La fila es SELECCIONABLE: el organizador la elige y ve su detalle sin salir
        // de la tabla (UX-DR-14).
        border.PointerPressed += (_, _) => Select(row);

        return border;
    }

    /// <summary>
    /// Selects a row of the table and shows its detail.
    ///
    /// <para>
    /// La selección es una operación de la ventana, no un efecto colateral del clic: el
    /// organizador elige la fila y ve su detalle sin salir de la tabla (UX-DR-14).
    /// </para>
    /// </summary>
    /// <param name="row">Row of the client that the organizer chose.</param>
    public void Select(ClientRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        _selected = row;

        _detailText.Text =
            $"{row.MachineName} · {row.ClientId} · {row.ConnectionStateText} · "
            + $"cliente {row.ClientVersion} · protocolo {row.ProtocolVersion} · "
            + $"último contacto {row.LastSeenAt}";

        _detail.IsVisible = true;
    }

    /// <summary>
    /// The row of the table that is selected, or <c>null</c> when no row is.
    /// </summary>
    public ClientRow? SelectedRow => _selected;

    /// <summary>
    /// Builds a cell of data with the strong body typography (UX-DR-14).
    /// </summary>
    /// <param name="value">Value of the cell.</param>
    /// <returns>The control of the cell.</returns>
    private static TextBlock BuildDataCell(string value) => new()
    {
        Text = value,
        FontSize = BodyStrongFontSize,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>
    /// Builds a cell of literal with the monospaced typography, the one of the
    /// identifiers and the versions (UX-DR-14).
    /// </summary>
    /// <param name="value">Value of the cell.</param>
    /// <returns>The control of the cell.</returns>
    private static TextBlock BuildMonospaceCell(string value) => new()
    {
        Text = value,
        FontFamily = MonospaceFontFamily,
        FontSize = BodyStrongFontSize,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>
    /// Builds a cell of metadata, the one of the instants and the units (UX-DR-14).
    /// </summary>
    /// <param name="value">Value of the cell.</param>
    /// <returns>The control of the cell.</returns>
    private static TextBlock BuildMetaCell(string value) => new()
    {
        Text = value,
        FontFamily = MetaFontFamily,
        FontSize = MetaFontSize,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>
    /// Builds a cell of a column RESERVED for the stories 9.2 and 9.3. The heading is
    /// already the final one, and the cell stays empty because the server does not hold
    /// that data yet: la vista no inventa un dato que no existe (FR-10).
    /// </summary>
    /// <returns>The control of the cell.</returns>
    private static TextBlock BuildReservedCell() => new()
    {
        FontFamily = MetaFontFamily,
        FontSize = MetaFontSize,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>
    /// The headings of the columns of the table, in order (FR-10, UX-DR-14).
    /// </summary>
    public static readonly string[] ColumnHeaders =
    [
        "Identificador",
        "Nombre del equipo",
        "Estado de conexión",
        "Versión del cliente",
        "Versión del protocolo",
        "Último contacto",
        "Juegos instalados",
        "Descargas activas",
        "Progreso de instalaciones",
        "Errores registrados",
    ];

    /// <summary>
    /// The widths of the columns of the table, in order. The table is dense, so every
    /// column has a fixed width and the rows never change their height (UX-DR-14).
    ///
    /// <para>
    /// Las columnas suman 1180 px y la ventana mide 1100, así que la tabla va dentro
    /// de un visor con scroll HORIZONTAL: las columnas reservadas de las historias 9.2
    /// y 9.3 tienen que ser alcanzables, no recortadas (FR-10).
    /// </para>
    /// </summary>
    private static ColumnDefinitions BuildColumnDefinitions() =>
    [
        new ColumnDefinition { Width = new GridLength(220) },
        new ColumnDefinition { Width = new GridLength(170) },
        new ColumnDefinition { Width = new GridLength(130) },
        new ColumnDefinition { Width = new GridLength(100) },
        new ColumnDefinition { Width = new GridLength(100) },
        new ColumnDefinition { Width = new GridLength(200) },
        new ColumnDefinition { Width = new GridLength(130) },
        new ColumnDefinition { Width = new GridLength(110) },
        new ColumnDefinition { Width = new GridLength(130) },
        new ColumnDefinition { Width = new GridLength(110) },
    ];
}
