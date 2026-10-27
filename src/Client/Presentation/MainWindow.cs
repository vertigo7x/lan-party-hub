using Avalonia.Controls;

namespace LANPartyHub.Client.Presentation;

/// <summary>
/// Main window of the client. Story 1.1 only declares the shell: the surfaces of the
/// catalog, the downloads and the library arrive with the following stories.
/// </summary>
public sealed class MainWindow : Window
{
    /// <summary>Creates the main window of the client.</summary>
    public MainWindow()
    {
        Title = "LAN Party Hub";
        Content = new TextBlock { Text = "LAN Party Hub" };
    }
}
