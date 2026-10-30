using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using LANPartyHub.Client.Application;
using LANPartyHub.Client.Application.CatalogSync;
using LANPartyHub.Client.Application.Connection;
using LANPartyHub.Client.Host;
using LANPartyHub.Client.Presentation;
using LANPartyHub.Client.Presentation.Catalog;
using LANPartyHub.Client.Presentation.Connection;

namespace LANPartyHub.Client;

/// <summary>
/// Avalonia application of the client (AD-1). The window and the view models live in the
/// Presentation layer and the application itself only wires them.
/// <para>
/// The base type is fully qualified because the Application layer of the client owns a
/// namespace with the same name.
/// </para>
/// </summary>
public sealed class App : Avalonia.Application
{
    private readonly ClientSettings? _settings;
    private readonly string? _startupError;
    private readonly CatalogSyncComposition? _catalogSync;
    private readonly ConnectionComposition? _connection;

    /// <summary>Creates the application without settings and without a failure to report.</summary>
    public App()
        : this(null, null)
    {
    }

    /// <summary>Creates the application.</summary>
    /// <param name="settings">Settings of the client, or <c>null</c> when there are none.</param>
    /// <param name="startupError">Message of a failure of the startup, or <c>null</c> when the startup succeeded.</param>
    /// <param name="catalogSync">
    /// Composition of the synchronization of the catalog, or <c>null</c> when the client
    /// was not able to open it.
    /// </param>
    /// <param name="connection">
    /// Composition of the discovery of the event server, or <c>null</c> when the client was
    /// not able to open it.
    /// </param>
    public App(
        ClientSettings? settings,
        string? startupError,
        CatalogSyncComposition? catalogSync = null,
        ConnectionComposition? connection = null)
    {
        _settings = settings;
        _startupError = startupError;
        _catalogSync = catalogSync;
        _connection = connection;
    }

    /// <inheritdoc />
    public override void Initialize() => Styles.Add(new FluentTheme());

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // La superficie del catálogo filtra sobre el snapshot que el cliente ya
            // sincronizó, así que se construye con su propio view model y se enlaza al
            // estado de la sincronización (AC1, AC2, AC3).
            var catalogSurface = new CatalogSurface(
                new CatalogSurfaceViewModel(),
                () => _ = SynchronizeNowAsync());

            // Las instalaciones que el cliente registró son LOCALES: la ficha del
            // videojuego las lee para derivar la insignia de su estado de vista, y la
            // lectura NO va al servidor (AC3, AD-22). La lectura NO bloquea el arranque:
            // la ficha se abre igual y su insignia sale del estado que el servidor
            // publica (AC3, AD-26).
            _ = LoadInstallationsAsync(catalogSurface);

            // AD-3: la superficie de Conexión ofrece los servidores que el descubrimiento
            // encontró y, cuando no hay ninguno, la acción primaria «Introducir dirección
            // manualmente» (AC3, UX-DR-30).
            var connectionViewModel = new ConnectionSurfaceViewModel();
            var connectionSurface = new ConnectionSurface(connectionViewModel);

            // El descubrimiento NO bloquea el arranque: la superficie se abre de inmediato
            // y el resultado llega cuando la búsqueda termina (AC1, AC3).
            _ = DiscoverServersAsync(connectionViewModel);

            // A failure of the startup is shown in a window of its own: the client is a
            // graphical application and the player would never read a message that only
            // goes to the log.
            var window = _startupError is null
                ? (Window)new MainWindow(_settings, SynchronizeNowAsync, catalogSurface, connectionSurface)
                : new StartupErrorWindow(_startupError);

            desktop.MainWindow = window;

            // El aviso de la sincronización pendiente se actualiza con el estado que
            // publica la composición, y NO bloquea la navegación (AC3, UX-DR-32).
            if (_catalogSync is not null && window is MainWindow mainWindow)
            {
                _catalogSync.StateChanged += state => mainWindow.ShowSyncState(state);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Reads the installations that the client registered and hands them to the surface of
    /// the catalog (AC3).
    ///
    /// <param name="catalogSurface">Surface of the catalog of the player.</param>
    /// <returns>A task that completes when the installations have been read.</returns>
    private async Task LoadInstallationsAsync(CatalogSurface catalogSurface)
    {
        if (_catalogSync is null)
        {
            return;
        }

        try
        {
            var installations = await _catalogSync
                .LoadInstallationsAsync(CancellationToken.None)
                .ConfigureAwait(true);

            catalogSurface.ApplyInstallations(installations);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // La lectura de las instalaciones es LOCAL y su fallo NO puede impedir que el
            // jugador abra el catálogo: la ficha se abre y la insignia sale del estado que
            // el servidor publica (AC3, AD-26).
        }
    }

    /// <summary>
    /// Searches the servers of the event and hands them to the surface of the connection
    /// (AC1, AC3, AC4).
    ///
    /// <para>
    /// The search NEVER kills the client: a network that blocks the discovery ends the
    /// search as blocked and the manual address stays operative (AC4, AD-3).
    /// </para>
    /// </summary>
    /// <param name="viewModel">View model of the surface of the connection.</param>
    /// <returns>A task that completes when the search has been attempted.</returns>
    private async Task DiscoverServersAsync(ConnectionSurfaceViewModel viewModel)
    {
        if (_connection is null)
        {
            // Sin composición de descubrimiento la superficie se queda en su estado vacío,
            // que ofrece la dirección manual (AC3, AC4).
            viewModel.ApplyDiscovery(DiscoveryResult.Empty());

            return;
        }

        try
        {
            var result = await _connection.Service
                .DiscoverAsync(CancellationToken.None)
                .ConfigureAwait(true);

            viewModel.ApplyDiscovery(result);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // La búsqueda no puede impedir que el jugador use la vía manual (AC4).
            viewModel.ApplyDiscovery(DiscoveryResult.Blocked());
        }
    }

    /// <summary>
    /// Performs the synchronization that the player asks with «Sincronizar ahora»
    /// (AC3, UX-DR-32).
    /// </summary>
    /// <returns>A task that completes when the synchronization has been attempted.</returns>
    private async Task SynchronizeNowAsync()
    {
        if (_catalogSync is null)
        {
            return;
        }

        var state = await _catalogSync.Service
            .SynchronizeAsync(CancellationToken.None)
            .ConfigureAwait(true);

        _catalogSync.Publish(state);
    }
}
