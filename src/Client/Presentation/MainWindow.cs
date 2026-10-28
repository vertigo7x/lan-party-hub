using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LANPartyHub.Client.Application;

namespace LANPartyHub.Client.Presentation;

/// <summary>
/// Main window of the client. Story 1.1 only declares the shell: the surfaces of the
/// catalog, the downloads and the library arrive with the following stories.
/// <para>
/// The window carries the settings surface of the installation root, which is the only
/// directory that the rule of extraction confines (AD-9, AC4). The rest of the settings
/// surface (the connection, the theme and the log of diagnostics) belongs to Epic 11.
/// </para>
/// </summary>
public sealed class MainWindow : Window
{
    /// <summary>Creates the main window of the client.</summary>
    /// <param name="settings">
    /// Settings of the client, or <c>null</c> when the window is built without them.
    /// </param>
    public MainWindow(ClientSettings? settings = null)
    {
        Title = "LAN Party Hub";
        Width = 640;
        Height = 360;

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

        Content = new StackPanel
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
    }
}
