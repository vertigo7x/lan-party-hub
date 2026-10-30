using System.Net;
using System.Text.Json;
using LANPartyHub.Client.Application.Catalog;
using LANPartyHub.Client.Application.CatalogSync;
using LANPartyHub.Client.Host;
using LANPartyHub.Client.Infrastructure.Networking;
using LANPartyHub.Client.Infrastructure.Persistence;
using LANPartyHub.Client.Presentation.Catalog;
using LANPartyHub.Server.Application.Packages;
using LANPartyHub.Server.Host;
using LANPartyHub.Server.Infrastructure.Persistence;
using LANPartyHub.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LANPartyHub.IntegrationTests;

/// <summary>
/// The surface of the catalog of the player over the real server and the real SQLite file
/// of the client (AC1, AC2, AC3, AC4, AC5, AD-16).
///
/// <para>
/// The tests start the real host of the event, publish videogames in it, synchronize the
/// real client over a real <c>state.sqlite</c> and then filter the surface over the
/// snapshot that the client applied. That is the only way to observe that the filtering
/// works over the catalog that the server really published, and NOT over a fabricated
/// snapshot (AD-16, AD-26).
/// </para>
/// <para>
/// The filtering is LOCAL: the surface NEVER goes back to the server, so the search works
/// with the server down over the last snapshot that the client applied (AC3, AD-5).
/// </para>
/// </summary>
[Collection(RealHostCollection.Name)]
public sealed class CatalogSurfaceTests : IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly List<string> _temporaryFolders = [];

    /// <summary>
    /// Writes a query in the search field and waits for the delay of 200 ms of the search
    /// to elapse, so the filter is applied (AC1, UX-DR-9).
    /// </summary>
    /// <param name="viewModel">View model of the surface.</param>
    /// <param name="searchText">Query that the player typed.</param>
    /// <returns>A task that completes when the filter has been applied.</returns>
    private static async Task SearchAsync(CatalogSurfaceViewModel viewModel, string searchText)
    {
        viewModel.SearchText = searchText;

        // El filtro se aplica al escribir, con 200 ms de retardo, y el reloj real es el
        // que mide el retardo (AC1, UX-DR-9).
        await Task.Delay(
            CatalogSurfaceViewModel.SearchDelayMilliseconds + 100,
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task The_published_videogames_are_synchronized_and_the_surface_shows_them()
    {
        await using var host = await TestServer.StartAsync(
            CreateTemporaryDataDirectory(),
            TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        await PublishGameAsync(client, host.DataDirectory, "Counter-Strike 2", "Shooter");
        await PublishGameAsync(client, host.DataDirectory, "Minecraft", "Sandbox");

        var viewModel = await SynchronizeAsync(host);

        Assert.Equal(2, viewModel.Games.Count);
        Assert.Equal(["Sandbox", "Shooter"], viewModel.Categories);
    }

    [Fact]
    public async Task The_surface_filters_by_name_over_the_snapshot_that_the_client_applied()
    {
        await using var host = await TestServer.StartAsync(
            CreateTemporaryDataDirectory(),
            TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        await PublishGameAsync(client, host.DataDirectory, "Counter-Strike 2", "Shooter");
        await PublishGameAsync(client, host.DataDirectory, "Minecraft", "Sandbox");

        var viewModel = await SynchronizeAsync(host);

        await SearchAsync(viewModel, "min");

        var game = Assert.Single(viewModel.Games);

        Assert.Equal("Minecraft", game.Name);
    }

    [Fact]
    public async Task The_surface_filters_by_category_over_the_snapshot_that_the_client_applied()
    {
        await using var host = await TestServer.StartAsync(
            CreateTemporaryDataDirectory(),
            TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        await PublishGameAsync(client, host.DataDirectory, "Counter-Strike 2", "Shooter");
        await PublishGameAsync(client, host.DataDirectory, "Minecraft", "Sandbox");

        var viewModel = await SynchronizeAsync(host);

        viewModel.SelectedCategory = "Sandbox";

        var game = Assert.Single(viewModel.Games);

        Assert.Equal("Minecraft", game.Name);
    }

    [Fact]
    public async Task The_surface_does_not_go_back_to_the_server_when_it_filters()
    {
        await using var host = await TestServer.StartAsync(
            CreateTemporaryDataDirectory(),
            TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        await PublishGameAsync(client, host.DataDirectory, "Counter-Strike 2", "Shooter");
        await PublishGameAsync(client, host.DataDirectory, "Minecraft", "Sandbox");

        var viewModel = await SynchronizeAsync(host);

        // El host se detiene: la superficie sigue filtrando sobre el snapshot que el
        // cliente aplicó, porque el filtrado es LOCAL (AC1, AD5, AD-26).
        await host.DisposeAsync();

        await SearchAsync(viewModel, "min");

        var game = Assert.Single(viewModel.Games);

        Assert.Equal("Minecraft", game.Name);
    }

    [Fact]
    public async Task An_empty_catalog_shows_the_exact_text_and_the_action_of_connecting()
    {
        await using var host = await TestServer.StartAsync(
            CreateTemporaryDataDirectory(),
            TestContext.Current.CancellationToken);

        // El servidor está conectado pero el catálogo no tiene ningún videojuego
        // todavía (AC4, UX-DR-28).
        var viewModel = await SynchronizeAsync(host);

        Assert.True(viewModel.IsCatalogEmpty);
        Assert.Equal("El catálogo está vacío.", viewModel.EmptyCatalogMessage);
        Assert.Equal("Sincronizar ahora", viewModel.EmptyCatalogActionText);
    }

    [Fact]
    public async Task The_catalog_that_the_client_applied_survives_the_fall_of_the_server()
    {
        await using var host = await TestServer.StartAsync(
            CreateTemporaryDataDirectory(),
            TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        await PublishGameAsync(client, host.DataDirectory, "Counter-Strike 2", "Shooter");

        var viewModel = await SynchronizeAsync(host);

        Assert.Single(viewModel.Games);

        // El servidor se cae: el cliente conserva el último catálogo que aplicó y la
        // superficie sigue mostrando el catálogo vacío de la vista del cliente, con la
        // acción «Conectar al servidor» (AC3, AC4, UX-DR-32).
        await host.DisposeAsync();

        var pending = await SynchronizeAsync(host, connected: false);

        Assert.False(pending.IsServerConnected);
        Assert.Equal("Conectar al servidor", pending.EmptyCatalogActionText);
    }

    [Fact]
    public async Task A_search_without_results_offers_the_action_of_clearing_the_filters()
    {
        await using var host = await TestServer.StartAsync(
            CreateTemporaryDataDirectory(),
            TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        await PublishGameAsync(client, host.DataDirectory, "Counter-Strike 2", "Shooter");

        var viewModel = await SynchronizeAsync(host);

        await SearchAsync(viewModel, "quake");

        Assert.True(viewModel.ShowsNoResults);
        Assert.Equal("Sin resultados para «quake».", viewModel.NoResultsMessage);
        Assert.Equal("Quitar filtros", viewModel.ClearFiltersActionText);

        viewModel.ClearFilters();

        Assert.False(viewModel.ShowsNoResults);
        Assert.Single(viewModel.Games);
    }

    [Fact]
    public async Task The_pagination_of_the_surface_pages_the_videogames_of_the_catalog()
    {
        await using var host = await TestServer.StartAsync(
            CreateTemporaryDataDirectory(),
            TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        for (var index = 0; index < CatalogFilter.PageSize + 1; index++)
        {
            await PublishGameAsync(client, host.DataDirectory, $"Juego {index:00}", "Shooter");
        }

        var viewModel = await SynchronizeAsync(host);

        Assert.Equal(2, viewModel.PageCount);
        Assert.Equal(CatalogFilter.PageSize, viewModel.Games.Count);

        viewModel.GoForward();

        Assert.Equal(2, viewModel.CurrentPage);
        Assert.Single(viewModel.Games);
    }

    [Fact]
    public async Task The_surface_receives_the_state_that_the_startup_published_before_the_window_existed()
    {
        await using var host = await TestServer.StartAsync(
            CreateTemporaryDataDirectory(),
            TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        await PublishGameAsync(client, host.DataDirectory, "Counter-Strike 2", "Shooter");
        await PublishGameAsync(client, host.DataDirectory, "Minecraft", "Sandbox");

        var dataFolder = CreateTemporaryDataDirectory();
        var databasePath = Path.Combine(dataFolder, ClientDataLayout.StateDatabaseFileName);

        var options = new DbContextOptionsBuilder<ClientStateDbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False")
            .Options;

        await using (var context = new ClientStateDbContext(options))
        {
            await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
        }

        using var correlation = new ClientCorrelationFactory(NullLoggerFactory.Instance);

        correlation.Open("0193f2a1b2c34d4e5f60718293a4b5c6");

        await using var composition = new CatalogSyncComposition(
            new Uri($"http://{host.BoundAddress}:{host.Port}"),
            databasePath,
            correlation,
            NullLoggerFactory.Instance);

        // La primera sincronización ocurre durante el ARRANQUE, antes de que exista la
        // ventana: el estado que lleva el snapshot se publica sin ningún suscriptor.
        var state = await composition.Service.SynchronizeAsync(TestContext.Current.CancellationToken);

        composition.Publish(state);

        // La ventana se suscribe DESPUÉS, como hace App.cs: sin la repetición del último
        // estado, el catálogo nunca llegaría a la superficie (AC1).
        var viewModel = new CatalogSurfaceViewModel();

        composition.StateChanged += published => viewModel.ApplySnapshot(published.Snapshot, published.Synchronized);

        Assert.Equal(2, viewModel.Games.Count);
        Assert.Equal(["Sandbox", "Shooter"], viewModel.Categories);
    }

    [Fact]
    public async Task The_surface_offers_to_connect_when_the_server_is_not_reachable_at_the_startup()
    {
        var dataFolder = CreateTemporaryDataDirectory();
        var databasePath = Path.Combine(dataFolder, ClientDataLayout.StateDatabaseFileName);

        var options = new DbContextOptionsBuilder<ClientStateDbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False")
            .Options;

        await using (var context = new ClientStateDbContext(options))
        {
            await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
        }

        using var correlation = new ClientCorrelationFactory(NullLoggerFactory.Instance);

        correlation.Open("0193f2a1b2c34d4e5f60718293a4b5c6");

        // El servidor NO está disponible: el arranque no puede suscribirse al aviso de
        // cambios ni leer el snapshot (AC3, AC4).
        await using var composition = new CatalogSyncComposition(
            new Uri("http://127.0.0.1:1"),
            databasePath,
            correlation,
            NullLoggerFactory.Instance);

        var viewModel = new CatalogSurfaceViewModel();

        composition.StateChanged += published => viewModel.ApplySnapshot(published.Snapshot, published.Synchronized);

        // El arranque publica el estado pendiente AUNQUE el servidor no responda: la
        // superficie sabe que no hay conexión y ofrece «Conectar al servidor» (AC4).
        await composition.StartAsync(TestContext.Current.CancellationToken);

        Assert.False(viewModel.IsServerConnected);
        Assert.True(viewModel.IsCatalogEmpty);
        Assert.Equal("El catálogo está vacío.", viewModel.EmptyCatalogMessage);
        Assert.Equal("Conectar al servidor", viewModel.EmptyCatalogActionText);
    }

    /// <summary>
    /// Synchronizes the real client over a real <c>state.sqlite</c> and builds the
    /// surface over the snapshot that the client applied.
    /// </summary>
    /// <param name="host">Host of the event server.</param>
    /// <param name="connected">
    /// Whether the server is reachable. When it is not, the client falls back to the last
    /// snapshot that it applied.
    /// </param>
    /// <returns>The view model of the surface.</returns>
    private async Task<CatalogSurfaceViewModel> SynchronizeAsync(
        TestServer host,
        bool connected = true)
    {
        var dataFolder = CreateTemporaryDataDirectory();
        var databasePath = Path.Combine(dataFolder, ClientDataLayout.StateDatabaseFileName);

        var options = new DbContextOptionsBuilder<ClientStateDbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False")
            .Options;

        await using var context = new ClientStateDbContext(options);

        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);

        var store = new CatalogSnapshotStore(context);

        var applier = new CatalogSnapshotApplier(
            store,
            NullLogger<CatalogSnapshotApplier>.Instance);

        // Con el servidor conectado la lectura va al host real; sin él, la lectura va a
        // un puerto que no tiene ningún servidor, y el cliente conserva el último
        // snapshot que aplicó (AC3, UX-DR-32).
        var address = connected
            ? $"http://{host.BoundAddress}:{host.Port}"
            : "http://127.0.0.1:1";

        // La superficie del cliente exige el identificador del cliente en cada petición,
        // y el manejador real de la correlación es el que lo estampa (AD-15, AD-23).
        using var correlation = new ClientCorrelationFactory(NullLoggerFactory.Instance);

        correlation.Open("0193f2a1b2c34d4e5f60718293a4b5c6");

        using var httpClient = new HttpClient(
            new CorrelationHeadersHandler(correlation)
            {
                InnerHandler = new HttpClientHandler(),
            })
        {
            BaseAddress = new Uri(address),
        };

        var service = new CatalogSyncService(
            new CatalogHttpClient(httpClient),
            store,
            new UnreachableNotifier(),
            applier,
            NullLogger<CatalogSyncService>.Instance);

                var state = await service.SynchronizeAsync(TestContext.Current.CancellationToken);

        var viewModel = new CatalogSurfaceViewModel();

        viewModel.ApplySnapshot(state.Snapshot, state.Synchronized);

        return viewModel;
    }

    /// <summary>
    /// Registers a videogame in the catalog of the real server.
    /// </summary>
    /// <param name="client">Client of the host of the event.</param>
    /// <param name="name">Human readable name of the videogame.</param>
    /// <param name="categories">Categories of the videogame.</param>
    /// <returns>A task that completes when the videogame is registered.</returns>
    /// <summary>
    /// Registers a videogame in the catalog of the real server and publishes a package for
    /// it, so the videogame is PUBLISHED and it is part of the snapshot that the client
    /// synchronizes (AD-26).
    ///
    /// <para>
    /// Un videogame sin paquete publicado no está en el snapshot: la publicación es la
    /// que hace visible el videogame en el catálogo del cliente (AD-26).
    /// </para>
    /// </summary>
    /// <param name="client">Client of the host of the event.</param>
    /// <param name="dataDirectory">Folder of the data of the host.</param>
    /// <param name="name">Human readable name of the videogame.</param>
    /// <param name="categories">Categories of the videogame.</param>
    /// <returns>A task that completes when the videogame is published.</returns>
    private static async Task PublishGameAsync(
        HttpClient client,
        string dataDirectory,
        string name,
        params string[] categories)
    {
        // Los dieciocho metadata de FR-1 son obligatorios, y las colecciones de them
        // no pueden estar vacías: el registro los exige uno por uno (FR-1, AD-34).
        var request = new GameRequest
        {
            SchemaVersion = GamesEndpoint.SupportedSchemaVersion,
            Game = new GameDescriptor(
                string.Empty,
                name,
                "Descripción del videojuego.",
                categories.Length > 0 ? categories : ["Shooter"],
                ["lan"],
                "cover.png",
                ["captura-1.png"],
                "1.0.0",
                1_000_000,
                1,
                4,
                "Windows",
                "Servidor",
                "Portable",
                new InstallationManifest.LaunchSettings("game.exe", ["-novid"], "game"),
                ["DirectX 12"],
                "Propietaria",
                "Solo para el evento"),
        };

        using var response = await client.PostAsync(
            GamesEndpoint.Route,
            new StringContent(
                GamesEndpoint.Serialize(request),
                System.Text.Encoding.UTF8,
                "application/json"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var descriptor = JsonSerializer.Deserialize<GameDescriptor>(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            SerializerOptions);

        Assert.NotNull(descriptor);

        await PublishPackageAsync(client, dataDirectory, descriptor!.GameId, name);
    }

    /// <summary>
    /// Publishes a package for a videogame, so the videogame enters the snapshot that the
    /// client synchronizes (AD-26).
    /// </summary>
    /// <param name="client">Client of the host of the event.</param>
    /// <param name="dataDirectory">Folder of the data of the host.</param>
    /// <param name="gameId">Identifier of the videogame.</param>
    /// <param name="name">Name of the videogame, used to build a unique package.</param>
    /// <returns>A task that completes when the package is published.</returns>
    private static async Task PublishPackageAsync(
        HttpClient client,
        string dataDirectory,
        string gameId,
        string name)
    {
        var packageId = $"0195f3b2-4c8a-7c1e-9d33-{Math.Abs(name.GetHashCode()):x8}";

        var bytes = System.Text.Encoding.UTF8.GetBytes($"contenido del paquete de {name}");

        var packages = Path.Combine(dataDirectory, DataDirectory.PackagesFolderName);

        Directory.CreateDirectory(packages);

        File.WriteAllBytes(Path.Combine(packages, packageId), bytes);

        var sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))
            .ToLowerInvariant();

        var request = new PackageRequest
        {
            SchemaVersion = PackagesEndpoint.SupportedSchemaVersion,
            GameId = gameId,
            Package = new PackageDescriptor(
                packageId,
                gameId,
                "1.0.0",
                bytes.LongLength,
                0,
                sha256,
                string.Empty),
            Manifest = new InstallationManifest(
                InstallationManifestSchema.Version,
                packageId,
                "1.0.0",
                bytes.LongLength,
                sha256,
                new InstallationManifest.LaunchSettings("game\\game.exe", ["-novid"], "game"),
                [
                    new InstallationManifest.CreateDirectory("game"),
                    new InstallationManifest.Extract("game.zip", "game"),
                ]),
        };

        using var response = await client.PostAsync(
            PackagesEndpoint.Route,
            new StringContent(
                JsonSerializer.Serialize(request, SerializerOptions),
                System.Text.Encoding.UTF8,
                "application/json"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    /// <summary>
    /// Creates a temporary data folder.
    /// </summary>
    /// <returns>The path of the folder.</returns>
    private string CreateTemporaryDataDirectory()
    {
        var folder = Directory.CreateTempSubdirectory("lan-party-hub-catalog-").FullName;

        _temporaryFolders.Add(folder);

        return folder;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var folder in _temporaryFolders)
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
                // A file that the operating system still holds does not fail the test.
            }
        }
    }

    /// <summary>
    /// Notifier that never receives a trigger: the tests synchronize explicitly, and the
    /// push of the server is not what this story observes (AD-5).
    /// </summary>
    private sealed class UnreachableNotifier : LANPartyHub.Client.Application.Ports.ICatalogChangeNotifier
    {
        public Task SubscribeAsync(
            Func<CatalogChanged, CancellationToken, Task> handler,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
