using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using LANPartyHub.Server.Presentation.Catalog;

namespace LANPartyHub.Server.Presentation;

/// <summary>
/// Avalonia application of the event server (AD-1).
///
/// <para>
/// The server is a single deployable, so its interface lives inside <c>src/Server</c> and
/// not in a project of its own: the binary of the server carries the host HTTP and the
/// window of the organizer in the same process (AD-1).
/// </para>
/// <para>
/// The base type is fully qualified because the Application layer of the server owns a
/// namespace with the same name.
/// </para>
/// </summary>
public sealed class ServerApp : Avalonia.Application
{
    /// <summary>Creates the application of the server.</summary>
    public ServerApp()
    {
    }

    /// <inheritdoc />
    public override void Initialize() => Styles.Add(new FluentTheme());

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new CatalogWindow(new CatalogViewModel());
        }

        base.OnFrameworkInitializationCompleted();
    }
}
