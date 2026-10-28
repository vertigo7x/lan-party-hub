using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace LANPartyHub.Client.Presentation;

/// <summary>
/// Window that reports a failure of the startup of the client (AD-15).
///
/// <para>
/// The client is a graphical application: launched from the desktop it has no console, so a
/// failure that only goes to the log leaves the player without a clue of what happened.
/// </para>
/// </summary>
public sealed class StartupErrorWindow : Window
{
    /// <summary>Creates the window that reports the failure.</summary>
    /// <param name="message">Message that explains what failed and what to do now.</param>
    public StartupErrorWindow(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        Title = "LAN Party Hub";
        Width = 640;
        Height = 260;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = "El cliente no pudo arrancar",
                    FontSize = 18,
                    FontWeight = FontWeight.Bold,
                },
                new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap,
                },
            },
        };
    }
}
