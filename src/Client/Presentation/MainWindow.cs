using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LANPartyHub.Client.Application;
using LANPartyHub.Client.Application.CatalogSync;
using LANPartyHub.Client.Presentation.Catalog;
using LANPartyHub.Client.Presentation.Connection;
using LANPartyHub.Client.Presentation.Design;
using LANPartyHub.Client.Presentation.Diagnostics;
using LANPartyHub.Client.Presentation.Library;
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

        var save = new Button { Content = "Guardar" };
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

        var layout = new StackPanel { Spacing = 0 };

        if (banner is not null)
        {
            layout.Children.Add(banner);
        }

        layout.Children.Add(content);

        // AC1, NFR-7: el VISOR de diagnóstico vive en la sección de «Configuración y
        // diagnóstico» de la ventana, junto al directorio de instalación, así que el
        // equipo ve los errores que su cliente registró sin salir de la aplicación.
        if (diagnosticsView is not null)
        {
            layout.Children.Add(new StackPanel
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
        if (catalogSurface is not null)
        {
            layout.Children.Add(catalogSurface);
        }

        // AC8: la Biblioteca vive en la MISMA columna que el catálogo, con sus
        // instalaciones y su estado vacío accionable (UX-DR-28).
        if (librarySurface is not null)
        {
            layout.Children.Add(librarySurface);
        }

        // AD-3: la superficie de Conexión ofrece los servidores que el descubrimiento
        // encontró y, cuando no hay ninguno, la acción primaria «Introducir dirección
        // manualmente» (AC3, UX-DR-30).
        if (connectionSurface is not null)
        {
            layout.Children.Add(connectionSurface);
        }

        // AC1: la ventana tiene DOS COLUMNAS. El contenido vive a la izquierda y el panel
        // PERSISTENTE de la conexión a la derecha, con una anchura FIJA: el panel no se
        // colapsa, no se esconde en un menú ni en un modal (AC1, UX-DR-11).
        Content = BuildColumns(layout, statusPanel);
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
    private static Grid BuildColumns(Control content, ConnectionStatusPanel? statusPanel)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
        };

        Grid.SetColumn(content, 0);

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

            Grid.SetColumn(column, 1);

            grid.Children.Add(column);
        }

        return grid;
    }

    /// <summary>
    /// Fixed width of the column of the persistent panel of the connection (AC1).
    /// </summary>
    public const double StatusPanelWidth = 320;

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
