using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using LANPartyHub.Server.Application.Catalog;
using LANPartyHub.Server.Application.Packages;
using LANPartyHub.Server.Presentation.Catalog;
using LANPartyHub.Server.Presentation.Packages;
using Microsoft.Extensions.DependencyInjection;

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
/// <para>
/// The use cases of the catalog are scoped, because they depend on the scoped context of
/// the database, so the window is opened inside a scope of the container of the host and
/// that scope lives as long as the window lives.
/// </para>
/// </summary>
public sealed class ServerApp : Avalonia.Application
{
    private readonly IServiceProvider _services;

    /// <summary>Creates the application of the server.</summary>
    /// <param name="services">Container of the host, with the use cases of the catalog.</param>
    public ServerApp(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        _services = services;
    }

    /// <inheritdoc />
    public override void Initialize() => Styles.Add(new FluentTheme());

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // The scope of the container of the host is created here and lives as long as
            // the window of the organizer, so the use cases of the catalog always have the
            // scoped context of the database that they need (AD-1).
            var scope = _services.CreateScope();

            var registerGame = scope.ServiceProvider.GetRequiredService<RegisterGame>();
            var modifyGame = scope.ServiceProvider.GetRequiredService<ModifyGame>();
            var listGames = scope.ServiceProvider.GetRequiredService<ListGames>();
            var publishPackage = scope.ServiceProvider.GetRequiredService<PublishPackage>();
            var listPackages = scope.ServiceProvider.GetRequiredService<ListPackages>();
            var withdrawGame = scope.ServiceProvider.GetRequiredService<WithdrawGame>();

            var window = new CatalogWindow(new CatalogViewModel(), registerGame, modifyGame, listGames);

            desktop.MainWindow = window;

            // The section of the packages is reachable from the window of the catalog: the
            // organizer publishes the package of a videogame that is already in the
            // catalog (AD-1, AC1).
            window.Opened += async (_, _) =>
            {
                var packages = new PackagesWindow(
                    new PackagesViewModel(),
                    publishPackage,
                    listPackages,
                    withdrawGame);

                await packages.ShowDialog(window).ConfigureAwait(true);
            };

            desktop.ShutdownRequested += (_, _) => scope.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
