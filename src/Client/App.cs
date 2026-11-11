using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using LANPartyHub.Client.Application;
using LANPartyHub.Client.Application.CatalogSync;
using LANPartyHub.Client.Application.Connection;
using LANPartyHub.Client.Application.GameLauncher;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Application.Trust;
using LANPartyHub.Client.Host;
using LANPartyHub.Client.Infrastructure;
using LANPartyHub.Client.Presentation;
using LANPartyHub.Client.Presentation.Catalog;
using LANPartyHub.Client.Presentation.Connection;
using LANPartyHub.Client.Presentation.Diagnostics;
using LANPartyHub.Client.Presentation.Downloads;
using LANPartyHub.Client.Presentation.GameLauncher;
using LANPartyHub.Client.Presentation.Install;
using LANPartyHub.Client.Presentation.Library;
using LANPartyHub.Client.Presentation.Status;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging;
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
    private readonly ClientRegistrationComposition? _registration;
    private readonly DownloadComposition? _downloads;
    private readonly IUiThread _uiThread;
    private ConnectionSurfaceViewModel? _connectionViewModel;
    private ServerAvailabilityMonitor? _availabilityMonitor;
    private InstallationComposition? _installations;
    private GameLauncherComposition? _gameLauncher;
    private CatalogSurface? _catalogSurface;
    private LibrarySurface? _librarySurface;
    private GameRunStatusViewModel? _gameRunStatus;
    private DiagnosticsLogViewModel? _diagnosticsViewModel;

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
    /// <param name="installations">
    /// Composition of the installations, or <c>null</c> when the client was not able to
    /// open it.
    /// </param>
    /// <param name="gameLauncher">
    /// Composition of the launcher of the videogames, or <c>null</c> when the client was
    /// not able to open it.
    /// </param>
    /// <param name="registration">
    /// Composition of the registration of the team against the event server, or
    /// <c>null</c> when the client was not able to open it (AD-30, AD-32).
    /// </param>
    public App(
        ClientSettings? settings,
        string? startupError,
        CatalogSyncComposition? catalogSync = null,
        ConnectionComposition? connection = null,
        IUiThread? uiThread = null,
        DownloadComposition? downloads = null,
        InstallationComposition? installations = null,
        GameLauncherComposition? gameLauncher = null,
        ClientRegistrationComposition? registration = null)
    {
        _settings = settings;
        _startupError = startupError;
        _catalogSync = catalogSync;
        _connection = connection;
        _downloads = downloads;
        _installations = installations;
        _gameLauncher = gameLauncher;
        _registration = registration;

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
            //
            // AD-19, AC1: la ficha del videojuego lleva la acción «Jugar», que lanza el
            // juego instalado a través del módulo `GameLauncher`.
            //
            // AC1, AC2: la primaria es POLIMÓRFICA —«Reparar» o «Descargar de nuevo»—
            // así que el cableado la ENRUTA por la acción que el jugador pulsó, y cada
            // una registra la intención que le corresponde.
            var catalogSurface = new CatalogSurface(
                new CatalogSurfaceViewModel(),
                () => _ = SynchronizeNowAsync(),
                gameId => _ = LaunchGameAsync(gameId),
                (gameId, primary) => _ = RecordPrimaryIntentAsync(gameId, primary),
                gameId => _ = RecordDiagnoseIntentAsync(gameId));

            _catalogSurface = catalogSurface;

            // AC1, AC2, AC8: la Biblioteca lista las instalaciones del cliente con la
            // MISMA tabla de acciones que la ficha, así que ninguna de las dos superficies
            // abre un camino de lanzamiento distinto (UX-DR-17, UX-DR-19).
            //
            // AC1, AC2: la primaria se ENRUTA igual que en la ficha, así que «Reparar» y
            // «Descargar de nuevo» registran la intención que les corresponde.
            var librarySurface = new LibrarySurface(
                new LibrarySurfaceViewModel(),
                gameId => _ = LaunchGameAsync(gameId),
                (gameId, primary) => _ = RecordPrimaryIntentAsync(gameId, primary),
                gameId => _ = RecordDiagnoseIntentAsync(gameId),
                () => ShowCatalog());

            _librarySurface = librarySurface;

            // AC1, AC4, NFR-7: el VISOR de diagnóstico muestra el registro de errores
            // persistidos de su equipo, con sus cuatro partes y con la asociación a la
            // descarga, la instalación o el lanzamiento que los produjo. El visor se
            // abre en el modo OSCURO, que es el predeterminado, y ofrece su variante
            // clara con los tokens `-light` de `DESIGN.md` (UX-DR-41, UX-DR-1).
            var diagnosticsViewModel = new DiagnosticsLogViewModel();
            _diagnosticsViewModel = diagnosticsViewModel;

            var diagnosticsView = new DiagnosticsLogView(diagnosticsViewModel);

            // La lectura del registro es LOCAL y su fallo NO puede impedir que el jugador
            // abra la ventana: el visor se abre igual y su estado vacío lo dice (AD-1,
            // AD-26, NFR-7).
            _ = LoadDiagnosticsAsync(diagnosticsViewModel);

            // Las instalaciones que el cliente registró son LOCALES: la ficha del
            // videojuego las lee para derivar la insignia de su estado de vista, y la
            // lectura NO va al servidor (AC3, AD-22). La lectura NO bloquea el arranque:
            // la ficha se abre igual y su insignia sale del estado que el servidor
            // publica (AC3, AD-26).
            _ = LoadInstallationsAsync(catalogSurface, librarySurface);

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

            // AC4, UX-DR-34: la instalación en curso se presenta en el MISMO panel
            // persistente, con su fase («Extrayendo», «Aplicando manifiesto») y su
            // porcentaje entero de los bytes REALES.
            var installViewModel = new InstallProgressViewModel();
            var installControl = new InstallProgressControl(installViewModel);

            // AC2, UX-DR-6: el estado de ejecución del juego vive en el MISMO panel
            // persistente, con «En ejecución» o «Ningún juego en ejecución». El estado
            // vive EN MEMORIA del cliente: no se persiste (AD-22, AC2).
            var gameRunStatus = new GameRunStatusViewModel();

            _gameRunStatus = gameRunStatus;

            var statusPanel = new ConnectionStatusPanel(statusViewModel, downloadControl, installControl, gameRunStatus);

            // A failure of the startup is shown in a window of its own: the client is a
            // graphical application and the player would never read a message that only
            // goes to the log.
            var window = _startupError is null
                ? (Window)new MainWindow(
                    _settings,
                    SynchronizeNowAsync,
                    catalogSurface,
                    connectionSurface,
                    statusPanel,
                    librarySurface,
                    diagnosticsView)
                : new StartupErrorWindow(_startupError);

            // El overlay de la confirmación de la cancelación vive DENTRO de la ventana,
            // así que las acciones se cablean cuando la ventana ya existe
            // (UX-DR-16, UX-DR-22).
            WireDownloadActions(downloadViewModel, downloadControl, window);

        // AC2, AC4: la confirmación de lo privilegiado y el progreso de la
        // instalación se cablean cuando la ventana ya existe, porque el overlay vive
        // DENTRO de la ventana (UX-DR-16, UX-DR-37).
        WireInstallActions(installViewModel, installControl, window);

        // AC2: el presentador de la confirmación se fija AHORA, cuando la ventana ya
        // existe: el overlay de la confirmación vive dentro de la ventana (UX-DR-16).
        if (_installations?.Confirmation is DialogPrivilegedOperationConfirmation confirmation)
        {
            confirmation.SetPresenter(dialog => ShowPrivilegedConfirmation(dialog, window));
        }


            desktop.MainWindow = window;

            // AD-11, AC2, SM-2: las descargas que sobrevivieron al último reinicio se
            // RECUPERAN cuando la ventana ya está cableada, así que la operación se
            // presenta como «Reanudando desde 42 % · 3,2 GB de 7,6 GB» con el offset
            // persistido (UX-DR-6, UX-DR-45).
            _ = RecoverDownloadsAsync(downloadViewModel);

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
    /// Wires the actions of the progress of the installation: la confirmación de lo
    /// privilegiado y el reintento de una instalación fallida (AC2, AC5, UX-DR-34,
    /// UX-DR-37).
    ///
    /// <para>
    /// El overlay de la confirmación de lo privilegiado es un <c>dialog-surface</c> de UN
    /// SOLO NIVEL que vive DENTRO de la ventana: no se abre una ventana nueva, porque un
    /// diálogo nunca se apila sobre otro (UX-DR-16, UX-DR-37).
    /// </para>
    /// <para>
    /// <c>Esc</c> cancela la acción NO destructiva y el foco vuelve al elemento
    /// INVOCADOR (UX-DR-37).
    /// </para>
    /// </summary>
    /// <param name="viewModel">View model of the progress of the installation.</param>
    /// <param name="control">Control of the progress of the installation.</param>
    /// <param name="window">Window of the player, which hosts the overlay de confirmación.</param>
    private void WireInstallActions(
        InstallProgressViewModel viewModel,
        InstallProgressControl control,
        Window window)
    {
        if (_installations is null)
        {
            return;
        }

        var coordinator = _installations.Coordinator;

        // AC4, UX-DR-13: el coordinador publica el progreso desde el grupo de hilos, así
        // que la notificación se ENCOLA en el hilo de la interfaz antes de tocar un
        // control de Avalonia.
        coordinator.ProgressChanged += progress =>
            _uiThread.Post(() => viewModel.Apply(progress));

        // AC5, UX-DR-34: «Reintentar» es una operación NUEVA sobre el paquete validado, y
        // NO reinstala el cliente (NFR-9).
        control.RetryButton.Click += async (_, _) =>
        {
            var failed = coordinator.FailedOperation;

            if (failed is null)
            {
                return;
            }

            var result = await coordinator
                .RetryAsync(failed.GameId, failed.PackageId, failed.Version, CancellationToken.None)
                .ConfigureAwait(true);

            if (result.IsRejected)
            {
                _uiThread.Post(() => viewModel.ApplyError(result.Error?.Code));

                return;
            }

            _uiThread.Post(() => viewModel.ApplyError(null));
        };
    }

    /// <summary>
    /// Launches the videogame of a game with the module <c>GameLauncher</c> (AD-19, AC1,
    /// AC2).
    ///
    /// <para>
    /// El juego se lanza como proceso PROPIO: se ejecuta FUERA de la ventana de LAN Party
    /// Hub, el cliente queda en segundo plano y el panel pasa a «En ejecución» (AC3,
    /// UX-DR-43).
    /// </para>
    /// <para>
    /// Un lanzamiento RECHAZADO no deja ningún proceso en marcha y su error se presenta
    /// junto a la acción «Jugar», con su ruta exacta cuando el problema es de ruta (AC1,
    /// AD-15).
    /// </para>
    /// </summary>
    /// <param name="gameId">Identifier of the videogame to launch.</param>
    private async Task LaunchGameAsync(string gameId)
    {
        if (_gameLauncher is null)
        {
            return;
        }

        var outcome = await _gameLauncher.Service
            .LaunchAsync(gameId, CancellationToken.None)
            .ConfigureAwait(true);

        if (outcome.IsRejected)
        {
            _uiThread.Post(() => _catalogSurface?.ApplyLaunchError(outcome.Error?.Code));

            return;
        }

        // AC2, UX-DR-6: el panel pasa a «En ejecución» y el estado vive EN MEMORIA del
        // cliente: no se persiste (AD-22, AC2).
        _uiThread.Post(() => _gameRunStatus?.ApplyLaunch(outcome.Handle!, gameId));
    }

    /// <summary>
    /// Recovers the downloads that survived the last restart of the machine
    /// (AC2, AD-11, AD-20, SM-2).
    ///
    /// <para>
    /// La recuperación NO puede impedir que el cliente arranque: si el servidor no está,
    /// la operación queda persistida y se reintenta en el siguiente arranque, así que el
    /// offset no se pierde (AD-11).
    /// </para>
    /// <para>
    /// AD-6, AC5: un cliente INCOMPATIBLE no reanuda la descarga y recibe el diagnóstico
    /// de incompatibilidad de versión. La incompatibilidad NO es un estado: lo que el
    /// jugador ve es el texto con las dos versiones y el siguiente paso (AD-27,
    /// UX-DR-31).
    /// </para>
    /// <para>
    /// La operación recuperada se presenta con la microcopy «Reanudando desde …», que NO
    /// es un estado nuevo: el estado de la operación sigue siendo el que la máquina
    /// declara (AD-10, UX-DR-45).
    /// </para>
    /// </summary>
    /// <param name="viewModel">View model of the progress of the download.</param>
    private async Task RecoverDownloadsAsync(DownloadProgressViewModel viewModel)
    {
        if (_downloads is null)
        {
            return;
        }

        try
        {
            var endpoint = ResolveEndpoint();

            if (endpoint is not null)
            {
                var gate = await _downloads.Coordinator
                    .CheckCompatibilityAsync(endpoint, CancellationToken.None)
                    .ConfigureAwait(true);

                if (gate.IsIncompatible)
                {
                    // El cliente incompatible NO reanuda: la operación sigue persistida y
                    // el diagnóstico se publica en la superficie de Conexión, que es
                    // donde vive el diagnóstico de la versión (AD-6, AC5).
                    _connectionViewModel?.ApplyVersionIncompatibility(
                        ClientBuildVersion.Current,
                        gate.ServerVersion);

                    return;
                }
            }

            var recovered = await _downloads.Coordinator
                .RecoverAsync(_downloads.ContentUriResolver, CancellationToken.None)
                .ConfigureAwait(true);

            if (recovered.Count == 0)
            {
                return;
            }

            // La microcopy de la reanudación se aplica ANTES de que la operación se
            // presente, así que el jugador lee «Reanudando desde 42 % · 3,2 GB de 7,6 GB»
            // y no un porcentaje suelto (UX-DR-6, UX-DR-45).
            _uiThread.Post(() => viewModel.ApplyResuming(true));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // La recuperación NO puede impedir que el jugador use el cliente: las
            // operaciones siguen persistidas y se reintentan en el siguiente arranque
            // (AD-11).
        }
    }

    /// <summary>
    /// Shows the overlay de CONFIRMACIÓN de la operación privilegiada (AC2, UX-DR-16,
    /// UX-DR-37).
    ///
    /// <para>
    /// El overlay vive DENTRO de la ventana y no abre una ventana nueva: el cliente es
    /// una aplicación de escritorio con una sola ventana y un diálogo nunca se apila
    /// sobre otro (UX-DR-16, UX-DR-23).
    /// </para>
    /// <para>
    /// El presentador TOMA el foco al abrir el overlay y lo devuelve al elemento
    /// INVOCADOR al cerrarlo, para que el <c>Esc</c> del jugador cancele la acción NO
    /// destructiva (AC2, UX-DR-37).
    /// </para>
    /// </summary>
    /// <param name="dialog">Overlay de confirmación de la operación.</param>
    /// <param name="window">Window of the player, which hosts the overlay.</param>
    /// <returns>A task that completes when the player confirmed or declined.</returns>
    private static Task ShowPrivilegedConfirmation(PrivilegedOperationDialog dialog, Window window)
    {
        var overlay = new PrivilegedOperationOverlay(dialog, window);

        overlay.Show();

        return Task.CompletedTask;
    }

    /// <summary>
    /// Shows the overlay de confirmación de la cancelación (AC4, UX-DR-22).
    /// </summary>
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

                    // AD-30, AD-32: cuando el servidor vuelve a estar disponible, el
                    // cliente se REGISTRA otra vez, y la misma operación cubre la
                    // reconexión: el servidor conserva la fila y el nombre efectivo del
                    // equipo, así que un cliente reconectado nunca colisiona consigo
                    // mismo.
                    _ = RegisterWithEventServerAsync(
                        _connectionViewModel?.ConnectionState ?? ConnectionState.Disconnected,
                        CancellationToken.None);

                    return;
                }

                banner.DisableWithReason(CatalogSyncBanner.ServerUnavailableReasonText);

                // AD-29, FR-10: el servidor deja de estar disponible, así que el
                // cliente DECLARA su desconexión y el servidor la persiste: la tabla
                // de clientes del organizador refleja que ese equipo ya no está.
                _ = _registration?.PublishStateAsync(
                    ConnectionState.Disconnected,
                    CancellationToken.None);
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
    /// <para>
    /// La lectura es LOCAL y su fallo NO puede impedir que el jugador abra el catálogo:
    /// la ficha se abre y su insignia sale del estado que el servidor publica (AC3,
    /// AD-26).
    /// </para>
    /// </summary>
    /// <param name="catalogSurface">Surface of the catalog of the player.</param>
    /// <param name="librarySurface">Surface of the Biblioteca of the player (AC8).</param>
    /// <returns>A task that completes when the installations have been read.</returns>
    private async Task LoadInstallationsAsync(
        CatalogSurface catalogSurface,
        LibrarySurface librarySurface)
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

            // AC1, AC2, AC8: la decisión sobre la jugabilidad se REderiva al pintar, a
            // partir del veredicto de la certificación y del inspector, así que no hay
            // columna nueva ni migración (AD-11, AD-22).
            var projection = await ProjectPlayabilityAsync().ConfigureAwait(true);

            if (projection is null)
            {
                return;
            }

            catalogSurface.ApplyDecisions(DecisionsOf(projection));

            librarySurface.ApplyInstallations(projection);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // La lectura de las instalaciones es LOCAL y su fallo NO puede impedir que el
            // jugador abra el catálogo: la ficha se abre y la insignia sale del estado que
            // el servidor publica (AC3, AD-26).
        }
    }

    /// <summary>
    /// Projects the playability of the installations that the client registered (AC1,
    /// AC2, AC8).
    /// </summary>
    /// <returns>The projection, or <c>null</c> when the client cannot project it.</returns>
    private async Task<IReadOnlyList<InstalledGamePlayability>?> ProjectPlayabilityAsync()
    {
        if (_gameLauncher is null)
        {
            return null;
        }

        try
        {
            return await _gameLauncher.Projection
                .ProjectAsync(CancellationToken.None)
                .ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // La proyección es LOCAL y su fallo NO puede impedir que el jugador use el
            // cliente: la Biblioteca se abre y cada superficie aplica su decisión
            // (AC8).
            return null;
        }
    }

    /// <summary>
    /// Builds the decisions of the projection, keyed by the identifier of the videogame
    /// (AC1, AC2).
    /// </summary>
    /// <param name="projection">Projection of the installations.</param>
    /// <returns>The decisions, keyed by the identifier of the videogame.</returns>
    private static IReadOnlyDictionary<string, PlayabilityDecision> DecisionsOf(
        IReadOnlyList<InstalledGamePlayability> projection)
    {
        var decisions = new Dictionary<string, PlayabilityDecision>(StringComparer.Ordinal);

        foreach (var installation in projection)
        {
            decisions[installation.GameId] = installation.Decision;
        }

        return decisions;
    }

    /// <summary>
    /// Records the intention of the PRIMARY action that the player pressed, que es
    /// POLIMÓRFICA: «Reparar» o «Descargar de nuevo» (AC1, AC2, UX-DR-20).
    ///
    /// <para>
    /// La ruta la decide la CAPA DE APLICACIÓN, que es su único dueño: «Descargar de
    /// nuevo» registra la intención de RE-DESCARGAR, así que el puerto de reparación no
    /// recibe nunca una re-descarga (AC1, AC2, AD-1).
    /// </para>
    /// <para>
    /// AC1, UX-DR-20: «Reparar» EJECUTA la reparación, que es la ejecución de esta
    /// historia: la reparación reutiliza el paquete ya validado y NO vuelve a descargar.
    /// La intención se registra IGUAL, así que el diagnóstico conserva la pulsación del
    /// jugador (AC1, AC2).
    /// </para>
    /// </summary>
    /// <param name="gameId">Identifier of the videogame.</param>
    /// <param name="primary">The PRIMARY action that the player pressed.</param>
    private async Task RecordPrimaryIntentAsync(string gameId, PlayabilityAction primary)
    {
        if (_gameLauncher is null)
        {
            return;
        }

        await _gameLauncher.IntentService
            .RecordPrimaryAsync(gameId, primary, CancellationToken.None)
            .ConfigureAwait(true);

        // AC1, UX-DR-20: la reparación se EJECUTA aquí, y su ejecución es de CERO RED: el
        // paquete ya validado y el manifiesto firmado en caché son locales.
        if (primary == PlayabilityAction.Repair)
        {
            await RepairInstallationAsync(gameId).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// REPAIRS an invalid installation from the package that was already validated,
    /// WITHOUT downloading it again (AC1, UX-DR-20).
    ///
    /// <para>
    /// AC2, UX-DR-17, UX-DR-18: mientras la reparación dura, la acción «Jugar» sigue
    /// BLOQUEADA con el motivo VISIBLE de la reparación en curso, así que el jugador
    /// nunca puede lanzar un juego que se está reparando.
    /// </para>
    /// <para>
    /// AC3: al terminar, la insignia y la acción primaria se REderivan de la
    /// certificación, así que la instalación reparada muestra «Instalado · v1.2» y «Jugar»
    /// vuelve a ser la primaria (AC3, UX-DR-17).
    /// </para>
    /// </summary>
    /// <param name="gameId">Identifier of the videogame whose installation is repaired.</param>
    /// <returns>A task that completes when the repair has been attempted.</returns>
    private async Task RepairInstallationAsync(string gameId)
    {
        if (_installations is null || _gameLauncher is null)
        {
            return;
        }

        // AC2: la reparación en curso bloquea «Jugar» con su motivo VISIBLE, así que la
        // decisión se aplica ANTES de que la reparación arranque.
        ApplyRepairInProgress(gameId);

        try
        {
            var installation = await _gameLauncher.Projection
                .FindInstallationAsync(gameId, CancellationToken.None)
                .ConfigureAwait(true);

            if (installation is null)
            {
                return;
            }

            var outcome = await _installations.RepairService
                .RepairAsync(
                    gameId,
                    installation.InstalledVersion,
                    CancellationToken.None)
                .ConfigureAwait(true);

            if (outcome.IsRejected)
            {
                _uiThread.Post(() => _catalogSurface?.ApplyLaunchError(outcome.Error?.Code));
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // La reparación NO puede impedir que el jugador use el cliente: el estado se
            // REderiva y la instalación vuelve a su estado vigente (AC1).
        }
        finally
        {
            // AC3: la decisión se REderiva de la certificación, así que la insignia final
            // y la primaria «Jugar» son las que la certificación declara.
            await RefreshDecisionsAsync().ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Applies the decision of a REPAIR IN PROGRESS, así que la acción «Jugar» queda
    /// bloqueada con el motivo VISIBLE (AC2, UX-DR-17, UX-DR-18).
    /// </summary>
    /// <param name="gameId">Identifier of the videogame.</param>
    private void ApplyRepairInProgress(string gameId) =>
        _uiThread.Post(() =>
        {
            _catalogSurface?.ApplyDecision(gameId, PlayabilityDecision.RepairInProgress());

            _librarySurface?.ApplyRepairInProgress(gameId);
        });

    /// <summary>
    /// REderiva las decisiones de jugabilidad y las aplica a las dos superficies, así que
    /// la insignia y la primaria reflejan la certificación vigente (AC3, AC1, AD-11).
    /// </summary>
    /// <returns>A task that completes when the decisions have been applied.</returns>
    private async Task RefreshDecisionsAsync()
    {
        var projection = await ProjectPlayabilityAsync().ConfigureAwait(true);

        if (projection is null)
        {
            return;
        }

        var decisions = DecisionsOf(projection);

        _uiThread.Post(() =>
        {
            _catalogSurface?.ApplyDecisions(decisions);

            _librarySurface?.ApplyInstallations(projection);
        });
    }

    /// <summary>
    /// Reads the errors that the client registered and hands them to the visor de
    /// diagnóstico (AC1, NFR-7).
    ///
    /// <para>
    /// La lectura es LOCAL: el registro de errores vive en la base de datos del cliente y
    /// nunca va al servidor (AD-22).
    /// </para>
    /// <para>
    /// La lectura NO bloquea el arranque: el visor se abre igual y su estado vacío dice
    /// que no hay errores registrados (AD-1, AD-26).
    /// </para>
    /// </summary>
    /// <param name="viewModel">View model of the visor de diagnóstico.</param>
    /// <returns>A task that completes when the errors have been read.</returns>
    private async Task LoadDiagnosticsAsync(DiagnosticsLogViewModel viewModel)
    {
        var store = _registration?.StateStore;

        if (store is null)
        {
            return;
        }

        try
        {
            var errors = await store
                .LoadErrorsAsync(CancellationToken.None)
                .ConfigureAwait(true);

            // El registro llega desde el grupo de hilos, así que la notificación se
            // encola en el hilo de la interfaz antes de tocar el visor (AC3, AC4, AC5).
            _uiThread.Post(() => viewModel.ApplyErrors(errors));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // La lectura del registro es LOCAL y su fallo NO puede impedir que el jugador
            // use el cliente: el visor se abre con su estado vacío (AC3, AD-26).
        }
    }

    /// <summary>
    /// Records the intention of OPENING the registered error, que es un SEAM: el
    /// registro de diagnóstico es de Epic 11 y aquí sólo queda registrada la intención
    /// (AC1, AC2).
    /// </summary>
    /// <param name="gameId">Identifier of the videogame.</param>
    private async Task RecordDiagnoseIntentAsync(string gameId)
    {
        if (_gameLauncher is null)
        {
            return;
        }

        await _gameLauncher.IntentService
            .RecordDiagnoseAsync(gameId, CancellationToken.None)
            .ConfigureAwait(true);
    }

    /// <summary>
    /// Shows the surface of the catalog, que es la salida accionable del estado vacío de
    /// la Biblioteca (AC8, UX-DR-28).
    /// </summary>
    private void ShowCatalog() => _catalogSurface?.Focus();

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

            // AD-30, AD-32: cuando la conexión pasa, el cliente se REGISTRA ante el
            // servidor, y la misma operación cubre el alta y la reconexión: el
            // servidor conserva la fila y el nombre efectivo del equipo.
            if (attempt.IsConnected)
            {
                await RegisterWithEventServerAsync(attempt.State, cancellationToken).ConfigureAwait(true);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // La conexión no puede impedir que el jugador vuelva a intentarlo (AC3).
        }
    }

    /// <summary>
    /// Registers the team against the event server, so the organizer sees it in the
    /// table of the clients (AD-30, AD-32).
    ///
    /// <para>
    /// Un fallo del registro NO impide que el jugador siga usando el cliente: el
    /// catálogo y las instalaciones no dependen del registro (AD-1).
    /// </para>
    /// </summary>
    /// <param name="connectionState">Connection state that the client declares.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the registration has been attempted.</returns>
    private async Task RegisterWithEventServerAsync(
        ConnectionState connectionState,
        CancellationToken cancellationToken)
    {
        if (_registration is null)
        {
            return;
        }

        try
        {
            var result = await _registration
                .RegisterAsync(connectionState, cancellationToken)
                .ConfigureAwait(true);

            // AD-30, AD-32: el rechazo del servidor se PRESENTA al jugador con su
            // mensaje, su causa y su siguiente paso, tal cual.
            if (result.Rejection is not null)
            {
                _connectionViewModel?.ApplyRegistrationError(result.Rejection);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // El registro es un complemento del cliente y no su motivo de vida: el
            // jugador sigue jugando (AD-1).
            _registration.Logger.LogWarning(
                exception,
                "No se pudo registrar el equipo ante el servidor: el cliente sigue operando sin estar registrado.");
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
