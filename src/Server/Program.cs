using Avalonia;
using LANPartyHub.Server.Application.Catalog;
using LANPartyHub.Server.Application.Packages;
using LANPartyHub.Server.Application.Ports;
using LANPartyHub.Server.Host;
using LANPartyHub.Server.Infrastructure.Http;
using LANPartyHub.Server.Infrastructure.Logging;
using LANPartyHub.Server.Infrastructure.Networking;
using LANPartyHub.Server.Infrastructure.Persistence;
using LANPartyHub.Server.Infrastructure.Signing;
using LANPartyHub.Server.Presentation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Composition root of the event server (AD-1, AD-5).
//
// Story 1.2 fixes the startup of the server: the effective configuration with the precedence
// flags > env > file, the data folder with its SQLite file and its package repository, the
// network surface reduced to the private interfaces, the local metrics endpoint and the mDNS
// advertisement. The REST surface of the catalog, the packages and the clients belongs to the
// stories of the epics 2, 3, 5 and 9.
//
// Story 1.4 fixes the observability of the server: the log is JSON with scopes, the surface
// of the client is correlated with the client identifier and the one of the unit of work,
// and the metrics of the host are instrumented (AD-15, AD-23).

try
{
    var app = await Program.BuildServerAsync(args);

    // AD-1: the server is a single deployable, so the window of the organizer opens in
    // the same process as the host HTTP. The window lives in its own thread, and the
    // failure of the desktop never takes the host down: the server keeps serving the
    // clients of the LAN even when there is no graphical session to draw the window on.
    Program.StartOrganizerWindow(app.Services);

    await app.RunAsync();

    return 0;
}
catch (ServerStartupException exception)
{
    await Console.Error.WriteLineAsync(exception.Message);

    return 1;
}
catch (Exception exception)
{
    // Any other failure is not a decision of the server, but a problem of the machine,
    // the network or the file system: the organizer gets one line and not a stack trace.
    await Console.Error.WriteLineAsync($"El servidor no pudo arrancar: {exception.Message}");

    return 1;
}

/// <summary>
/// Composition root of the event server. It is public so the integration level can start the
/// real host, with the real Kestrel and the real sockets (AD-16).
/// </summary>
public partial class Program
{
    /// <summary>
    /// Prefix of the surface of the client (AD-5). Every request under this prefix carries
    /// the identifier of the client and opens the correlation scope of its unit of work.
    /// </summary>
    public const string ClientSurfacePrefix = "/api/v1";

    /// <summary>
    /// Prefix of the surface of the administration (AD-30). The organizer is not a client
    /// of the LAN and does not carry the identifier of a client, so the surface of the
    /// administration is not covered by the middleware of the client. It is correlated all
    /// the same, by the identifier of the unit of work (AD-15).
    /// </summary>
    public const string AdminSurfacePrefix = "/api/v1/admin";

    /// <summary>
    /// Builds the host of the event server.
    /// </summary>
    /// <param name="args">Arguments of the process; only <c>--data-dir</c> and <c>--http-port</c> are accepted.</param>
    /// <param name="privateAddressProvider">
    /// Source of the private addresses to listen on. It defaults to the real resolver and
    /// exists so the case of a host without a private address can be exercised.
    /// </param>
    /// <param name="mdnsPublisher">
    /// Publisher of the mDNS records. It defaults to the real one and exists so the case of
    /// a network that blocks the multicast can be exercised.
    /// </param>
    /// <param name="baseDirectory">
    /// Folder of the deployment: it holds the configuration file and it is the base of the
    /// relative paths. It defaults to the folder of the binary.
    /// </param>
    /// <returns>The host, ready to run.</returns>
    /// <exception cref="ServerStartupException">The startup cannot continue.</exception>
    public static async Task<WebApplication> BuildServerAsync(
        string[] args,
        IPrivateAddressProvider? privateAddressProvider = null,
        IMdnsPublisher? mdnsPublisher = null,
        string? baseDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(args);

        var deploymentDirectory = baseDirectory ?? AppContext.BaseDirectory;

        // AD-12, AR-23: the command line is parsed strictly and it is the source of the
        // highest priority of the configuration.
        var commandLine = ServerCommandLine.Parse(args);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = [],
            ContentRootPath = deploymentDirectory,
            ApplicationName = typeof(Program).Assembly.GetName().Name,
        });

        // AD-15: the log of the server is JSON from the very first startup, and it carries
        // the scopes, so the correlation of the operation and the client travels in every
        // entry. The timestamps are UTC in ISO-8601, as the data convention fixes.
        builder.Logging.ClearProviders();
        builder.Logging.AddJsonConsole(options =>
        {
            options.IncludeScopes = true;
            options.TimestampFormat = "O";
            options.UseUtcTimestamp = true;
        });

        // AD-12, AR-23: the providers are registered in the order file > env > flags, and the
        // last provider that holds the key wins.
        builder.Configuration.Sources.Clear();
        ServerConfiguration.AddSources(builder.Configuration, commandLine);

        var configuration = ServerConfiguration.Resolve(builder.Configuration, deploymentDirectory);

        // AD-12: the server listens only on the private interfaces, so the endpoints of
        // Kestrel are the explicit Listen() calls below and nothing else. The mechanism is
        // PreferHostingUrls: while it is false, Kestrel keeps those endpoints and ignores
        // the hosting URLs; while it is true, it discards them and binds to the "urls"
        // address instead, which would take the hub to 0.0.0.0. The default is false, but
        // it is set explicitly here so the guarantee of AD-12 does not rest on a framework
        // default that could change.
        builder.WebHost.PreferHostingUrls(false);

        // AD-12: the server listens only on private interfaces.
        var addressProvider = privateAddressProvider ?? new PrivateAddressResolver();
        var privateAddresses = addressProvider.GetPrivateAddresses();

        if (privateAddresses.Count == 0)
        {
            throw new ServerStartupException(
                "El equipo no tiene ninguna interfaz privada operativa (RFC 1918 o ULA) en la que escuchar. "
                + "Enlazar a 0.0.0.0 expondría el hub fuera de la red del evento, así que el servidor no arranca.");
        }

        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            foreach (var address in privateAddresses)
            {
                kestrel.Listen(address, configuration.HttpPort);
            }
        });

        // AD-11, AD-17: the data folder holds the SQLite file of the server and the
        // repository of the packages, and it is created before the connection is opened.
        var dataDirectory = DataDirectory.Create(configuration.DataDirectory);

        // The connection string is built with its builder so a path that carries a semicolon
        // or a quote is quoted and the connection string stays well formed.
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dataDirectory.CatalogDatabasePath,
        }.ToString();

        builder.Services.AddSingleton(dataDirectory);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<HostMetrics>();
        builder.Services.AddSingleton<IMetricsCounters>(provider => provider.GetRequiredService<HostMetrics>());
        builder.Services.AddSingleton<CorrelationScopeFactory>();
        builder.Services.AddSingleton<IMdnsPublisher>(mdnsPublisher ?? new HaukcodeMdnsPublisher(
            Environment.MachineName,
            MdnsServiceAdvertiser.ServiceType,
            configuration.HttpPort,
            MdnsServiceAdvertiser.TxtProperties));
        builder.Services.AddSingleton<IMdnsServiceAdvertiser, MdnsServiceAdvertiser>();
        builder.Services.AddHostedService<MdnsAdvertisementService>();

        builder.Services.AddDbContext<CatalogDbContext>(options => options.UseSqlite(
            connectionString,
            sqlite => sqlite.MigrationsAssembly(typeof(CatalogDbContext).Assembly.GetName().Name)));

        // AD-1, AD-22: the module of the catalog of the server. The use cases depend on
        // the port of the repository and never on the adapter that implements it.
        builder.Services.AddScoped<IGameRepository, GameRepository>();
        builder.Services.AddScoped<RegisterGame>();
        builder.Services.AddScoped<ModifyGame>();
        builder.Services.AddScoped<ListGames>();

        // AD-1, AD-2, AD-7: the module of the distribution of packages of the server. The
        // signature is Ed25519 over the canonical form of the manifest, and the adapter
        // that holds the key is the only one that knows the cryptography: the use case
        // depends on the port and never on it.
        builder.Services.AddScoped<IPackageRepository, PackageRepository>();
        builder.Services.AddScoped<IPackageContentStore, PackageContentStore>();
        builder.Services.AddSingleton<IManifestSigner, Ed25519ManifestSigner>();
        builder.Services.AddScoped<PublishPackage>();
        builder.Services.AddScoped<ListPackages>();
        builder.Services.AddScoped<WithdrawGame>();

        var app = builder.Build();

        // AD-15: the effective configuration is published, because it is the surface the
        // organizer inspects when the server does not behave as expected.
        app.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("LANPartyHub.Server.Startup")
            .LogInformation(
                "Configuración efectiva: carpeta de datos {DataDirectory} y puerto HTTP {HttpPort}.",
                configuration.DataDirectory,
                configuration.HttpPort);

        // AD-11: the migrations are versioned and they are applied automatically at startup.
        // EnsureCreated is never used: it skips the history of the migrations.
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            await catalog.Database.MigrateAsync();

            var pending = (await catalog.Database.GetPendingMigrationsAsync()).ToArray();

            if (pending.Length > 0)
            {
                throw new ServerStartupException(
                    $"Quedan migraciones sin aplicar en la base de datos del servidor: {string.Join(", ", pending)}.");
            }
        }

        // AD-15, AD-23: the surface of the client is correlated. Every request that
        // reaches /api/v1 opens the scope of its unit of work and has to carry the
        // identifier of the client. The surface of the host, /metrics, is not the surface
        // of the client: the diagnosis of the server never depends on a client.
        //
        // AD-30: the surface of the administration is not the surface of the client. The
        // organizer is not a client of the LAN, so the predicate leaves /api/v1/admin
        // out of the branch of the client: every other route of /api/v1 keeps requiring
        // the identifier of the client, and the administration keeps its correlation by the
        // identifier of the unit of work.
        app.UseWhen(
            context => IsClientSurface(context.Request.Path),
            branch => branch.UseMiddleware<CorrelationMiddleware>());

        // AD-15: the surface of the administration is correlated by the identifier of the
        // unit of work. It does not ask for the identifier of the client, because the
        // organizer is not a client (AD-30).
        app.UseWhen(
            context => context.Request.Path.StartsWithSegments(AdminSurfacePrefix, StringComparison.OrdinalIgnoreCase),
            branch => branch.UseMiddleware<AdminCorrelationMiddleware>());

        // AD-15: the metrics are published locally, on the same HTTP port.
        MetricsEndpoint.Map(app, app.Services.GetRequiredService<HostMetrics>());

        // AD-5, AD-34: the administration of the videogames of the catalog.
        GamesEndpoint.Map(app);

        // AD-5, AD-7: the administration of the publication of the packages.
        PackagesEndpoint.Map(app);

        return app;
    }

    /// <summary>
    /// Tells whether a path belongs to the surface of the client. The surface of the
    /// administration is excluded, because the organizer is not a client of the LAN and
    /// does not carry the identifier of a client (AD-30).
    /// </summary>
    /// <param name="path">Path of the request.</param>
    /// <returns><c>true</c> when the path belongs to the surface of the client.</returns>
    private static bool IsClientSurface(PathString path) =>
        path.StartsWithSegments(ClientSurfacePrefix, StringComparison.OrdinalIgnoreCase)
        && !path.StartsWithSegments(AdminSurfacePrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Builds the Avalonia application builder of the server.
    /// </summary>
    /// <param name="services">Container of the host, with the use cases of the catalog.</param>
    /// <returns>The builder of the application of the organizer.</returns>
    public static AppBuilder BuildAvaloniaApp(IServiceProvider services) =>
        AppBuilder.Configure(() => new ServerApp(services)).UsePlatformDetect();

    /// <summary>
    /// Opens the window of the organizer in a thread of its own, so the host HTTP keeps
    /// serving the clients of the LAN while the interface is open (AD-1).
    ///
    /// <para>
    /// The failure of the desktop never takes the host down: a server that runs without a
    /// graphical session still serves the surface of la API, y la organizadora puede
    /// Catch it con las herramientas de la línea de órdenes.
    /// </para>
    /// </summary>
    /// <param name="services">Container of the host, with the use cases of the catalog.</param>
    public static void StartOrganizerWindow(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var thread = new Thread(() =>
        {
            try
            {
                BuildAvaloniaApp(services).StartWithClassicDesktopLifetime([]);
            }
            catch (Exception exception)
            {
                // La interfaz del organizador es un complemento del host, no su motivo de
                // vida: si el escritorio no está disponible, el servidor sigue sirviendo
                // la API y el fallo queda escrito en el log (AD-15).
                services
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("LANPartyHub.Server.Startup")
                    .LogWarning(
                        exception,
                        "No se pudo abrir la ventana del organizador: el servidor sigue sirviendo la API.");
            }
        })
        {
            IsBackground = true,
            Name = "LANPartyHub.Server.Organizer",
        };

        // El hilo de la interfaz necesita un apartamento de un solo hilo en Windows, que
        // es la plataforma del despliegue del MVP (AD-17). En el resto de plataformas el
        // modelo del hilo es el de por defecto y la llamada no existe (CA1416).
        if (OperatingSystem.IsWindows())
        {
            thread.SetApartmentState(ApartmentState.STA);
        }

        thread.Start();
    }
}
