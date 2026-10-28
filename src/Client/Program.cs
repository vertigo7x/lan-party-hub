using Avalonia;
using LANPartyHub.Client.Application;
using LANPartyHub.Client.Host;
using LANPartyHub.Client.Infrastructure.Configuration;
using LANPartyHub.Client.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Client;

/// <summary>
/// Composition root of the desktop client (AD-1, AD-9, AD-11, AD-17, AD-23, AD-24).
///
/// <para>
/// Story 1.3 fixes the startup of the client: the effective configuration with the precedence
/// flags &gt; env &gt; file, the layout of the data folder, the migrations of the database of
/// the client, the identity of the team and the reconciliation of the persisted progress. The
/// window itself belongs to the following stories.
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
        try
        {
            var startup = StartClientAsync(args).GetAwaiter().GetResult();

            BuildAvaloniaApp(startup.Settings).StartWithClassicDesktopLifetime(args);

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
    }

    /// <summary>Builds the Avalonia application builder of the client.</summary>
    /// <param name="settings">Settings of the client, or <c>null</c> when there are none.</param>
    /// <param name="startupError">Message of a failure of the startup, or <c>null</c> when the startup succeeded.</param>
    public static AppBuilder BuildAvaloniaApp(ClientSettings? settings = null, string? startupError = null) =>
        AppBuilder.Configure(() => new App(settings, startupError)).UsePlatformDetect();

    /// <summary>
    /// Runs the startup of the client and returns its outcome.
    /// </summary>
    /// <param name="args">Arguments of the process; only <c>--server-address</c>, <c>--install-directory</c> and <c>--theme</c> are accepted.</param>
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
    /// The composition is closed over the startup: the database of the client and the log
    /// live exactly as long as it takes. The stories that need them alive (the connection, the
    /// catalogue and the downloads) open their own composition over the same layout.
    /// </remarks>
    public static async Task<ClientStartupResult> StartClientAsync(
        string[] args,
        string? dataFolder = null,
        string? baseDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);

        var deploymentDirectory = baseDirectory ?? AppContext.BaseDirectory;
        var dataFolderPath = dataFolder ?? ClientDataLayout.DefaultDataFolder;

        // AD-12, AR-23: the command line is parsed strictly and it is the source of the
        // highest priority of the configuration.
        var commandLine = ClientCommandLine.Parse(args);

        // The effective configuration is read out of the three sources, in the order
        // file > env > flags.
        var effectiveConfiguration = ClientConfiguration.Resolve(deploymentDirectory, dataFolderPath, commandLine);

        using var loggerFactory = LoggerFactory.Create(logging =>
        {
            logging.AddConsole();
            logging.SetMinimumLevel(LogLevel.Information);
        });

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
