using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using LANPartyHub.Client.Application;
using LANPartyHub.Client.Application.CatalogSync;
using LANPartyHub.Client.Application.Connection;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Application.Trust;
using LANPartyHub.Client.Host;
using LANPartyHub.Client.Presentation;
using LANPartyHub.Client.Presentation.Catalog;
using LANPartyHub.Client.Presentation.Connection;
using LANPartyHub.Client.Presentation.Downloads;
using LANPartyHub.Client.Presentation.Status;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

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
    private readonly DownloadComposition? _downloads;
    private readonly IUiThread _uiThread;
    private ConnectionSurfaceViewModel? _connectionViewModel;
    private ServerAvailabilityMonitor? _availabilityMonitor;

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
    /// Composition of the discovery of the event server, or <c>null</c> when the client
    /// was not able to open it.
    /// </param>
    /// <param name="downloads">
    /// Composition of the downloads, or <c>null</c> when the client was not able to open
    /// it.
    /// </param>
    public App(
        ClientSettings? settings,
        string? startupError,
        CatalogSyncComposition? catalogSync = null,
        ConnectionComposition? connection = null,
        IUiThread? uiThread = null,
        DownloadComposition? downloads = null)
    {
        _settings = settings;
        _startupError = startupError;
        _catalogSync = catalogSync;
        _connection = connection;
        _downloads = downloads;

        // El hilo de la interfaz encola las notificaciones que llegan desde el grupo de
        // hilos: el supervisor de la disponibilidad y la reconexión publican desde ahí, y
        // un control de Avalonia SOLO se toca desde el hilo que lo posee (AC3, AC4, AC5).
        _uiThread = uiThread ?? new AvaloniaUiThread();
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
            // manualmente» (AC3, UX-DR-30). La vía manual es PERMANENTE y equivalente: el
            // campo de la dirección vive en la superficie en todos los estados y «Enter»
            // confirma la conexión (AC1, AC2).
            var connectionViewModel = new ConnectionSurfaceViewModel();
            _connectionViewModel = connectionViewModel;

            var connectionSurface = new ConnectionSurface(
                connectionViewModel,
                ConnectManuallyAsync,
                FixTrustAnchorAsync);

            // El descubrimiento NO bloquea el arranque: la superficie se abre de inmediato
            // y el resultado llega cuando la búsqueda termina (AC1, AC3).
            _ = DiscoverServersAsync(connectionViewModel);

            // AC1: el panel de la conexión es PERSISTENTE y vive en la columna derecha
            // fija de la ventana. No se colapsa, no se esconde en un menú ni en un modal
            // (AC1, UX-DR-11).
            var statusViewModel = new ConnectionStatusPanelViewModel();

            // AC2, AC3: el progreso de la descarga vive en el MISMO panel, con su barra y
            // su texto, y los dos salen de UNA SOLA fuente: la barra nunca contradice al
            // porcentaje (UX-DR-5, UX-DR-13).
            //
            // La superficie «Descargas» de la navegación es de otra historia (UX-DR-23),
            // así que aquí la descarga se muestra en el panel que ya es persistente.
            var downloadViewModel = new DownloadProgressViewModel();
            var downloadControl = new DownloadProgressControl(downloadViewModel);

            var statusPanel = new ConnectionStatusPanel(statusViewModel, downloadControl);

            // A failure of the startup is shown in a window of its own: the client is a
            // graphical application and the player would never read a message that only
            // goes to the log.
            var window = _startupError is null
                ? (Window)new MainWindow(
                    _settings,
                    SynchronizeNowAsync,
                    catalogSurface,
                    connectionSurface,
                    statusPanel)
                : new StartupErrorWindow(_startupError);

            // El overlay de la confirmación de la cancelación vive DENTRO de la ventana,
            // así que las acciones se cablean cuando la ventana ya existe
            // (UX-DR-16, UX-DR-22).
            WireDownloadActions(downloadViewModel, downloadControl, window);

            desktop.MainWindow = window;

            // El aviso de la sincronización pendiente se actualiza con el estado que
            // publica la composición, y NO bloquea la navegación (AC3, UX-DR-32).
            //
            // La publicación llega desde el grupo de hilos cuando la reconexión resincroniza
            // el catálogo, así que el aviso se ENCOLA en el hilo de la interfaz: tocar el
            // banner desde el hilo que no lo posee lo deja sin repintar para siempre
            // (AC3, AC4, AC5).
            if (_catalogSync is not null && window is MainWindow mainWindow)
            {
                _catalogSync.StateChanged += state =>
                    _uiThread.Post(() => mainWindow.ShowSyncState(state));
            }

            // AD-6, FR-4: el servidor puede rechazar la versión del cliente con un 426.
            // La sincronización publica ese diagnóstico y la superficie de Conexión lo
            // muestra con las dos versiones y el siguiente paso (AD-6, UX-DR-31).
            //
            // El diagnóstico se publica desde el grupo de hilos cuando la sincronización
            // la resuelve la reconexión, así que se ENCOLA en el hilo de la interfaz antes
            // de tocar la superficie (AC3, AC4, AC5).
            if (_catalogSync is not null)
            {
                _catalogSync.Service.VersionIncompatible += failure =>
                    _uiThread.Post(() =>
                        connectionViewModel.ApplyVersionIncompatibility(
                            failure.ClientVersion,
                            failure.ServerVersion));
            }

            // AC3, AC4, AC5: el supervisor de la disponibilidad vigila el servidor y
            // publica su estado al panel PERSISTENTE, a la superficie de Conexión y al
            // aviso de la sincronización. La caída del servidor NO interrumpe al jugador:
            // no hay ningún modal (AC3, UX-DR-29).
            StartAvailabilitySupervision(statusViewModel, connectionViewModel, ResolveBanner(window));
        }

        base.OnFrameworkInitializationCompleted();
    }

/// <summary>
    /// Wires the actions of the progress of the download: «Pausar», «Reanudar» y
    /// «Cancelar» (AC3, AC4, UX-DR-6, UX-DR-22).
    ///
    /// <para>
    /// Cancelar pide CONFIRMACIÓN nombrando lo que se pierde, y la confirmación es un
    /// overlay <c>dialog-surface</c> DENTRO de la ventana: no se abre una ventana nueva,
    /// porque un diálogo nunca se apila sobre otro (UX-DR-16, UX-DR-22).
    /// </para>
    /// </summary>
    /// <param name="viewModel">View model of the progress of the download.</param>
    /// <param name="control">Control of the progress of the download.</param>
    /// <param name="window">Window of the player, which hosts the overlay de confirmación.</param>
    private void WireDownloadActions(
        DownloadProgressViewModel viewModel,
        DownloadProgressControl control,
        Window window)
    {
        if (_downloads is null)
        {
            return;
        }

        var coordinator = _downloads.Coordinator;

        // AC2, UX-DR-13: el coordinador publica el estado y el progreso, y la publicación
        // llega desde el grupo de hilos: los controles de Avalonia SOLO se tocan desde el
        // hilo que los posee, así que la notificación se encola.
        coordinator.Changed += handle =>
            _uiThread.Post(() => viewModel.Apply(handle));

        control.PauseButton.Click += async (_, _) =>
        {
            var handle = viewModel.Handle;

            if (handle is null)
            {
                return;
            }

            await coordinator.PauseAsync(handle).ConfigureAwait(true);
        };

        control.ResumeButton.Click += async (_, _) =>
        {
            var handle = viewModel.Handle;

            if (handle is null)
            {
                return;
            }

            await coordinator.ResumeAsync(handle, CancellationToken.None).ConfigureAwait(true);
        };

        // AC4, UX-DR-22: la cancelación destruye trabajo no recuperable, así que la
        // interfaz PIDE CONFIRMACIÓN y nombra lo que se pierde antes de cancelar.
        control.CancelButton.Click += (_, _) => ShowCancelConfirmation(viewModel, coordinator, window);
    }

    /// <summary>
    /// Shows the overlay de confirmación de la cancelación (AC4, UX-DR-22).
    ///
    /// <param name="viewModel">View model of the progress of the download.</param>
    /// <param name="coordinator">Coordinator of the downloads.</param>
    /// <param name="window">Window of the player, which hosts the overlay.</param>
    private static void ShowCancelConfirmation(
        DownloadProgressViewModel viewModel,
        Application.Downloads.DownloadCoordinator coordinator,
        Window window)
    {
        var handle = viewModel.Handle;

        if (handle is null)
        {
            return;
        }

        var dialog = new DownloadCancelDialog(viewModel);

        // El overlay vive dentro de la ventana y no abre una ventana nueva: el cliente es
        // una aplicación de escritorio con una sola ventana y un diálogo nunca se apila
        // sobre otro (UX-DR-16, UX-DR-23).
        var overlay = new Grid();

        void CloseOverlay()
        {
            if (overlay.Parent is Panel parent)
            {
                parent.Children.Remove(overlay);
            }
        }

        dialog.KeepButton.Click += (_, _) => CloseOverlay();

        dialog.ConfirmButton.Click += async (_, _) =>
        {
            CloseOverlay();

            await coordinator.CancelAsync(handle, CancellationToken.None).ConfigureAwait(true);
        };

        overlay.Children.Add(dialog);

        if (window.Content is Panel panel)
        {
            panel.Children.Add(overlay);
        }
    }

/// <summary>
    /// Starts the supervision of the availability of the event server and wires its state to
    /// the panel, to the surface of the connection and to the notice of the synchronization
    /// (AC3, AC4, AC5).
    /// </summary>
    /// <param name="statusViewModel">View model of the persistent panel of the connection.</param>
    /// <param name="connectionViewModel">View model of the surface of the connection.</param>
    /// <param name="banner">Notice of the pending synchronization, when the window has one.</param>
    private void StartAvailabilitySupervision(
        ConnectionStatusPanelViewModel statusViewModel,
        ConnectionSurfaceViewModel connectionViewModel,
        CatalogSyncBanner? banner)
    {
        if (_connection is null)
        {
            return;
        }

        var monitor = new ServerAvailabilityMonitor(
            _connection.VersionProbe,
            _catalogSync,
            NullLogger<ServerAvailabilityMonitor>.Instance);

        _availabilityMonitor = monitor;

        monitor.AvailabilityChanged += availability =>
        {
            // El supervisor publica su estado desde el GRUPO DE HILOS: el bucle de la
            // supervisión va con `ConfigureAwait(false)`. Los controles de Avalonia SOLO
            // se tocan desde el hilo que los posee, así que TODA la notificación se
            // encola en el hilo de la interfaz antes de tocar un control. Sin esto, el
            // `SetValue` del punto de presencia lanza `InvalidOperationException`, la
            // supervisión queda fallida y el panel NO se repinta ni reconecta nunca
            // (AC3, AC4, AC5).
            _uiThread.Post(() =>
            {
                // El panel PERSISTENTE muestra el punto de presencia, el nombre del
                // servidor, la dirección en uso, la versión y la última operación
                // (AC1, UX-DR-11).
                statusViewModel.ApplyAvailability(availability);

                // Las acciones que REQUIEREN el servidor quedan deshabilitadas con su
                // motivo visible (AC4, UX-DR-29).
                connectionViewModel.ApplyAvailability(availability);

                if (banner is null)
                {
                    return;
                }

                if (availability.Available)
                {
                    banner.Enable();

                    return;
                }

                banner.DisableWithReason(CatalogSyncBanner.ServerUnavailableReasonText);
            });
        };

        var endpoint = ResolveEndpoint();

        if (endpoint is null)
        {
            return;
        }

        _ = monitor.StartAsync(endpoint, CancellationToken.None);
    }

    /// <summary>
    /// Resolves the endpoint of the event server that the client is using, or <c>null</c>
    /// when the client does not know any (AC1).
    /// </summary>
    /// <returns>The endpoint of the event server, or <c>null</c>.</returns>
    private ServerEndpoint? ResolveEndpoint()
    {
        if (_connectionViewModel?.EndpointInUse is { } inUse)
        {
            return inUse;
        }

        var address = _catalogSync?.ServerAddress;

        if (address is null)
        {
            return null;
        }

        return ServerEndpointParser.TryParse(
            $"{address.Host}:{address.Port}",
            out var endpoint,
            out _)
            ? endpoint
            : null;
    }

    /// <summary>
    /// The notice of the pending synchronization of the catalog of the window, when the
    /// window carries one (AC3, UX-DR-32).
    /// </summary>
    /// <param name="window">Window of the player.</param>
    /// <returns>The notice, or <c>null</c> when the window does not carry one.</returns>
    private static CatalogSyncBanner? ResolveBanner(Window window) =>
        window is MainWindow mainWindow
            ? mainWindow.SyncBanner
            : null;

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
    /// Connects with the server of the event at the address that the player typed and hands
    /// the result to the surface of the connection (AC1, AC3).
    ///
    /// <para>
    /// The connection NEVER kills the client: an address that does not answer ends the
    /// attempt as disconnected and the surface shows the three part message of the error
    /// (AC3, UX-DR-36).
    /// </para>
    /// </summary>
    /// <param name="address">Text of the address, in the form <c>host:puerto</c>.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the connection has been attempted.</returns>
    private async Task ConnectManuallyAsync(string? address, CancellationToken cancellationToken)
    {
        if (_connection is null)
        {
            return;
        }

        try
        {
            var attempt = await _connection.ConnectionService
                .ConnectAsync(address, cancellationToken)
                .ConfigureAwait(true);

            _connectionViewModel?.ApplyConnection(attempt);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // La conexión no puede impedir que el jugador vuelva a intentarlo (AC3).
        }
    }

    /// <summary>
    /// Fixes the ancla de confianza con el fingerprint que el jugador recibió FUERA de
    /// banda (AC2, AC3, AD-27).
    ///
    /// <para>
    /// AD-27: la secuencia es la del caso de uso. El cliente obtiene la clave que
    /// presenta el servidor, DERIVA su fingerprint, lo COMPARA con el esperado y solo
    /// persiste cuando coinciden. Cuando NO coinciden, el cliente RECHAZA y no escribe
    /// nada en el disco (AC2, AD-27).
    /// </para>
    /// <para>
    /// El fingerprint esperado NEVER viene del servidor: llega por el canal fuera de
    /// banda, que es el QR del organizador o el contraste en su pantalla (AD-27).
    /// </para>
    /// </summary>
    /// <param name="expectedFingerprint">
    /// Fingerprint as the player transported it, from the QR or from the screen of the
    /// organizer.
    /// </param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the fixation has been attempted.</returns>
    private async Task FixTrustAnchorAsync(string? expectedFingerprint, CancellationToken cancellationToken)
    {
        if (_connection is null || _connection.StateStore is null)
        {
            return;
        }

        try
        {
            var endpoint = _connectionViewModel?.EndpointInUse;

            if (endpoint is null)
            {
                // Sin servidor al que preguntar, no hay clave que derivar: la fijación
                // espera a que el jugador se conecte primero (AC2, AD-27).
                return;
            }

            var probe = await _connection.AnchorProbe
                .ProbeAsync(endpoint, TimeSpan.FromSeconds(3), cancellationToken)
                .ConfigureAwait(true);

            var useCase = new FixTrustAnchor(
                _connection.StateStore,
                TimeProvider.System,
                NullLogger<FixTrustAnchor>.Instance);

            var result = await useCase
                .ExecuteAsync(expectedFingerprint, probe.Anchor, cancellationToken)
                .ConfigureAwait(true);

            if (result.IsFixed)
            {
                _connectionViewModel?.ApplyAnchorVerified();

                return;
            }

            // AD-27: el rechazo precede a persistir y el `code` del error es el de
            // «fingerprint de ancla no coincide», DISTINTO del de «firma inválida»
            // (AC2, AD-27).
            if (result.PresentedFingerprint is not null && result.FixedFingerprint is not null)
            {
                _connectionViewModel?.ApplyAnchorMismatch(
                    result.PresentedFingerprint,
                    result.FixedFingerprint);

                return;
            }

            _connectionViewModel?.ApplyAnchorMismatch(
                result.PresentedFingerprint ?? string.Empty,
                result.FixedFingerprint ?? string.Empty);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // La fijación no puede impedir que el jugador vuelva a intentarlo (AC3).
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
