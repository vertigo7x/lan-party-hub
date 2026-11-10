using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using LANPartyHub.Server.Application.Catalog;
using LANPartyHub.Server.Application.Clients;
using LANPartyHub.Server.Application.Packages;
using LANPartyHub.Server.Application.Ports;
using LANPartyHub.Server.Host;
using LANPartyHub.Server.Presentation.Catalog;
using LANPartyHub.Server.Presentation.Clients;
using LANPartyHub.Server.Presentation.Packages;
using LANPartyHub.Server.Presentation.Trust;
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
            var listClients = scope.ServiceProvider.GetRequiredService<ListClients>();

            // AD-27: el ancla de confianza la publica el adaptador que posee la clave del
            // servidor, y la dirección es la que el organizador tiene que escribir en el
            // QR para que el jugador escanee el par completo. La dirección se compone
            // con el nombre del host y el puerto HTTP de la configuración efectiva.
            var signer = scope.ServiceProvider.GetRequiredService<IManifestSigner>();
            var configuration = scope.ServiceProvider.GetRequiredService<ServerConfiguration>();

            var trustAnchor = new TrustAnchorViewModel(
                signer.GetTrustAnchor(),
                $"{Environment.MachineName}:{configuration.HttpPort}");

            var window = new CatalogWindow(new CatalogViewModel(), registerGame, modifyGame, listGames, trustAnchor);

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

            // AD-29: la sección de clientes es la vista de organización de los equipos
            // de la LAN, y la alcanza el organizador desde la navegación.
            window.ClientsRequested += async (_, _) =>
            {
                // AD-6, FR-4: la fila INCOMPATIBLE se decide con el rango de versiones
                // que el servidor REALMENTE admite, así que la vista recibe la política
                // efectiva del servidor y no la inventa (AD-6, AD-15).
                var clients = new ClientsWindow(
                    new ClientsViewModel { MinimumProtocolVersion = configuration.MinimumProtocolVersion },
                    listClients);

                await clients.ShowDialog(window).ConfigureAwait(true);
            };

            desktop.ShutdownRequested += (_, _) => scope.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
