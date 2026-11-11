using Avalonia;
using LANPartyHub.Client.Application;
using LANPartyHub.Client.Application.Clients;
using LANPartyHub.Client.Application.Diagnostics;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Host;
using LANPartyHub.Client.Infrastructure.Configuration;
using LANPartyHub.Client.Infrastructure.Filesystem;
using LANPartyHub.Client.Infrastructure.Logging;
using LANPartyHub.Client.Infrastructure.Networking;
using LANPartyHub.Client.Infrastructure.Persistence;
using LANPartyHub.Client.Presentation;
using LANPartyHub.Client.Presentation.Install;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Client;

/// <summary>
/// Composition root of the desktop client (AD-1, AD-9, AD-11, AD-15, AD-17, AD-23, AD-24).
///
/// <para>
/// Story 1.3 fixes the startup of the client: the effective configuration with the precedence
/// flags &gt; env &gt; file, the layout of the data folder, the migrations of the database of the
/// client, the identity of the team and the reconciliation of the persisted progress. The
/// window itself belongs to the following stories.
/// </para>
/// <para>
/// Story 1.4 fixes the observability of the client: the log is JSON with scopes, the factory
/// of the log lives as long as the whole session and the correlation scope carries the client
/// identifier and the one of the unit of work (AD-15, AD-23).
/// </para>
/// </summary>
public static class Program
{
    /// <summary>Starts the client.</summary>
    /// <param name="args">Command line arguments of the application.</param>
    /// <returns>The exit code of the process: zero when the client ends normally.</returns>
    [STAThread]
    public static int Main(string[] args)
    {
        ILoggerFactory? loggerFactory = null;
        IClientCorrelationScope? correlationScope = null;
        CatalogSyncComposition? catalogSync = null;
        ConnectionComposition? connection = null;
        ClientRegistrationComposition? registration = null;
        TransferComposition? transfer = null;
        DownloadComposition? downloads = null;
        InstallationComposition? installations = null;
        GameLauncherComposition? gameLauncher = null;
        DialogPrivilegedOperationConfirmation? privilegedConfirmation = null;
        ClientStateHubPublisher? statePublisher = null;
        ClientStateStoreLease? stateStoreLease = null;
        RecordClientError? errorRecorder = null;

        try
        {
            // AD-15: the log of the client is JSON from the very first startup, it goes to
            // the console and to a file next to the deployment, and the factory lives as
            // long as the session: the log of a client that runs for hours does not
            // evaporate when the startup ends.
            loggerFactory = CreateLoggerFactory(AppContext.BaseDirectory);

            // AD-15, AD-23: the correlation scope belongs to the composition root, so it is
            // opened as soon as the identity of the team is known and it is closed when the
            // session ends.
            //
            // AD-14, AD-30: la etiqueta de la máquina es el nombre humano del equipo, y el
            // servidor la usa para distinguir a los clientes en el reparto del enlace. No
            // es una credencial: no habilita nada.
            correlationScope = new ClientCorrelationFactory(loggerFactory, Environment.MachineName);

            var startup = StartClientAsync(args, correlationScope, loggerFactory).GetAwaiter().GetResult();

            // AD-3: la composición del descubrimiento vive mientras vive la ventana del
            // jugador, porque la superficie de Conexión consume su resultado. Un fallo al
            // abrirla NO impide que el cliente arrancar: la vía manual sigue operativa
            // (AC4).
            //
            // AD-27: la composición recibe la ruta de la base de datos del cliente, que es
            // donde vive el ancla de confianza YA FIJADA. Las conexiones posteriores se
            // validan contra ese valor persistido (AD-27).
            //
            // AC5: la composición se abre ANTES que la de la sincronización, porque la
            // reconexión repite la puerta de versión (AD-6) y la del ancla fijado (AD-27),
            // y esa puerta vive en el caso de uso de la conexión.
            connection = OpenConnection(correlationScope, loggerFactory, startup.StateDatabasePath);

            // AD-18, AD-29: el publicador del estado del cliente es el ÚNICO canal de
            // push del sistema, así que se abre UNA vez y se comparte con la
            // sincronización, el registro y las instalaciones: todas las publicaciones
            // viajan por la MISMA conexión con el hub (AD-22).
            //
            // Un fallo al abrirlo NO impide que el cliente arrancar: la proyección es un
            // complemento del cliente y no su motivo de vida (AD-1).
            statePublisher = OpenStatePublisher(startup, loggerFactory);

            // AD-11: el alquiler del store se abre AQUÍ y se dispone al final de la
            // sesión, así que el contexto sobre `state.sqlite` no queda retenido (AD-11).
            //
            // El store se abre ANTES que las instalaciones, las descargas y el lanzador,
            // porque el REGISTRADOR de errores necesita escribir en él y el registrador es
            // COMPARTIDO por las tres composiciones (AD-1, AD-11, NFR-7).
            stateStoreLease = OpenClientStateStore(startup);

            // AD-1, AD-11, AD-15, NFR-7: el REGISTRADOR de errores del cliente es el
            // ÚNICO dueño del orden del diagnóstico: primero PERSISTE el error en la base
            // de datos del cliente y después lo PUBLICA al servidor como
            // `ClientErrorReported`. Un fallo del hub NO puede impedir la persistencia
            // (AD-15).
            //
            // El registrador es COMPARTIDO por las descargas, las instalaciones y el
            // lanzador, así que los tres registran sus errores con la misma regla y el
            // mismo orden, y con los MISMOS identificadores de correlación (AD-23).
            errorRecorder = OpenErrorRecorder(startup, stateStoreLease?.Store, statePublisher, loggerFactory);

            // AD-7, AD-8, AD-9, AC1: la composición de las instalaciones. El manifiesto
            // firmado se pide al servidor, su firma se valida contra el ancla ya fijada y
            // SOLO después se crea el directorio de instalación (AC1, AC3).
            //
            // AC2: la confirmación de lo privilegiado la lleva el overlay de UN SOLO
            // NIVEL dentro de la ventana, así que el puerto se construye aquí y el
            // presentador se fija cuando la ventana ya existe (UX-DR-16, UX-DR-37).
            privilegedConfirmation = new DialogPrivilegedOperationConfirmation(
                (operation, detail) => new PrivilegedOperationDialog(operation, detail));

            // AD-22, FR-10: la composición de las instalaciones se abre ANTES que la del
            // REGISTRO, porque el registro publica el estado del cliente y ese estado
            // lleva las instalaciones EN CURSO, que el tracker de la composición de las
            // instalaciones posee (AD-18, AD-22).
            installations = OpenInstallations(
                startup,
                correlationScope,
                loggerFactory,
                privilegedConfirmation,
                statePublisher,
                errorRecorder);

            // AD-5, AD-26: la composición de la sincronización del catálogo vive mientras
            // vive la ventana del jugador, porque la sincronización tiene que reaccionar al
            // disparo del hub mientras el cliente corre. Se abre DESPUÉS del startup, que
            // ya ha aplicado las migraciones de la base de datos del cliente (AD-11).
            //
            // AD-33: el sincronizador REPORTA el resultado terminal de las operaciones
            // retiradas del catálogo, así que recibe el publicador del estado (AD-22).
            catalogSync = OpenCatalogSync(startup, correlationScope, loggerFactory, connection, statePublisher);

            // AD-30, AD-32: la composición del REGISTRO del equipo. El cliente se
            // registra ante el servidor cuando se conecta y cuando el servidor vuelve
            // a estar disponible, y la MISMA operación cubre el alta, la reconexión y
            // el renombrado (AD-32).
            //
            // Un fallo al abrirla NO impide que el cliente arrancar: el registro es un
            // complemento del cliente y no su motivo de vida (AD-1).
            //
            // AD-22: el registro publica el estado REAL del cliente —las instalaciones
            // que tiene y las operaciones en curso—, así que recibe el store local y la
            // fuente de las instalaciones en curso (AD-18, AD-22).
            registration = OpenRegistration(
                startup,
                correlationScope,
                loggerFactory,
                statePublisher: statePublisher,
                stateStore: stateStoreLease?.Store,
                activeInstallations: installations?.ActiveInstallations,
                connectionPublisher: statePublisher,
                errorRecorder: errorRecorder);

            // AD-22, FR-10: la publicación del estado COMPLETO se ENLAZA TARDE, después de
            // que existan las dos composiciones. El registro necesita al tracker como
            // fuente de las instalaciones EN CURSO, y el tracker necesita al registro
            // para publicar esa lista: construirlos con el uno al otro al mismo tiempo
            // sería un ciclo, así que uno de los dos llega después (AD-22).
            //
            // Con el enlace, una instalación que empieza o termina DECLARA al servidor
            // sus operaciones en curso, y la tabla del organizador ve aparecer y
            // desaparecer la operación sin esperar a la siguiente publicación (AD-22).
            if (installations?.ActiveInstallations is { } tracker && registration is not null)
            {
                tracker.ConfigureStatePublication(registration.PublishCurrentStateAsync);
            }

            // AD-4, AD-28: la transferencia de bytes entra por el puerto
            // `ITransferEngine` y el puerto se entrega con UNA sola implementación,
            // `HttpRangeTransferEngine`. La composición NO expone ningún selector: en el
            // MVP no hay nada que seleccionar.
            //
            // AD-6, AD-15, AD-23: la composición recibe el scope de correlación ya
            // abierto con el identificador canónico del equipo, así que el motor estampa
            // un identificador real y el servidor puede pronunciarse sobre la
            // compatibilidad de la petición.
            transfer = new TransferComposition(correlationScope);

            // AD-5, AD-10, AD-14: la composición de las descargas. El cliente registra su
            // intención con el servidor y pide los bytes por el puerto de la
            // transferencia, con los límites de concurrencia que fija la configuración
            // efectiva (AC10).
            //
            // Un fallo al abrirla NO impide que el cliente arrancar: las descargas son un
            // complemento del cliente y no su motivo de vida (AD-1).
            downloads = OpenDownloads(
                startup,
                transfer,
                correlationScope,
                loggerFactory,
                connection,
                statePublisher,
                errorRecorder);

            // AD-22, FR-10: la publicación del estado COMPLETO se ENLAZA TARDE al
            // coordinador de las descargas, así que una descarga que EMPIEZA o que
            // TERMINA DECLARA al servidor sus operaciones en curso, y la tabla del
            // organizador ve aparecer y desaparecer la operación sin esperar a la
            // siguiente publicación periódica (AD-22).
            //
            // El enlace es TARDE porque el REGISTRO publica el estado y el coordinador
            // necesita al registro: construirlos con el uno al otro al mismo tiempo sería
            // un ciclo (AD-22).
            if (downloads is not null && registration is not null)
            {
                downloads.Coordinator.ConfigureStateNotifier(
                    new RegistrationStateChangeNotifier(registration));
            }

            // AD-19, AC1: la composición del lanzador de juegos. El lanzador NO necesita
            // servidor para arrancar: lee la instalación que el cliente ya registró y el
            // manifiesto firmado que ya está en la caché local, así que un cliente sin
            // servidor puede lanzar lo que ya tiene instalado (AD-3, AD-19).
            gameLauncher = OpenGameLauncher(startup, loggerFactory, errorRecorder);

            BuildAvaloniaApp(
                startup.Settings,
                catalogSync: catalogSync,
                connection: connection,
                downloads: downloads,
                installations: installations,
                gameLauncher: gameLauncher,
                registration: registration)
                .StartWithClassicDesktopLifetime(args);

            return 0;
        }
        catch (ClientStartupException exception)
        {
            ReportStartupFailure(exception.Message);

            return 1;
        }
        catch (Exception exception)
        {
            // Any other failure is a problem of the machine, the disk or the network: the
            // player gets one line and not a stack trace.
            ReportStartupFailure($"El cliente no pudo arrancar: {exception.Message}");

            return 1;
        }
        finally
        {
            catalogSync?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            downloads?.Dispose();
            registration?.Dispose();
            installations?.Dispose();
            transfer?.Dispose();
            statePublisher?.DisposeAsync().AsTask().GetAwaiter().GetResult();

            // AD-11: el contexto del store del estado se cierra con la sesión, así que
            // no queda una conexión retenida sobre `state.sqlite` (AD-11).
            stateStoreLease?.Dispose();

            correlationScope?.Dispose();
            loggerFactory?.Dispose();
        }
    }

    /// <summary>
    /// Opens the publisher of the state of the client (AD-18, AD-29).
    ///
    /// <para>
    /// El publicador es el ÚNICO canal de push del sistema, así que se abre UNA vez y se
    /// comparte con el registro, la sincronización y las instalaciones: todas las
    /// publicaciones viajan por la MISMA conexión con el hub (AD-22).
    /// </para>
    /// <para>
    /// Un fallo al abrirlo NO impide que el cliente arrancar: la proyección es un
    /// complemento del cliente y no su motivo de vida (AD-1).
    /// </para>
    /// </summary>
    /// <param name="startup">Outcome of the startup of the client.</param>
    /// <param name="loggerFactory">Factory of the log of the client.</param>
    /// <returns>The publisher, or <c>null</c> when it could not be opened.</returns>
    private static ClientStateHubPublisher? OpenStatePublisher(
        ClientStartupResult startup,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(startup);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        if (string.IsNullOrWhiteSpace(startup.ServerAddress))
        {
            return null;
        }

        try
        {
            return new ClientStateHubPublisher(
                new Uri(startup.ServerAddress, UriKind.Absolute),
                loggerFactory.CreateLogger<ClientStateHubPublisher>());
        }
        catch (Exception exception)
        {
            // El cliente no puede publicar su estado, pero sigue siendo un cliente que
            // arranca: el catálogo y las instalaciones no dependen de la proyección
            // (AD-1).
            loggerFactory
                .CreateLogger("LANPartyHub.Client.State")
                .LogWarning(
                    exception,
                    "No se pudo abrir el publicador del estado del cliente: el cliente sigue arrancando sin publicar su estado.");

            return null;
        }
    }

    /// <summary>
    /// Opens the access to the local database of the client, de donde salen las
    /// instalaciones y las operaciones REALES que el cliente publica (AD-22).
    ///
    /// <para>
    /// El store se abre sobre el MISMO archivo SQLite que el arranque ya migró, así que
    /// la publicación del estado ve las instalaciones que el cliente ya registró
    /// (AD-11, AD-22).
    /// </para>
    /// <para>
    /// El alquiler DEVUELVE la conexión con el archivo: el contexto se abre UNA vez y
    /// se DISPONE cuando la sesión termina, así que no queda una conexión retenida
    /// compitiendo con los demás contextos que el cliente abre sobre la misma base
    /// (AD-11).
    /// </para>
    /// </summary>
    /// <param name="startup">Outcome of the startup of the client.</param>
    /// <returns>The lease of the store, or <c>null</c> when it could not be opened.</returns>
    private static ClientStateStoreLease? OpenClientStateStore(ClientStartupResult startup)
    {
        ArgumentNullException.ThrowIfNull(startup);

        ClientStateDbContext? context = null;

        try
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = startup.StateDatabasePath,
                Pooling = false,
            }.ToString();

            var options = new DbContextOptionsBuilder<ClientStateDbContext>()
                .UseSqlite(
                    connectionString,
                    sqlite => sqlite.MigrationsAssembly(typeof(ClientStateDbContext).Assembly.GetName().Name))
                .Options;

            context = new ClientStateDbContext(options);

            return new ClientStateStoreLease(
                context,
                new ClientStateStore(context, startup.Layout));
        }
        catch (Exception)
        {
            // Sin store el estado se publica SIN las instalaciones y las operaciones, y el
            // cliente sigue arrancando: la proyección es un complemento y no su motivo de
            // vida (AD-1, AD-22).
            //
            // El contexto a medias se DISPONE aquí: un contexto abierto sobre
            // `state.sqlite` retiene la conexión con el archivo (AD-11).
            context?.Dispose();

            return null;
        }
    }

    /// <summary>Builds the Avalonia application builder of the client.</summary>
    /// <param name="settings">Settings of the client, or <c>null</c> when there are none.</param>
    /// <param name="startupError">Message of a failure of the startup, or <c>null</c> when the startup succeeded.</param>
    /// <param name="catalogSync">
    /// Composition of the synchronization of the catalog, or <c>null</c> when there is none.
    /// </param>
    /// <param name="connection">
    /// Composition of the discovery of the event server, or <c>null</c> when there is none.
    /// </param>
    /// <param name="downloads">
    /// Composition of the downloads, or <c>null</c> when there is none.
    /// </param>
    /// <param name="installations">
    /// Composition of the installations, or <c>null</c> when there is none.
    /// </param>
    /// <param name="gameLauncher">
    /// Composition of the launcher of the videogames, or <c>null</c> when there is none.
    /// </param>
    public static AppBuilder BuildAvaloniaApp(
        ClientSettings? settings = null,
        string? startupError = null,
        CatalogSyncComposition? catalogSync = null,
        ConnectionComposition? connection = null,
        DownloadComposition? downloads = null,
        InstallationComposition? installations = null,
        GameLauncherComposition? gameLauncher = null,
        ClientRegistrationComposition? registration = null) =>
        AppBuilder.Configure(() => new App(
            settings,
            startupError,
            catalogSync,
            connection,
            downloads: downloads,
            installations: installations,
            gameLauncher: gameLauncher,
            registration: registration)).UsePlatformDetect();

    /// <summary>
    /// Opens the REGISTRADOR de errores del cliente (AD-1, AD-11, AD-15, AD-23, NFR-7).
    ///
    /// <para>
    /// El registrador es el ÚNICO dueño del orden del diagnóstico: PERSISTE el error en la
    /// base de datos del cliente y lo PUBLICA al servidor como
    /// <c>ClientErrorReported</c>. La publicación NUNCA impide la persistencia, así que un
    /// hub que no está no se lleva el diagnóstico (AD-15, AD-1).
    /// </para>
    /// <para>
    /// El identificador del equipo lo emite el cliente una sola vez y lo persistió, así que
    /// el registrador lo toma de ahí y no lo inventa (AD-23).
    /// </para>
    /// <para>
    /// Sin store local NO hay registrador: el error se quedaría sin persistir, así que el
    /// registro del diagnóstico es un complemento del cliente y no su motivo de vida
    /// (AD-1).
    /// </para>
    /// </summary>
    /// <param name="startup">Outcome of the startup of the client.</param>
    /// <param name="store">Access to the local database of the client.</param>
    /// <param name="publisher">
    /// Port of the publication of the ERROR al servidor, or <c>null</c> when the client
    /// could not open it.
    /// </param>
    /// <param name="loggerFactory">Factory of the log of the client.</param>
    /// <returns>The recorder, or <c>null</c> when the client has no local database.</returns>
    private static RecordClientError? OpenErrorRecorder(
        ClientStartupResult startup,
        IClientStateStore? store,
        IClientErrorPublisher? publisher,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(startup);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        if (store is null)
        {
            // Sin store local el error no se puede persistir, así que el registro del
            // diagnóstico no puede existir: el cliente sigue operando (AD-1, AD-11).
            return null;
        }

        return new RecordClientError(
            store,
            publisher,
            new StartupClientIdentity(startup.Identity),
            loggerFactory.CreateLogger<RecordClientError>());
    }

    /// <summary>
    /// Builds the two columns of the window: the content and the persistent panel of the
    /// connection (AC1).
    /// </summary>
    /// <param name="args">Command line arguments of the process; only <c>--server-address</c>, <c>--install-directory</c> and <c>--theme</c> are accepted.</param>
    /// <param name="correlationScope">
    /// Scope that correlates the log of the client. It belongs to the caller, so it stays
    /// open as long as the session lasts (AD-15, AD-23).
    /// </param>
    /// <param name="loggerFactory">
    /// Factory of the log of the client. It belongs to the caller, so the entries of the
    /// log outlive the startup (AD-15).
    /// </param>
    /// <param name="dataFolder">
    /// Folder of the data of the client. It defaults to <c>%LOCALAPPDATA%\LanPartyHub</c>
    /// (AD-17) and it exists so the integration level can exercise the startup over a
    /// temporary folder.
    /// </param>
    /// <param name="baseDirectory">
    /// Folder of the deployment: it holds the configuration file. It defaults to the folder of
    /// the binary.
    /// </param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task with the outcome of the startup.</returns>
    /// <exception cref="ClientStartupException">The startup cannot continue.</exception>
    /// <remarks>
    /// The composition is closed over the startup: the database of the client lives
    /// exactly as long as it takes. The stories that need it alive (the connection, the
    /// catalogue and the downloads) open their own composition over the same layout.
    /// </remarks>
    public static async Task<ClientStartupResult> StartClientAsync(
        string[] args,
        IClientCorrelationScope correlationScope,
        ILoggerFactory loggerFactory,
        string? dataFolder = null,
        string? baseDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(correlationScope);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        return await RunStartupAsync(args, correlationScope, loggerFactory, dataFolder, baseDirectory, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Creates the factory of the log of the client. The log is JSON and it carries the
    /// scopes, so the correlation of the operation and the client travels in every entry
    /// (AD-15).
    /// </summary>
    /// <param name="deploymentDirectory">
    /// Folder of the deployment: the file of the log is written next to the configuration
    /// file, because the client is a desktop application and it has no console when the
    /// player launches it from the desktop.
    /// </param>
    /// <returns>The factory of the log of the client.</returns>
    public static ILoggerFactory CreateLoggerFactory(string deploymentDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deploymentDirectory);

        return LoggerFactory.Create(logging =>
        {
            logging.ClearProviders();
            logging.AddJsonConsole(options =>
            {
                options.IncludeScopes = true;
                options.TimestampFormat = "O";
                options.UseUtcTimestamp = true;
            });

            logging.AddProvider(new JsonFileLoggerProvider(
                Path.Combine(deploymentDirectory, JsonFileLoggerProvider.FileName)));

            logging.SetMinimumLevel(LogLevel.Information);
        });
    }

    /// <summary>
    /// Runs the startup of the client over the given factory of the log.
    /// </summary>
    private static async Task<ClientStartupResult> RunStartupAsync(
        string[] args,
        IClientCorrelationScope correlationScope,
        ILoggerFactory loggerFactory,
        string? dataFolder,
        string? baseDirectory,
        CancellationToken cancellationToken)
    {
        var deploymentDirectory = baseDirectory ?? AppContext.BaseDirectory;
        var dataFolderPath = dataFolder ?? ClientDataLayout.DefaultDataFolder;

        // AD-12, AR-23: the command line is parsed strictly and it is the source of the
        // highest priority of the configuration.
        var commandLine = ClientCommandLine.Parse(args);

        // The effective configuration is read out of the three sources, in the order
        // file > env > flags.
        var effectiveConfiguration = ClientConfiguration.Resolve(deploymentDirectory, dataFolderPath, commandLine);

        // AD-15: the effective configuration is published, because it is the surface the
        // player inspects when the client does not behave as expected.
        loggerFactory.CreateLogger("LANPartyHub.Client.Startup").LogInformation(
            "Configuración efectiva: carpeta de datos {DataFolder}, directorio de instalación {InstallDirectory}, tema {Theme}.",
            effectiveConfiguration.DataFolder,
            effectiveConfiguration.InstallDirectory,
            effectiveConfiguration.Theme);

        var layout = ClientDataLayout.Create(effectiveConfiguration.DataFolder, effectiveConfiguration.InstallDirectory);

        // The connection string is built with its builder, so a path that carries a semicolon
        // or a quote is quoted and the connection string stays well formed. The pool is
        // disabled because the database is opened a handful of times and a pooled handle would
        // keep the file of the data folder locked while the client runs.
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = layout.StateDatabasePath,
            Pooling = false,
        }.ToString();

        var options = new DbContextOptionsBuilder<ClientStateDbContext>()
            .UseSqlite(
                connectionString,
                sqlite => sqlite.MigrationsAssembly(typeof(ClientStateDbContext).Assembly.GetName().Name))
            .Options;

        await using var context = new ClientStateDbContext(options);

        var stateStore = new ClientStateStore(context);
        var reconciler = new DownloadProgressReconciler(
            layout,
            stateStore,
            loggerFactory.CreateLogger<DownloadProgressReconciler>());

        var bootstrap = new ClientBootstrap(
            layout,
            stateStore,
            reconciler,
            correlationScope,
            loggerFactory.CreateLogger<ClientBootstrap>());

        // The settings of the player are written in the configuration file of the deployment,
        // which is the layer of the lowest priority of the configuration.
        var settings = new ClientSettings(
            new ClientSettingsStore(
                Path.Combine(deploymentDirectory, ClientConfiguration.FileName),
                ClientConfiguration.SectionName,
                ClientConfiguration.InstallDirectoryKey),
            layout);

        try
        {
            var result = await bootstrap.RunAsync(effectiveConfiguration.ToBootstrapOptions(), cancellationToken);

            return result with { Settings = settings };
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new ClientStartupException(
                $"El cliente no pudo arrancar: {exception.Message}",
                exception);
        }
        catch (IOException exception)
        {
            throw new ClientStartupException(
                $"El cliente no pudo arrancar: {exception.Message}",
                exception);
        }
    }

    /// <summary>
    /// Opens the composition of the synchronization of the catalog (AD-5, AD-26).
    ///
    /// <para>
    /// The composition is opened only when the client knows the address of the event
    /// server: sin servidor no hay catálogo que sincronizar, y el cliente conserva el
    /// último catálogo que aplicó (AC3).
    /// </para>
    /// <para>
    /// Un fallo al abrir la composición NO impide que el cliente arranque: la ventana se
    /// abre sin el aviso del catálogo, porque la sincronización es un complemento del
    /// cliente y no su motivo de vida (AD-1).
    /// </para>
    /// </summary>
    /// <param name="startup">Outcome of the startup of the client.</param>
    /// <param name="correlationScope">Scope that correlates the log of the client.</param>
    /// <param name="loggerFactory">Factory of the log of the client.</param>
    /// <param name="connection">
    /// Composition of the conexión, whose case de uso es la puerta de versión (AD-6) y la
    /// del ancla fijado (AD-27) que la reconexión repite (AC5).
    /// </param>
    /// <returns>The composition, or <c>null</c> when it could not be opened.</returns>
    private static CatalogSyncComposition? OpenCatalogSync(
        ClientStartupResult startup,
        IClientCorrelationScope correlationScope,
        ILoggerFactory loggerFactory,
        ConnectionComposition? connection = null,
        IOperationOutcomeReporter? outcomeReporter = null)
    {
        ArgumentNullException.ThrowIfNull(startup);
        ArgumentNullException.ThrowIfNull(correlationScope);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        if (string.IsNullOrWhiteSpace(startup.ServerAddress))
        {
            return null;
        }

        CatalogSyncComposition? composition = null;

        try
        {
            composition = new CatalogSyncComposition(
                new Uri(startup.ServerAddress, UriKind.Absolute),
                startup.StateDatabasePath,
                correlationScope,
                loggerFactory,
                connection?.ConnectionService,
                new AvaloniaUiThread(),
                outcomeReporter);

            composition.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            // El cliente no puede sincronizar el catálogo, pero sigue siendo un cliente
            // que arranca: la ventana se abre y el aviso de la sincronización pendiente
            // aparece cuando la sincronización se intente de nuevo (AC3).
            loggerFactory
                .CreateLogger("LANPartyHub.Client.CatalogSync")
                .LogWarning(
                    exception,
                    "No se pudo abrir la sincronización del catálogo: el cliente sigue arrancando sin ella.");

            composition?.DisposeAsync().AsTask().GetAwaiter().GetResult();

            composition = null;
        }

        return composition;
    }

    /// <summary>
    /// Opens the composition of the registration of the team against the event server
    /// (AD-30, AD-32).
    ///
    /// <para>
    /// La composición se abre solo cuando el cliente conoce la dirección del servidor:
    /// sin servidor no hay registro (AD-3).
    /// </para>
    /// <para>
    /// Un fallo al abrir la composición NO impide que el cliente arrancar: el registro
    /// es un complemento del cliente y no su motivo de vida (AD-1).
    /// </para>
    /// </summary>
    /// <param name="startup">Outcome of the startup of the client.</param>
    /// <param name="correlationScope">Scope that correlates the log of the client.</param>
    /// <param name="loggerFactory">Factory of the log of the client.</param>
    /// <param name="baseDirectory">
    /// Folder of the deployment: it holds the configuration file where the effective name
    /// of the team is persisted (AD-12, AD-30).
    /// </param>
    /// <returns>The composition, or <c>null</c> when it could not be opened.</returns>
    /// <param name="startup">Outcome of the startup of the client.</param>
    /// <param name="correlationScope">Scope that correlates the log of the client.</param>
    /// <param name="loggerFactory">Factory of the log of the client.</param>
    /// <param name="baseDirectory">
    /// Folder of the deployment: it holds the configuration file where the effective name
    /// of the team is persisted (AD-12, AD-30).
    /// </param>
    /// <param name="statePublisher">
    /// Publisher of the state of the client, que es el ÚNICO canal de push del sistema
    /// (AD-18, AD-29).
    /// </param>
    /// <param name="stateStore">
    /// Access to the local database of the client, de donde salen las instalaciones y las
    /// operaciones REALES que el cliente publica (AD-22).
    /// </param>
    /// <param name="activeInstallations">
    /// Source of the INSTALLATION operations that the client is running right now, así que
    /// el estado publicado las lleva como operaciones en curso (AD-18, AD-22, FR-10).
    /// </param>
    /// <param name="connectionPublisher">
    /// Port of the publication of the CONNECTION state ALONE, para el cliente que no
    /// puede sostener una instantánea completa de su estado (AD-18, AD-22, AD-29).
    /// </param>
    /// <param name="errorRecorder">
    /// Use case that REGISTERS the errors del cliente, or <c>null</c> when the client
    /// does not register them. Sin registrador el error queda PERSISTIDO en la base de
    /// datos del cliente y no se publica: la proyección es un complemento y no su
    /// motivo de vida (AD-1, AD-11, NFR-7).
    /// </param>
    /// <returns>The composition, or <c>null</c> when it could not be opened.</returns>
    private static ClientRegistrationComposition? OpenRegistration(
        ClientStartupResult startup,
        IClientCorrelationScope correlationScope,
        ILoggerFactory loggerFactory,
        string? baseDirectory = null,
        IClientStatePublisher? statePublisher = null,
        IClientStateStore? stateStore = null,
        IActiveInstallationSource? activeInstallations = null,
        IClientConnectionPublisher? connectionPublisher = null,
        RecordClientError? errorRecorder = null)
    {
        ArgumentNullException.ThrowIfNull(startup);
        ArgumentNullException.ThrowIfNull(correlationScope);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        if (string.IsNullOrWhiteSpace(startup.ServerAddress))
        {
            return null;
        }

        ClientRegistrationComposition? composition = null;
        ClientStateHubPublisher? ownedPublisher = null;

        try
        {
            // AD-30, AD-31: el nombre efectivo del equipo vive en el ARCHIVO DE
            // CONFIGURACIÓN del cliente, porque el esquema de su base de datos es el del
            // ERD de la espina y no admite una columna más (AD-11, AD-22).
            var deploymentDirectory = baseDirectory ?? AppContext.BaseDirectory;

            var nameStore = new ClientEffectiveNameStore(
                Path.Combine(deploymentDirectory, ClientConfiguration.FileName),
                ClientConfiguration.SectionName,
                ClientConfiguration.EffectiveMachineNameKey);

            // AD-18, AD-29: el publicador del estado viaja por el hub del servidor, que
            // es el canal sancionado para el estado que declara el cliente.
            //
            // El publicador es COMPARTIDO con la sincronización y las instalaciones, así
            // que la conexión con el hub se abre UNA vez y todas las publicaciones viajan
            // por el mismo canal (AD-18, AD-22).
            if (statePublisher is null)
            {
                ownedPublisher = new ClientStateHubPublisher(
                    new Uri(startup.ServerAddress, UriKind.Absolute),
                    loggerFactory.CreateLogger<ClientStateHubPublisher>());
            }

            var publisher = (IClientStatePublisher?)statePublisher ?? ownedPublisher!;

            // AD-22: el estado que el cliente publica es el REAL: las instalaciones que
            // tiene registradas y las operaciones que están en curso. El store del
            // cliente es la fuente de las instalaciones, y la fuente de las
            // instalaciones EN CURSO se entrega aparte (AD-18, AD-22).
            composition = new ClientRegistrationComposition(
                new Uri(startup.ServerAddress, UriKind.Absolute),
                correlationScope,
                startup.Identity,
                nameStore,
                publisher,
                loggerFactory,
                stateStore,
                activeInstallations,
                connectionPublisher ?? ownedPublisher,
                ownedPublisher,
                errorRecorder);
        }
        catch (Exception exception)
        {
            // El cliente no puede registrarse, pero sigue siendo un cliente que
            // arranca: la ventana se abre y el catálogo sigue disponible (AD-1).
            loggerFactory
                .CreateLogger("LANPartyHub.Client.Registration")
                .LogWarning(
                    exception,
                    "No se pudo abrir la composición del registro del equipo: el cliente sigue arrancando sin ella.");

            composition?.Dispose();
            if (composition is null)
            {
                ownedPublisher?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }

            composition = null;
        }

        return composition;
    }

    /// <summary>
    /// Opens the composition of the downloads (AD-5, AD-10, AD-14, AC10).
    ///
    /// <para>
    /// La composición se abre solo cuando el cliente conoce la dirección del servidor:
    /// sin servidor no hay a quién descargar, y el cliente conserva lo que ya tenía
    /// (AD-3).
    /// </para>
    /// <para>
    /// Un fallo al abrir la composición NO impide que el cliente arrancar: las descargas
    /// son un complemento del cliente y no su motivo de vida (AD-1).
    /// </para>
    /// </summary>
    /// <param name="startup">Outcome of the startup of the client.</param>
    /// <param name="transfer">Composition of the transfer of bytes of the client.</param>
    /// <param name="correlationScope">Scope that correlates the log of the client.</param>
    /// <param name="loggerFactory">Factory of the log of the client.</param>
    /// <param name="connection">
    /// Composition of the connection, whose sonda de versiones declara la puerta de
    /// compatibilidad que las descargas repiten antes de reanudar (AD-6, AC5).
    /// </param>
    /// <param name="progressReporter">
    /// Publisher of the progress of the download, or <c>null</c> when the client could
    /// not open the shared one. AD-22, AD-29: el progreso viaja por el publicador
    /// COMPARTIDO del estado del cliente, así que el progreso y el estado salen por la
    /// MISMA conexión con el hub y no pueden reordenarse (AD-22).
    /// </param>
    /// <param name="errorRecorder">
    /// Use case that REGISTERS the errors de la descarga, or <c>null</c> when the client
    /// does not register them. Sin registrador la descarga SIGUE: el diagnóstico es un
    /// complemento y no su motivo de vida (AD-1, AD-11, NFR-7).
    /// </param>
    /// <returns>The composition, or <c>null</c> when it could not be opened.</returns>
    private static DownloadComposition? OpenDownloads(
        ClientStartupResult startup,
        TransferComposition transfer,
        IClientCorrelationScope correlationScope,
        ILoggerFactory loggerFactory,
        ConnectionComposition? connection = null,
        ClientStateHubPublisher? progressReporter = null,
        RecordClientError? errorRecorder = null)
    {
        ArgumentNullException.ThrowIfNull(startup);
        ArgumentNullException.ThrowIfNull(transfer);
        ArgumentNullException.ThrowIfNull(correlationScope);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        if (string.IsNullOrWhiteSpace(startup.ServerAddress))
        {
            return null;
        }

        DownloadComposition? composition = null;

        try
        {
            // AD-11: el coordinador persiste el offset acumulado en la base de datos del
            // cliente, así que la composición abre su propio acceso a ella.
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = startup.StateDatabasePath,
                Pooling = false,
            }.ToString();

            var options = new DbContextOptionsBuilder<ClientStateDbContext>()
                .UseSqlite(
                    connectionString,
                    sqlite => sqlite.MigrationsAssembly(typeof(ClientStateDbContext).Assembly.GetName().Name))
                .Options;

            var context = new ClientStateDbContext(options);

            var layout = ClientDataLayout.Create(
                startup.DataFolder,
                startup.InstallDirectory);

            // AD-2, AD-27: la validación de la autenticidad se hace contra el ancla de
            // confianza YA FIJADA, que vive en la base de datos del cliente. El store es
            // el que la lee, así que la composición lo recibe como el port que es (AD-27).
            var stateStore = new ClientStateStore(context);

            composition = new DownloadComposition(
                new Uri(startup.ServerAddress, UriKind.Absolute),
                correlationScope,
                stateStore,
                layout,
                transfer.Engine,
                loggerFactory,
                stateStore,
                startup.MaxConcurrentDownloads,
                startup.MaxTotalConnections,
                connection?.VersionProbe,
                progressReporter,
                errorRecorder);
        }
        catch (Exception exception)
        {
            // El cliente no puede descargar, pero sigue siendo un cliente que arranca: la
            // ventana se abre y el catálogo sigue disponible (AD-1).
            loggerFactory
                .CreateLogger("LANPartyHub.Client.Downloads")
                .LogWarning(
                    exception,
                    "No se pudo abrir la composición de las descargas: el cliente sigue arrancando sin ella.");

            composition?.Dispose();

            composition = null;
        }

        return composition;
    }

    /// <summary>
    /// Opens the composition of the installations (AD-7, AD-8, AD-9, AC1..AC7).
    ///
    /// <para>
    /// La composición se abre solo cuando el cliente conoce la dirección del servidor:
    /// sin servidor no hay manifiesto firmado que pedir (AD-3).
    /// </para>
    /// <para>
    /// Un fallo al abrir la composición NO impide que el cliente arrancar: las
    /// instalaciones son un complemento del cliente y no su motivo de vida (AD-1).
    /// </para>
    /// </summary>
    /// <param name="startup">Outcome of the startup of the client.</param>
    /// <param name="correlationScope">Scope that correlates the log of the client.</param>
    /// <param name="loggerFactory">Factory of the log of the client.</param>
    /// <param name="confirmation">
    /// Port of the confirmation of the privileged operations, or <c>null</c> when the
    /// composition is built without interfaz (AC2).
    /// </param>
    /// <param name="progressReporter">
    /// Port of the publication of the progress of the installation, or <c>null</c> when
    /// the client cannot publish it. Sin publicador la instalación SIGUE: la proyección
    /// es un complemento y no su motivo de vida (AD-1, AD-22).
    /// </param>
    /// <param name="errorRecorder">
    /// Use case that REGISTERS the errors de la instalación, or <c>null</c> when the
    /// client does not register them. Sin registrador la instalación SIGUE: el
    /// diagnóstico es un complemento y no su motivo de vida (AD-1, AD-11, NFR-7).
    /// </param>
    /// <returns>The composition, or <c>null</c> when it could not be opened.</returns>
    private static InstallationComposition? OpenInstallations(
        ClientStartupResult startup,
        IClientCorrelationScope correlationScope,
        ILoggerFactory loggerFactory,
        IPrivilegedOperationConfirmation? confirmation = null,
        IInstallationProgressReporter? progressReporter = null,
        RecordClientError? errorRecorder = null)
    {
        ArgumentNullException.ThrowIfNull(startup);
        ArgumentNullException.ThrowIfNull(correlationScope);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        if (string.IsNullOrWhiteSpace(startup.ServerAddress))
        {
            return null;
        }

        InstallationComposition? composition = null;

        try
        {
            // AD-11: la fila de la instalación vive en la base de datos del cliente, así
            // que la composición abre su propio acceso a ella.
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = startup.StateDatabasePath,
                Pooling = false,
            }.ToString();

            var options = new DbContextOptionsBuilder<ClientStateDbContext>()
                .UseSqlite(
                    connectionString,
                    sqlite => sqlite.MigrationsAssembly(typeof(ClientStateDbContext).Assembly.GetName().Name))
                .Options;

            var context = new ClientStateDbContext(options);

            // AD-9: la composición comparte la MISMA instancia del layout que el
            // arranque, así que el resolvedor de rutas lee la raíz vigente en cada
            // llamada y el cambio del directorio de instalación surte efecto inmediato.
            var layout = startup.Layout;

            // AD-27: la validación de la autenticidad se hace contra el ancla de
            // confianza YA FIJADA, que vive en la base de datos del cliente. El store es
            // el que la lee, así que la composición lo recibe como el port que es.
            //
            // AC5: el store recibe el layout para que el error de una instalación
            // fallida tenga su sumidero durable y la acción «Reintentar» sobreviva al
            // reinicio del cliente (NFR-9).
            var stateStore = new ClientStateStore(context, layout);

            // AC1, UX-DR-20: la REPARACIÓN resuelve el paquete de la VERSIÓN INSTALADA en
            // el snapshot del catálogo, así que la composición abre su propio acceso a él
            // sobre la MISMA base de datos (AD-11, AD-22).
            var snapshotStore = new CatalogSnapshotStore(context);

            composition = InstallationComposition.Create(
                new Uri(startup.ServerAddress, UriKind.Absolute),
                correlationScope,
                layout,
                stateStore,
                stateStore,
                loggerFactory,
                confirmation,
                snapshotStore,
                progressReporter,
                errorRecorder);
        }
        catch (Exception exception)
        {
            // El cliente no puede instalar, pero sigue siendo un cliente que arranca: la
            // ventana se abre y el catálogo sigue disponible (AD-1).
            loggerFactory
                .CreateLogger("LANPartyHub.Client.Install")
                .LogWarning(
                    exception,
                    "No se pudo abrir la composición de las instalaciones: el cliente sigue arrancando sin ella.");

            composition?.Dispose();

            composition = null;
        }

        return composition;
    }

    /// <summary>
    /// Opens the composition of the launcher of the videogames (AD-19, AC1).
    ///
    /// <para>
    /// La composición se abre SIN servidor: el lanzador lee la instalación que el
    /// cliente ya registró y el manifiesto firmado que ya está en la caché local, así que
    /// un cliente sin servidor puede lanzar lo que ya tiene instalado (AD-3, AD-19).
    /// </para>
    /// <para>
    /// Un fallo al abrir la composición NO impide que el cliente arrancar: el lanzador es
    /// un complemento del cliente y no su motivo de vida (AD-1).
    /// </para>
    /// </summary>
    /// <param name="startup">Outcome of the startup of the client.</param>
    /// <param name="loggerFactory">Factory of the log of the client.</param>
    /// <param name="errorRecorder">
    /// Use case that REGISTERS the errors del lanzamiento, or <c>null</c> when the client
    /// does not register them. Sin registrador el lanzamiento SIGUE: el diagnóstico es un
    /// complemento y no su motivo de vida (AD-1, AD-11, NFR-7).
    /// </param>
    /// <returns>The composition, or <c>null</c> when it could not be opened.</returns>
    private static GameLauncherComposition? OpenGameLauncher(
        ClientStartupResult startup,
        ILoggerFactory loggerFactory,
        RecordClientError? errorRecorder = null)
    {
        ArgumentNullException.ThrowIfNull(startup);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        GameLauncherComposition? composition = null;

        try
        {
            // AD-11: la fila de la instalación vive en la base de datos del cliente, así
            // que la composición abre su propio acceso a ella.
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = startup.StateDatabasePath,
                Pooling = false,
            }.ToString();

            var options = new DbContextOptionsBuilder<ClientStateDbContext>()
                .UseSqlite(
                    connectionString,
                    sqlite => sqlite.MigrationsAssembly(typeof(ClientStateDbContext).Assembly.GetName().Name))
                .Options;

            var context = new ClientStateDbContext(options);

            // AD-9: la composición comparte la MISMA instancia del layout que el
            // arranque, así que el resolvedor de rutas lee la raíz vigente en cada
            // llamada.
            var layout = startup.Layout;

            composition = new GameLauncherComposition(
                new ClientStateStore(context, layout),
                new CatalogSnapshotStore(context),
                new ManifestCache(layout),
                new InstallPathResolver(layout),
                loggerFactory,
                errorRecorder);
        }
        catch (Exception exception)
        {
            // El cliente no puede lanzar juegos, pero sigue siendo un cliente que
            // arranca: la ventana se abre y el catálogo sigue disponible (AD-1).
            loggerFactory
                .CreateLogger("LANPartyHub.Client.GameLauncher")
                .LogWarning(
                    exception,
                    "No se pudo abrir la composición del lanzador de juegos: el cliente sigue arrancando sin ella.");

            composition = null;
        }

        return composition;
    }

    /// <summary>
    /// Opens the composition of the discovery of the event server (AD-3, AC4).
    ///
    /// <para>
    /// Un fallo al abrir la composición NO impide que el cliente arranque: el descubrimiento
    /// automático es una de las dos vías de conexión y la dirección manual es la otra, que
    /// sigue operativa siempre (AC4, AD-3).
    /// </para>
    /// </summary>
    /// <param name="correlationScope">
    /// Scope that correlates the log of the client and that carries the identifiers that
    /// the requests to the server have to declare (AD-15, AD-23).
    /// </param>
    /// <param name="loggerFactory">Factory of the log of the client.</param>
    /// <param name="stateDatabasePath">
    /// Absolute path of the SQLite file of the client, where the ancla de confianza fijada
    /// lives (AD-27).
    /// </param>
    /// <returns>The composition, or <c>null</c> when it could not be opened.</returns>
    private static ConnectionComposition? OpenConnection(
        IClientCorrelationScope correlationScope,
        ILoggerFactory loggerFactory,
        string? stateDatabasePath = null)
    {
        ArgumentNullException.ThrowIfNull(correlationScope);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        ConnectionComposition? composition = null;

        try
        {
            // AD-15, AD-23: el sondeo de las versiones habla con la superficie del
            // cliente del servidor, y esa superficie EXIGE el identificador del equipo en
            // cada petición. La composición recibe el scope ya abierto con el
            // identificador canónico, así que la sonda estampa un identificador real y el
            // servidor puede pronunciarse sobre la compatibilidad.
            composition = new ConnectionComposition(correlationScope, loggerFactory, stateDatabasePath);
        }
        catch (Exception exception)
        {
            // El cliente no puede descubrir servidores, pero sigue siendo un cliente que
            // arranca: la dirección manual es la vía permanente y equivalente (AC4, AD-3).
            loggerFactory
                .CreateLogger("LANPartyHub.Client.Connection")
                .LogWarning(
                    exception,
                    "No se pudo abrir el descubrimiento por mDNS: el cliente sigue arrancando con la dirección manual.");

            composition = null;
        }

        return composition;
    }

    /// <summary>
    /// Reports a failure of the startup. The client is a graphical application and it has no
    /// console when the player launches it from the desktop, so the failure is also shown in a
    /// window that the player can read.
    /// </summary>
    private static void ReportStartupFailure(string message)
    {
        Console.Error.WriteLine(message);

        try
        {
            BuildAvaloniaApp(startupError: message).StartWithClassicDesktopLifetime([]);
        }
        catch (Exception)
        {
            // There is no desktop to draw the window on, so the message stays in the log.
        }
    }
}

/// <summary>
/// Source of the canonical identifier of the client, sobre la identidad que el ARRANQUE
/// ya conoce (AD-23).
///
/// <para>
/// El identificador lo emitió el cliente una sola vez y lo persistió, así que la raíz de
/// composición lo entrega ya resuelto y ningún caso de uso lo inventa (AD-23).
/// </para>
/// </summary>
internal sealed class StartupClientIdentity : ClientIdentityProvider
{
    /// <summary>Creates the source over the identity of the startup.</summary>
    /// <param name="identity">Identity of the team.</param>
    public StartupClientIdentity(LANPartyHub.Client.Domain.ClientIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        Identity = identity;
    }

    /// <summary>Identity of the team.</summary>
    public LANPartyHub.Client.Domain.ClientIdentity Identity { get; }

    /// <inheritdoc />
    public string CanonicalId => Identity.CanonicalId;
}

/// <summary>
/// Aviso de que el estado del cliente cambió, sobre la composición del REGISTRO
/// (AD-22, FR-10).
///
/// <para>
/// La composición del REGISTRO es la que publica el estado COMPLETO del cliente —las
/// instalaciones que tiene y las operaciones en curso—, así que es la que recibe el
/// aviso del coordinador de las descargas cuando una descarga empieza o termina
/// (AD-18, AD-22).
/// </para>
/// <para>
/// El adaptador NUNCA impide que la descarga siga: el registro ya captura sus propios
/// fallos, así que el aviso solo llega al servidor cuando se puede (AD-1, AD-22).
/// </para>
/// </summary>
internal sealed class RegistrationStateChangeNotifier : IClientStateChangeNotifier
{
    private readonly ClientRegistrationComposition _registration;

    /// <summary>Creates the notifier over the composition of the registration.</summary>
    /// <param name="registration">Composition of the registration of the team.</param>
    public RegistrationStateChangeNotifier(ClientRegistrationComposition registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        _registration = registration;
    }

    /// <inheritdoc />
    public Task NotifyAsync(CancellationToken cancellationToken) =>
        _registration.PublishCurrentStateAsync(cancellationToken);
}
