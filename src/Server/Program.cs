using LANPartyHub.Server.Host;
using LANPartyHub.Server.Infrastructure.Networking;
using LANPartyHub.Server.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
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

try
{
    var app = await Program.BuildServerAsync(args);

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

        // AD-15: the log of the server is JSON from the very first startup.
        builder.Logging.ClearProviders();
        builder.Logging.AddJsonConsole();

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
        builder.Services.AddSingleton<HostMetrics>();
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

        // AD-15: the metrics are published locally, on the same HTTP port.
        MetricsEndpoint.Map(app, app.Services.GetRequiredService<HostMetrics>());

        return app;
    }
}
