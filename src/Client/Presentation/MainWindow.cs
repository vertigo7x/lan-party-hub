using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using LANPartyHub.Client.Application;
using LANPartyHub.Client.Application.CatalogSync;
using LANPartyHub.Client.Presentation.Catalog;
using LANPartyHub.Client.Presentation.Connection;
using LANPartyHub.Client.Presentation.Design;
using LANPartyHub.Client.Presentation.Diagnostics;
using LANPartyHub.Client.Presentation.Library;
using LANPartyHub.Client.Presentation.Shell;
using LANPartyHub.Client.Presentation.Status;

namespace LANPartyHub.Client.Presentation;

/// <summary>
/// Main window of the client. Story 1.1 only declares the shell: the surfaces of the
/// catalog, the downloads and the library arrive with the following stories.
/// <para>
/// The window carries the settings surface of the installation root, which is the only
/// directory that the rule of extraction confines (AD-9, AC4). The rest of the settings
/// surface (the connection, the theme and the log of diagnostics) belongs to Epic 11.
/// </para>
/// <para>
/// The window carries the notice of the pending synchronization of the catalog (AC3,
/// UX-DR-32). The notice is a discreet bar that does NOT block the navigation: the
/// surfaces of the window stay usable while the catalog is pending.
/// </para>
/// <para>
/// AC1: la ventana tiene DOS COLUMNAS: el contenido a la izquierda y el panel PERSISTENTE
/// de la conexión a la derecha, con una anchura FIJA. El panel no se colapsa, no se esconde
/// en un menú ni en un modal, y no hay ninguna <c>Window</c> que interrumpa al
/// jugador (AC1, AC3, UX-DR-11, UX-DR-29).
/// </para>
/// </summary>
public sealed class MainWindow : Window
{
    private readonly CatalogSyncBanner? _banner;
    private readonly CatalogSurface? _catalogSurface;
    private readonly LibrarySurface? _librarySurface;
    private readonly ConnectionStatusPanel? _statusPanel;
    private readonly DiagnosticsLogView? _diagnosticsView;
    private readonly ShellViewModel _shell = new();
    private readonly Dictionary<ClientSurface, Border> _surfaceHosts = [];
    private readonly Dictionary<ClientSurface, Button> _navigationButtons = [];

    /// <summary>
    /// Creates the main window of the client.
    /// </summary>
    /// <param name="settings">
    /// Settings of the client, or <c>null</c> when the window is built without them.
    /// </param>
    /// <param name="onSynchronizeNow">
    /// Action that the player triggers with «Sincronizar ahora», or <c>null</c> when the
    /// window is built without the synchronization of the catalog.
    /// </param>
    /// <param name="catalogSurface">
    /// Surface of the catalog of the player, or <c>null</c> when the window is built
    /// without it.
    /// </param>
    /// <param name="connectionSurface">
    /// Surface of the connection of the player, or <c>null</c> when the window is built
    /// without it.
    /// </param>
    /// <param name="statusPanel">
    /// Persistent panel of the connection, or <c>null</c> when the window is built without
    /// it.
    /// </param>
    /// <param name="librarySurface">
    /// Surface of the Biblioteca of the player, or <c>null</c> when the window is built
    /// without it (AC8).
    /// </param>
    /// <param name="diagnosticsView">
    /// The VISOR de diagnóstico del cliente, or <c>null</c> when the window is built
    /// without it (AC1, NFR-7).
    /// </param>
    public MainWindow(
        ClientSettings? settings = null,
        Func<Task>? onSynchronizeNow = null,
        CatalogSurface? catalogSurface = null,
        ConnectionSurface? connectionSurface = null,
        ConnectionStatusPanel? statusPanel = null,
        LibrarySurface? librarySurface = null,
        DiagnosticsLogView? diagnosticsView = null)
    {
        Title = "LAN Party Hub";
        Width = 1280;
        Height = 800;

        var installDirectory = new TextBox
        {
            PlaceholderText = "Directorio de instalación",
            Text = settings?.InstallDirectory ?? string.Empty,
        };

        var save = Buttons.Primary("Guardar");
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };

        save.Click += async (_, _) =>
        {
            if (settings is null)
            {
                status.Text = "Los ajustes del cliente no están disponibles.";

                return;
            }

            try
            {
                var result = await settings.SaveInstallDirectoryAsync(installDirectory.Text ?? string.Empty);

                status.Text = $"El directorio de instalación es ahora '{result.InstallDirectory}'.";
            }
            catch (Exception exception)
            {
                status.Text = exception.Message;
            }
        };

        // El aviso del catálogo desactualizado vive ENCIMA del contenido y no bloquea la
        // navegación: no es un modal, no desactiva las superficies y no roba el foco
        // (AC3, UX-DR-32).
        var banner = onSynchronizeNow is null
            ? null
            : new CatalogSyncBanner(onSynchronizeNow);

        if (banner is not null)
        {
            banner.Hide();
        }

        _banner = banner;

        _catalogSurface = catalogSurface;
        _librarySurface = librarySurface;
        _statusPanel = statusPanel;
        _diagnosticsView = diagnosticsView;

        var content = new StackPanel
        {
            Margin = new Thickness(DesignTokens.Spacing4),
            Spacing = DesignTokens.Spacing2,
            Children =
            {
                new TextBlock { Text = "LAN Party Hub" },
                new TextBlock { Text = "Directorio de instalación" },
                installDirectory,
                save,
                status,
            },
        };

        // La navegacion es una COLUMNA, no una barra inferior (AC1, UX-DR-9), y las
        // superficies se muestran de una en una: la columna dice donde esta el jugador.
        var navigation = new StackPanel { Spacing = DesignTokens.Spacing1 };
        var surfaceArea = new Grid();

        void RegisterSurface(ClientSurface surface, string label, Control? control)
        {
            if (control is null)
            {
                return;
            }

            var host = new Border { Child = control };

            surfaceArea.Children.Add(host);
            _surfaceHosts[surface] = host;

            var button = Buttons.Ghost(label);

            button.Name = NavigationButtonName(surface);
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.Click += (_, _) => _shell.Navigate(surface);

            _navigationButtons[surface] = button;
            navigation.Children.Add(button);
        }

        var layout = new StackPanel { Spacing = 0 };

        if (banner is not null)
        {
            layout.Children.Add(banner);
        }

        // AC1, NFR-7: el VISOR de diagnóstico vive en la sección de «Configuración y
        // diagnóstico» de la ventana, junto al directorio de instalación, así que el
        // equipo ve los errores que su cliente registró sin salir de la aplicación.
        var settingsPanel = new StackPanel { Spacing = DesignTokens.Spacing2 };

        settingsPanel.Children.Add(content);

        if (diagnosticsView is not null)
        {
            settingsPanel.Children.Add(new StackPanel
            {
                Spacing = DesignTokens.Spacing1,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Configuración y diagnóstico",
                        FontSize = DesignTokens.TitleFontSize,
                        FontWeight = DesignTokens.HeadlineFontWeight,
                    },
                    diagnosticsView,
                },
            });
        }

        // La superficie del catálogo vive DEBAJO del aviso de sincronización: el aviso no
        // bloquea la navegación y la superficie del catálogo tampoco (AC3, UX-DR-32).
        RegisterSurface(ClientSurface.Catalogo, "Catálogo", catalogSurface);

        // AC8: la Biblioteca vive en la MISMA columna que el catálogo, con sus
        // instalaciones y su estado vacío accionable (UX-DR-28).
        RegisterSurface(ClientSurface.Biblioteca, "Biblioteca", librarySurface);

        // AD-3: la superficie de Conexión ofrece los servidores que el descubrimiento
        // encontró y, cuando no hay ninguno, la acción primaria «Introducir dirección
        // manualmente» (AC3, UX-DR-30).
        RegisterSurface(ClientSurface.Conexion, "Conexión", connectionSurface);

        RegisterSurface(ClientSurface.Configuracion, "Configuración y diagnóstico", settingsPanel);

        // La columna de la izquierda es la NAVEGACION, y el aviso de sincronización vive
        // ENCIMA del contenido: el aviso no bloquea la navegación (AC3, UX-DR-32).
        layout.Children.Add(surfaceArea);

        // AC1: la ventana tiene TRES COLUMNAS. La navegación a la izquierda, el
        // contenido en el centro y el panel PERSISTENTE de la conexión a la derecha, con
        // una anchura FIJA: el panel no se colapsa, no se esconde en un menú ni en un
        // modal (AC1, UX-DR-11, UX-DR-29).
        Content = BuildColumns(navigation, layout, statusPanel);

        _shell.Navigated += (_, _) => ShowSurface(_shell.Current);

        KeyDown += OnShellKeyDown;

        ShowSurface(_shell.Current);
    }

    /// <summary>
    /// Shows ONE surface at a time and highlights the one in front, para que la columna
    /// diga donde esta el jugador (AC1, UX-DR-9).
    /// </summary>
    /// <param name="surface">The surface to show.</param>
    private void ShowSurface(ClientSurface surface)
    {
        foreach (var pair in _surfaceHosts)
        {
            pair.Value.IsVisible = pair.Key == surface;
        }

        foreach (var pair in _navigationButtons)
        {
            var active = pair.Key == surface;

            pair.Value.Background = active
                ? new SolidColorBrush(DesignTokens.AccentAction)
                : Brushes.Transparent;

            pair.Value.Foreground = active
                ? new SolidColorBrush(DesignTokens.AccentActionInk)
                : new SolidColorBrush(DesignTokens.InkSecondary);
        }
    }

    /// <summary>
    /// The KEYBOARD primitives of the navigation (AC3, UX-DR-11).
    ///
    /// <para>
    /// `Ctrl+1`…`Ctrl+4` y `Ctrl+,` saltan de superficie, `Ctrl+F` enfoca la busqueda
    /// del catalogo desde cualquier superficie y `Esc` la limpia cuando no hay ningun
    /// dialogo encima.
    /// </para>
    /// </summary>
    /// <param name="sender">Sender of the event.</param>
    /// <param name="eventArgs">Arguments of the event.</param>
    private void OnShellKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        // Un dialogo que ya ha resuelto el `Esc` no llega aqui: el foco esta dentro del
        // dialogo y su manejador marca el evento como resuelto (AC3).
        if (eventArgs.Handled)
        {
            return;
        }

        var target = ShellViewModel.SurfaceForShortcut(eventArgs.Key, eventArgs.KeyModifiers);

        if (target is not null)
        {
            _shell.Navigate(target.Value);
            eventArgs.Handled = true;

            return;
        }

        // `Ctrl+F` enfoca la busqueda del catalogo desde CUALQUIER superficie (AC3).
        if (eventArgs.Key == Key.F && eventArgs.KeyModifiers == KeyModifiers.Control)
        {
            _catalogSurface?.FocusSearch();
            eventArgs.Handled = true;

            return;
        }

        // `Esc` limpia la busqueda cuando no hay ningun dialogo encima (AC3).
        if (eventArgs.Key == Key.Escape && eventArgs.KeyModifiers == KeyModifiers.None)
        {
            _catalogSurface?.ClearSearch();
            eventArgs.Handled = true;
        }
    }

    /// <summary>
    /// Builds the two columns of the window: the content and the persistent panel of the
    /// connection (AC1).
    /// </summary>
    /// <param name="content">Content of the left column.</param>
    /// <param name="statusPanel">
    /// Persistent panel of the connection, or <c>null</c> when the window is built without
    /// it.
    /// </param>
    /// <returns>The grid of the window.</returns>
    private static Grid BuildColumns(
        Control navigation,
        Control content,
        ConnectionStatusPanel? statusPanel)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
        };

        var navigationColumn = new Border
        {
            Name = NavigationColumnName,
            Width = NavigationColumnWidth,
            Padding = new Thickness(DesignTokens.Spacing2),
            BorderBrush = new SolidColorBrush(DesignTokens.BorderHairline),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = navigation,
        };

        Grid.SetColumn(navigationColumn, 0);

        grid.Children.Add(navigationColumn);

        Grid.SetColumn(content, 1);

        grid.Children.Add(content);

        if (statusPanel is not null)
        {
            var column = new Border
            {
                Width = StatusPanelWidth,
                Padding = new Thickness(0),
                BorderBrush = new SolidColorBrush(DesignTokens.BorderHairline),
                BorderThickness = new Thickness(1, 0, 0, 0),
                Child = statusPanel,
            };

            Grid.SetColumn(column, 2);

            grid.Children.Add(column);
        }

        return grid;
    }

    /// <summary>
    /// Fixed width of the column of the persistent panel of the connection (AC1).
    /// </summary>
    public const double StatusPanelWidth = 320;

    /// <summary>
    /// Fixed width of the column of the NAVIGATION (AC1, UX-DR-9).
    /// </summary>
    public const double NavigationColumnWidth = 200;

    /// <summary>
    /// The name of the column of the navigation, para que las pruebas la encuentren sin
    /// depender del texto (AC1).
    /// </summary>
    public const string NavigationColumnName = "ClientNavigation";

    /// <summary>
    /// The name of the button of a surface in the column of the navigation, para que las
    /// pruebas la pulsen sin depender del texto (AC1, AC2).
    /// </summary>
    /// <param name="surface">The surface of the button.</param>
    /// <returns>The name of the button.</returns>
    public static string NavigationButtonName(ClientSurface surface) => $"Nav{surface}";

    /// <summary>
    /// The persistent panel of the connection, or <c>null</c> when the window is built
    /// without it (AC1).
    /// </summary>
    public ConnectionStatusPanel? StatusPanel => _statusPanel;

    /// <summary>
    /// The surface of the Biblioteca of the player, or <c>null</c> when the window is
    /// built without it (AC8).
    /// </summary>
    public LibrarySurface? LibrarySurface => _librarySurface;

    /// <summary>
    /// The VISOR de diagnóstico del cliente, or <c>null</c> when the window is built
    /// without it (AC1, NFR-7).
    /// </summary>
    public DiagnosticsLogView? DiagnosticsView => _diagnosticsView;

    /// <summary>
    /// The notice of the pending synchronization of the catalog, or <c>null</c> when the
    /// window is built without it (AC3, UX-DR-32).
    /// </summary>
    public CatalogSyncBanner? SyncBanner => _banner;

    /// <summary>
    /// Shows the state of the synchronization of the catalog in the notice of the window.
    ///
    /// <para>
    /// Un catálogo al día oculta el aviso y un catálogo pendiente lo muestra. El aviso NO
    /// bloquea la navegación en ninguno de los dos casos (AC3, UX-DR-32).
    /// </para>
    /// </summary>
    /// <param name="state">State of the synchronization of the catalog.</param>
    public void ShowSyncState(CatalogSyncState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        // La superficie del catálogo filtra sobre el snapshot que el cliente aplicó, así
        // que recibe el estado de la sincronización aunque el aviso no exista (AC3).
        _catalogSurface?.ApplySyncState(state);

        if (_banner is null)
        {
            return;
        }

        if (state.Synchronized)
        {
            _banner.Hide();

            return;
        }

        _banner.Show(state);
    }
}
