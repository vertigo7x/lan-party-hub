using Avalonia;
using LANPartyHub.Client.Application;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Host;
using LANPartyHub.Client.Infrastructure.Configuration;
using LANPartyHub.Client.Infrastructure.Logging;
using LANPartyHub.Client.Infrastructure.Persistence;
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
            correlationScope = new ClientCorrelationFactory(loggerFactory);

            var startup = StartClientAsync(args, correlationScope, loggerFactory).GetAwaiter().GetResult();

            // AD-5, AD-26: la composición de la sincronización del catálogo vive mientras
            // vive la ventana del jugador, porque la sincronización tiene que reaccionar al
            // disparo del hub mientras el cliente corre. Se abre DESPUÉS del startup, que
            // ya ha aplicado las migraciones de la base de datos del cliente (AD-11).
            catalogSync = OpenCatalogSync(startup, correlationScope, loggerFactory);

            // AD-3: la composición del descubrimiento vive mientras vive la ventana del
            // jugador, porque la superficie de Conexión consume su resultado. Un fallo al
            // abrirla NO impide que el cliente arranque: la vía manual sigue operativa
            // (AC4).
            connection = OpenConnection(loggerFactory);

            BuildAvaloniaApp(startup.Settings, catalogSync: catalogSync, connection: connection)
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
            correlationScope?.Dispose();
            loggerFactory?.Dispose();
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
    public static AppBuilder BuildAvaloniaApp(
        ClientSettings? settings = null,
        string? startupError = null,
        CatalogSyncComposition? catalogSync = null,
        ConnectionComposition? connection = null) =>
        AppBuilder.Configure(() => new App(settings, startupError, catalogSync, connection)).UsePlatformDetect();

    /// <summary>
    /// Runs the startup of the client and returns its outcome.
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
    /// <returns>The composition, or <c>null</c> when it could not be opened.</returns>
    private static CatalogSyncComposition? OpenCatalogSync(
        ClientStartupResult startup,
        IClientCorrelationScope correlationScope,
        ILoggerFactory loggerFactory)
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
                loggerFactory);

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
    /// Opens the composition of the discovery of the event server (AD-3, AC4).
    ///
    /// <para>
    /// Un fallo al abrir la composición NO impide que el cliente arranque: el descubrimiento
    /// automático es una de las dos vías de conexión y la dirección manual es la otra, que
    /// sigue operativa siempre (AC4, AD-3).
    /// </para>
    /// </summary>
    /// <param name="loggerFactory">Factory of the log of the client.</param>
    /// <returns>The composition, or <c>null</c> when it could not be opened.</returns>
    private static ConnectionComposition? OpenConnection(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        ConnectionComposition? composition = null;

        try
        {
            composition = new ConnectionComposition(loggerFactory);
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
