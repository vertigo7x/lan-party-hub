using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LANPartyHub.Client.Application;
using LANPartyHub.Client.Application.CatalogSync;
using LANPartyHub.Client.Presentation.Catalog;
using LANPartyHub.Client.Presentation.Connection;

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
/// </summary>
public sealed class MainWindow : Window
{
    private readonly CatalogSyncBanner? _banner;
    private readonly CatalogSurface? _catalogSurface;

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
    public MainWindow(
        ClientSettings? settings = null,
        Func<Task>? onSynchronizeNow = null,
        CatalogSurface? catalogSurface = null,
        ConnectionSurface? connectionSurface = null)
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

        var content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 8,
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

        // La superficie del catálogo vive DEBAJO del aviso de sincronización: el aviso no
        // bloquea la navegación y la superficie del catálogo tampoco (AC3, UX-DR-32).
        if (catalogSurface is not null)
        {
            layout.Children.Add(catalogSurface);
        }

        // AD-3: la superficie de Conexión ofrece los servidores que el descubrimiento
        // encontró y, cuando no hay ninguno, la acción primaria «Introducir dirección
        // manualmente» (AC3, UX-DR-30).
        if (connectionSurface is not null)
        {
            layout.Children.Add(connectionSurface);
        }

        Content = layout;
    }

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
