using Avalonia;

namespace LANPartyHub.Client;

/// <summary>
/// Entry point of the desktop client (AD-1, AD-19).
/// </summary>
internal static class Program
{
    /// <summary>Starts the desktop lifetime of the client.</summary>
    /// <param name="args">Command line arguments of the application.</param>
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    /// <summary>Builds the Avalonia application builder of the client.</summary>
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect();
}
