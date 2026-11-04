using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using LANPartyHub.Client.Application;
using LANPartyHub.Client.Application.Downloads;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Domain;
using LANPartyHub.Client.Host;
using LANPartyHub.Client.Infrastructure;
using LANPartyHub.Client.Infrastructure.Networking;
using LANPartyHub.Client.Infrastructure.Persistence;
using LANPartyHub.Server.Application.Packages;
using LANPartyHub.Server.Host;
using LANPartyHub.Server.Infrastructure.Persistence;
using LANPartyHub.Shared.Contracts;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using ClientProgram = LANPartyHub.Client.Program;

namespace LANPartyHub.IntegrationTests;

/// <summary>
/// Survival of a download to a real restart of the client
/// (AC1, AC2, AC3, AD-9, AD-11, AD-20, AD-24, SM-2).
///
/// <para>
/// La prueba levanta el HOST REAL del servidor del evento con un paquete publicado, deja
/// al CLIENTE REAL descargar una parte de sus bytes, cierra la composición del cliente
/// (que es lo que hace un reinicio del equipo) y abre una NUEVA sobre la MISMA carpeta de
/// datos. El reinicio no es simulado con un doble: la base de datos es el archivo SQLite
/// de verdad y el parcial es el archivo de verdad (AD-16).
/// </para>
/// <para>
/// Lo que se observa es de las dos puntas: el servidor recibe el <c>Range</c> que empieza
/// en el offset persistido, y el cliente presenta la operación como «Reanudando desde …»
/// con ese mismo offset (AC2, AD-20, UX-DR-45).
/// </para>
/// </summary>
[Collection(RealHostCollection.Name)]
public sealed class DownloadRestartRecoveryTests : IDisposable
{
    /// <summary>
    /// Canonical identifier of a team: 32 lowercase hexadecimal digits (AD-23, AD-31).
    /// </summary>
    private const string ClientId = "0193f2a1b2c34d4e5f60718293a4b5c6";

    /// <summary>
    /// Number of bytes of the package que se descargan en la primera ejecución, antes de
    /// la interrupción (AD-11).
    /// </summary>
    private const int PackageSize = 512_000;

    /// <summary>
    /// Bytes que el manejador deja pasar en cada trozo, para que la descarga siga EN
    /// CURSO cuando la prueba la interrumpe (AD-16).
    /// </summary>
    private const int ChunkSize = 1_024;

    /// <summary>
    /// Pausa entre dos trozos, para que la descarga avance despacio y la prueba la
    /// encuentre a medias (AD-16).
    /// </summary>
    private const int ChunkDelayMilliseconds = 20;

    private readonly List<string> _temporaryFolders = [];

    [Fact]
    public async Task A_download_that_was_interrupted_resumes_from_the_persisted_progress_after_a_restart()
    {
        var dataFolder = CreateTemporaryFolder();

        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        const string packageId = "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e70";

        var content = WritePackageBytes(host.DataDirectory, packageId, PackageSize);

        using var serverClient = host.CreateClient();

        await RegisterGameAndPublishAsync(serverClient, host.DataDirectory, packageId, content.Length);

        // La primera ejecución del cliente: descarga una parte y se INTERRUMPE.
        var first = await RunClientAsync(dataFolder, host, packageId, interrupted: true);

        Assert.NotNull(first.Handle);

        // El offset persistido es el que quedó en el archivo SQLite de verdad (AD-11).
        Assert.True(first.PersistedOffset > 0);

        // AC2: el offset persistido NUNCA es mayor que lo que el parcial contiene: el
        // progreso efectivo es la INTERSECCIÓN de lo persistido y lo que el archivo
        // parcial tiene en el disco (AD-11, AD-24).
        Assert.True(
            first.PersistedOffset <= first.PartialLength,
            "El offset persistido no puede ser mayor que los bytes del parcial.");

        // El parcial vive SOLO en la carpeta de las descargas (AD-9).
        Assert.Equal(
            Path.Combine(dataFolder, ClientDataLayout.DownloadsFolderName),
            Path.GetDirectoryName(first.PartialPath)!);

        // La segunda ejecución del cliente: es el REINICIO, sobre la MISMA carpeta.
        var second = await RunClientAsync(dataFolder, host, packageId, interrupted: false);

        Assert.NotNull(second.Handle);

        // AC2, AD-20: el servidor recibe el Range que empieza en el offset persistido, y
        // NUNCA desde cero.
        var range = Assert.Single(second.RequestedRanges);

        Assert.Equal($"bytes={first.PersistedOffset}-", range);
        Assert.DoesNotContain("bytes=0-", second.RequestedRanges);

        // La operación recuperada conserva su identificador: la intención NO se
        // duplica (AC1).
        Assert.Equal(first.Handle!.OperationId, second.Handle!.OperationId);

        // AC2, UX-DR-45: la operación se presenta como «Reanudando desde 42 % · 3,2 GB de
        // 7,6 GB», con el offset del que continúa.
        Assert.NotNull(second.ResumeText);
        Assert.StartsWith("Reanudando desde ", second.ResumeText, StringComparison.Ordinal);

        // El texto del progreso sigue siendo el de la fuente ÚNICA: el porcentaje y el
        // par de tamaños, nunca los bytes en crudo (UX-DR-5, UX-DR-13).
        Assert.NotNull(second.OffsetText);
        Assert.Matches(@"^\d+ % · .+ GB de .+ GB$", second.OffsetText);

        // El porcentaje que se presenta es el del offset persistido sobre el total.
        var expectedPercentage = (int)(first.PersistedOffset * 100 / PackageSize);

        Assert.Contains(
            $"{expectedPercentage} %",
            second.OffsetText,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_partial_of_an_interrupted_download_lives_only_in_the_downloads_folder()
    {
        var dataFolder = CreateTemporaryFolder();

        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        const string packageId = "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e71";

        var content = WritePackageBytes(host.DataDirectory, packageId, PackageSize);

        using var serverClient = host.CreateClient();

        await RegisterGameAndPublishAsync(serverClient, host.DataDirectory, packageId, content.Length);

        var first = await RunClientAsync(dataFolder, host, packageId, interrupted: true);

        // AD-9: el parcial vive en `downloads/` y en NINGÚN otro sitio de la carpeta de
        // datos.
        var partials = Directory
            .GetFiles(dataFolder, "*" + ClientDataLayout.PartialFileExtension, SearchOption.AllDirectories)
            .ToArray();

        Assert.Single(partials);
        Assert.Equal(first.PartialPath, partials[0]);
    }

    [Fact]
    public async Task Every_interrupted_download_resumes_from_the_persisted_progress()
    {
        // SM-2: el 100 % de las descargas de prueba interrumpidas se reanudan desde el
        // progreso persistido, sin reiniciar el paquete desde cero.
        var dataFolder = CreateTemporaryFolder();

        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        const string packageId = "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e72";

        var content = WritePackageBytes(host.DataDirectory, packageId, PackageSize);

        using var serverClient = host.CreateClient();

        await RegisterGameAndPublishAsync(serverClient, host.DataDirectory, packageId, content.Length);

        var first = await RunClientAsync(dataFolder, host, packageId, interrupted: true);

        Assert.True(first.PersistedOffset > 0);

        var second = await RunClientAsync(dataFolder, host, packageId, interrupted: false);

        // Cada reanudación pide el rango del offset persistido: ninguna vuelve a cero.
        Assert.All(
            second.RequestedRanges,
            range => Assert.Equal($"bytes={first.PersistedOffset}-", range));
    }

    [Fact]
    public async Task An_operation_that_the_player_cancelled_is_not_recovered_after_a_restart()
    {
        // AD-10, AC5: una descarga CANCELADA no se reanuda, y su registro queda en la
        // base de datos para que otra versión lo lea.
        var dataFolder = CreateTemporaryFolder();

        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        const string packageId = "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e73";

        var content = WritePackageBytes(host.DataDirectory, packageId, PackageSize);

        using var serverClient = host.CreateClient();

        await RegisterGameAndPublishAsync(serverClient, host.DataDirectory, packageId, content.Length);

        // La primera ejecución: la descarga se CANCELA, así que su estado es terminal.
        var cancelled = await RunCancelledDownloadAsync(dataFolder, host, packageId);

        Assert.NotNull(cancelled.Handle);
        Assert.Equal(LANPartyHub.Shared.Contracts.DownloadState.Cancelled, cancelled.Handle!.StateMachine.Current);

        // La segunda ejecución del cliente: es el REINICIO, y la operación terminal NO se
        // rehidrata, así que no se pide ningún rango al servidor.
        var second = await RunClientAsync(dataFolder, host, packageId, interrupted: false);

        Assert.Empty(second.RequestedRanges);
    }

    /// <summary>
    /// Runs one session of the client that CANCELS la descarga, para que su estado sea
    /// terminal (AD-10, AC5).
    /// </summary>
    /// <param name="dataFolder">Folder of the data of the client.</param>
    /// <param name="host">The real host of the event server.</param>
    /// <param name="packageId">Identifier of the package that the client downloads.</param>
    /// <returns>What the session observed.</returns>
    private async Task<ClientSession> RunCancelledDownloadAsync(
        string dataFolder,
        TestServer host,
        string packageId)
    {
        var logFolder = CreateTemporaryFolder();

        using var loggerFactory = ClientProgram.CreateLoggerFactory(logFolder);
        using var correlation = new ClientCorrelationFactory(loggerFactory, "equipo-de-prueba");

        var startup = await ClientProgram.StartClientAsync(
            [],
            correlation,
            loggerFactory,
            dataFolder: dataFolder,
            baseDirectory: logFolder,
            cancellationToken: TestContext.Current.CancellationToken);

        var layout = ClientDataLayout.Create(startup.DataFolder, startup.InstallDirectory);

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

        await using var context = new ClientStateDbContext(options);

        var stateStore = new ClientStateStore(context);

        var ranges = new List<string>();

        var headers = new StubRequestHeaders(ClientId);

        using var httpClient = new HttpClient(
            new ClientRequestHeadersHandler(headers, new RealClientVersionProvider())
            {
                InnerHandler = new RecordingRangeHandler(
                    new HttpClientHandler(),
                    ranges),
            })
        {
            BaseAddress = new Uri($"http://{host.BoundAddress}:{host.Port}"),
        };

        var engine = new HttpRangeTransferEngine(httpClient);

        using var composition = new DownloadComposition(
            new Uri($"http://{host.BoundAddress}:{host.Port}"),
            headers,
            stateStore,
            layout,
            engine,
            loggerFactory);

        DownloadOperationHandle? handle = null;

        composition.Coordinator.Changed += published => handle ??= published;

        var started = await composition.Coordinator.StartAsync(
            packageId,
            composition.ContentUriResolver(packageId),
            TestContext.Current.CancellationToken);

        await WaitUntilAsync(() => started.BytesPersisted > 0);

        await composition.Coordinator.CancelAsync(started, TestContext.Current.CancellationToken);

        return new ClientSession(
            started,
            await ReadPersistedOffset(stateStore),
            ReadPartialLength(layout, packageId),
            layout.PartialFilePath(packageId),
            ranges.ToArray(),
            null,
            null);
    }

    /// <summary>
    /// Runs one session of the client over the given data folder, which is what a restart
    /// of the team is: a NEW composition over the SAME folder (AD-11, AD-24).
    /// </summary>
    /// <param name="dataFolder">Folder of the data of the client.</param>
    /// <param name="host">The real host of the event server.</param>
    /// <param name="packageId">Identifier of the package that the client downloads.</param>
    /// <param name="interrupted">Whether the session leaves the download interrupted.</param>
    /// <returns>What the session observed.</returns>
    private async Task<ClientSession> RunClientAsync(
        string dataFolder,
        TestServer host,
        string packageId,
        bool interrupted)
    {
        var logFolder = CreateTemporaryFolder();

        using var loggerFactory = ClientProgram.CreateLoggerFactory(logFolder);
        using var correlation = new ClientCorrelationFactory(loggerFactory, "equipo-de-prueba");

        // El arranque REAL del cliente: aplica las migraciones, emite la identidad y
        // reconcilia el progreso persistido contra el parcial del disco (AD-11).
        var startup = await ClientProgram.StartClientAsync(
            [],
            correlation,
            loggerFactory,
            dataFolder: dataFolder,
            baseDirectory: logFolder,
            cancellationToken: TestContext.Current.CancellationToken);

        var layout = ClientDataLayout.Create(startup.DataFolder, startup.InstallDirectory);

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

        await using var context = new ClientStateDbContext(options);

        var stateStore = new ClientStateStore(context);

        // AD-6, AD-15, AD-23: la ruta del contenido es de la superficie del cliente, así
        // que la petición declara el identificador del equipo y la versión del
        // protocolo. Sin esas cabeceras el servidor la rechaza y no llega a servir un
        // solo byte.
        var headers = new StubRequestHeaders(ClientId);

        // El motor REAL de la transferencia, con un manejador que RECUERDA el `Range`
        // que pidió, para que la observación sea del servidor y no del cliente (AD-20).
        var ranges = new List<string>();

        using var httpClient = new HttpClient(
            new ClientRequestHeadersHandler(headers, new RealClientVersionProvider())
            {
                InnerHandler = new RecordingRangeHandler(
                    new ThrottledContentHandler
                    {
                        InnerHandler = new HttpClientHandler(),
                    },
                    ranges),
            })
        {
            BaseAddress = new Uri($"http://{host.BoundAddress}:{host.Port}"),
        };

        var engine = new HttpRangeTransferEngine(httpClient);

        using var composition = new DownloadComposition(
            new Uri($"http://{host.BoundAddress}:{host.Port}"),
            headers,
            stateStore,
            layout,
            engine,
            loggerFactory);

        var viewModel = new LANPartyHub.Client.Presentation.Downloads.DownloadProgressViewModel();

        DownloadOperationHandle? handle = null;

        composition.Coordinator.Changed += published => handle ??= published;

        if (!interrupted)
        {
            // El REINICIO: las operaciones persistidas se rehidratan y se reanudan.
            var recovered = await composition.Coordinator.RecoverAsync(
                composition.ContentUriResolver,
                TestContext.Current.CancellationToken);

            foreach (var published in recovered)
            {
                viewModel.Apply(published);
            }

            viewModel.ApplyResuming(recovered.Count > 0);

            await WaitUntilAsync(() => ranges.Count > 0 || recovered.Count == 0);

            return new ClientSession(
                handle,
                await ReadPersistedOffset(stateStore),
                ReadPartialLength(layout, packageId),
                layout.PartialFilePath(packageId),
                ranges.ToArray(),
                viewModel.ProgressTextWithResume,
                viewModel.ProgressText);
        }

        // La PRIMERA ejecución: la descarga empieza y se INTERRUMPE a mitad.
        var started = await composition.Coordinator.StartAsync(
            packageId,
            composition.ContentUriResolver(packageId),
            TestContext.Current.CancellationToken);

        var partialPath = layout.PartialFilePath(packageId);

        // La espera es por un parcial INTERMEDIO: la descarga tiene que seguir en curso
        // cuando la prueba la interrumpe, así que lo que se corta es una descarga a
        // medias, no una ya terminada (AD-11, SM-2).
        //
        // El progreso se observa en el ARCHIVO PARCIAL y no en el handle: el coordinador
        // publica el offset cuando el tramo TERMINA, así que el archivo es lo que
        // muestra que los bytes están en el disco (AD-9, AD-11).
        //
        // La espera es por más bytes que el intervalo de persistencia, así que el offset
        // persistido es REAL y no un cero (AD-11, SM-2).
        await WaitUntilAsync(
            () => ReadPartialLength(layout, packageId) > ProgressCountingStream.PersistenceInterval * 2);

        await composition.Coordinator.PauseAsync(started);

        // La descarga interrupted: el parcial tiene menos bytes que el paquete, así que
        // hay algo que reanudar (AD-11, SM-2).
        Assert.True(
            ReadPartialLength(layout, packageId) < PackageSize,
            "La descarga se terminó antes de que la prueba la interrumpiera.");

        Assert.True(
            await ReadPersistedOffset(stateStore) > 0,
            $"El offset persistido es cero: el estado de la operación es {started.StateMachine.Current} "
            + $"y el parcial tiene {ReadPartialLength(layout, packageId)} bytes.");

        Assert.True(
            await ReadPersistedTotal(stateStore) == PackageSize,
            $"El total persistido es {await ReadPersistedTotal(stateStore)} y el tamaño del paquete es {PackageSize}.");

        return new ClientSession(
            started,
            await ReadPersistedOffset(stateStore),
            ReadPartialLength(layout, packageId),
            partialPath,
            ranges.ToArray(),
            null,
            null);
    }

    /// <summary>
    /// Reads the offset that quedó persistido en la base de datos del cliente (AD-11).
    /// </summary>
    /// <param name="stateStore">Access to the database of the client.</param>
    /// <returns>The persisted offset, or zero when there is none.</returns>
    private static async Task<long> ReadPersistedOffset(IClientStateStore stateStore)
    {
        var operations = await stateStore.LoadDownloadOperationsAsync(TestContext.Current.CancellationToken);

        return operations
            .Select(operation => operation.BytesPersisted ?? 0L)
            .DefaultIfEmpty(0L)
            .Max();
    }

    /// <summary>
    /// Reads the total que quedó persistido en la base de datos del cliente (AD-11).
    /// </summary>
    /// <param name="stateStore">Access to the database of the client.</param>
    /// <returns>The persisted total, or zero when there is none.</returns>
    private static async Task<long> ReadPersistedTotal(IClientStateStore stateStore)
    {
        var operations = await stateStore.LoadDownloadOperationsAsync(TestContext.Current.CancellationToken);

        return operations
            .Select(operation => operation.TotalBytes ?? 0L)
            .DefaultIfEmpty(0L)
            .Max();
    }

    /// <summary>
    /// Reads the length of the partial file of a package (AD-9).
    /// </summary>
    /// <param name="layout">Layout of the data folder of the client.</param>
    /// <param name="packageId">Identifier of the package.</param>
    /// <returns>The length of the partial file, or zero when it does not exist.</returns>
    private static long ReadPartialLength(ClientDataLayout layout, string packageId)
    {
        var path = layout.PartialFilePath(packageId);

        return File.Exists(path) ? new FileInfo(path).Length : 0L;
    }

    /// <summary>
    /// Waits until the condition holds, so the test no tiene que dormir un tiempo fijo
    /// (AD-16).
    /// </summary>
    /// <param name="condition">Condition that has to become true.</param>
    /// <returns>A task that completes when the condition holds.</returns>
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);

        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("La condición no se cumplió dentro del plazo.");
            }

            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    /// Registers a videogame and publishes a package with real bytes, so the server can
    /// serve its content by ranges (AD-7, AD-20).
    /// </summary>
    /// <param name="client">Client of the host.</param>
    /// <param name="dataDirectory">Folder of the data of the host.</param>
    /// <param name="packageId">Identifier of the package.</param>
    /// <param name="sizeBytes">Exact size of the package.</param>
    private static async Task RegisterGameAndPublishAsync(
        HttpClient client,
        string dataDirectory,
        string packageId,
        long sizeBytes)
    {
        var gameRequest = new GameRequest
        {
            SchemaVersion = GamesEndpoint.SupportedSchemaVersion,
            Game = new GameDescriptor(
                string.Empty,
                "Counter-Strike 2",
                "Disparadores en primera persona.",
                ["Shooter"],
                ["fps"],
                "cover.png",
                ["captura-1.png"],
                "1.41.3",
                sizeBytes,
                1,
                10,
                "Windows",
                "Servidor",
                "Portable",
                new InstallationManifest.LaunchSettings("bin\\cs2.exe", ["-novid"], "game\\bin"),
                ["DirectX 12"],
                "Propietaria",
                "Solo para el evento"),
        };

        using var gameResponse = await client.PostAsync(
            GamesEndpoint.Route,
            BuildBody(gameRequest),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, gameResponse.StatusCode);

        var gameDescriptor = await ReadDescriptorAsync(gameResponse, TestContext.Current.CancellationToken);

        var packageRequest = new PackageRequest
        {
            SchemaVersion = PackagesEndpoint.SupportedSchemaVersion,
            GameId = gameDescriptor.GameId,
            Package = new PackageDescriptor(
                packageId,
                gameDescriptor.GameId,
                "1.0.0",
                sizeBytes,
                0,
                string.Empty,
                string.Empty),
            Manifest = new InstallationManifest(
                InstallationManifestSchema.Version,
                packageId,
                "1.0.0",
                sizeBytes,
                string.Empty,
                new InstallationManifest.LaunchSettings("game\\game.exe", [], "game"),
                [new InstallationManifest.CreateDirectory("game")]),
        };

        using var packageResponse = await client.PostAsync(
            PackagesEndpoint.Route,
            BuildBody(packageRequest),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, packageResponse.StatusCode);

        Assert.NotNull(dataDirectory);
    }

    /// <summary>
    /// Writes the bytes of a package in the repository of packages of the server, so the
    /// route del contenido puede servirlos por rangos (AD-9, AD-20).
    /// </summary>
    /// <param name="dataDirectory">Folder of the data of the host.</param>
    /// <param name="packageId">Identifier of the package.</param>
    /// <param name="length">Number of bytes of the package.</param>
    /// <returns>The bytes of the package.</returns>
    private static byte[] WritePackageBytes(string dataDirectory, string packageId, int length)
    {
        var packages = Path.Combine(dataDirectory, DataDirectory.PackagesFolderName);

        Directory.CreateDirectory(packages);

        var bytes = new byte[length];

        Random.Shared.NextBytes(bytes);

        File.WriteAllBytes(Path.Combine(packages, packageId), bytes);

        return bytes;
    }

    /// <summary>
    /// Builds the body of a request with the JSON que el servidor espera (AD-5).
    /// </summary>
    /// <typeparam name="T">Type of the request.</typeparam>
    /// <param name="request">Request to build.</param>
    /// <returns>The content of the request.</returns>
    private static StringContent BuildBody<T>(T request) =>
        new(
            JsonSerializer.Serialize(request, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            System.Text.Encoding.UTF8,
            "application/json");

    /// <summary>
    /// Reads the contract del videojuego de una respuesta (AD-7).
    /// </summary>
    /// <param name="response">Response of the host.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The contract of the videogame.</returns>
    private static async Task<GameDescriptor> ReadDescriptorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        var descriptor = JsonSerializer.Deserialize<GameDescriptor>(
            payload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(descriptor);

        return descriptor!;
    }

    private string CreateTemporaryFolder()
    {
        var folder = Directory.CreateTempSubdirectory("lan-party-hub-restart-");

        _temporaryFolders.Add(folder.FullName);

        return folder.FullName;
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
    /// What one session of the client observed (AD-16).
    /// </summary>
    /// <param name="Handle">Handle of the operation, or <c>null</c> when there was none.</param>
    /// <param name="PersistedOffset">Offset that quedó persistido.</param>
    /// <param name="PartialLength">Length of the partial file.</param>
    /// <param name="PartialPath">Path of the partial file.</param>
    /// <param name="RequestedRanges">Ranges that el motor pidió al servidor.</param>
    /// <param name="ResumeText">Text of the progress with the microcopy of la reanudación.</param>
    /// <param name="OffsetText">Text of the progress.</param>
    private sealed record ClientSession(
        DownloadOperationHandle? Handle,
        long PersistedOffset,
        long PartialLength,
        string PartialPath,
        IReadOnlyList<string> RequestedRanges,
        string? ResumeText,
        string? OffsetText);

    /// <summary>
    /// Headers of the correlation that the composition stamps in every request (AD-15,
    /// AD-23).
    /// </summary>
    private sealed class StubRequestHeaders : IClientRequestHeaders
    {
        public StubRequestHeaders(string? clientId, string? operationId = null)
        {
            ClientId = clientId;
            OperationId = operationId;
        }

        public string? ClientId { get; }

        public string? OperationId { get; }

        public string? MachineName => "equipo-de-prueba";
    }

    /// <summary>
    /// Manejador que RECUERDA el `Range` que el motor pidió y lo deja pasar al servidor
    /// real, así que la observación es del servidor y no del cliente (AD-20).
    /// </summary>
    private sealed class RecordingRangeHandler : DelegatingHandler
    {
        private readonly List<string> _ranges;

        public RecordingRangeHandler(HttpMessageHandler innerHandler, List<string> ranges)
            : base(innerHandler) => _ranges = ranges;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Headers.Range is { } range)
            {
                _ranges.Add(range.ToString());
            }

            return base.SendAsync(request, cancellationToken);
        }
    }

    /// <summary>
    /// Manejador que deja pasar el contenido POR TROZOS, para que la descarga siga en
    /// curso cuando la prueba la interrumpe: sin la lentitud, el paquete entero cabría
    /// en la prueba y no habría nada que reanudar (AD-16).
    /// </summary>
    private sealed class ThrottledContentHandler : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);

            if (response.Content is HttpContent content)
            {
                // AD-20: las CABECERAS del contenido se copian, porque el total del
                // paquete viaja en el `Content-Range` y el motor lo lee de ahí. Un
                // contenido que las pierde haría que la descarga no supiera el tamaño
                // del paquete.
                var throttled = new ThrottledContent(content);

                foreach (var header in content.Headers)
                {
                    throttled.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }

                response.Content = throttled;
            }

            return response;
        }
    }

    /// <summary>
    /// Contenido que se entrega por trozos, para que la descarga avance despacio
    /// (AD-16).
    /// </summary>
    private sealed class ThrottledContent : HttpContent
    {
        private readonly HttpContent _inner;

        public ThrottledContent(HttpContent inner) => _inner = inner;

        protected override async Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context)
        {
            await CopyAsync(await _inner.ReadAsStreamAsync().ConfigureAwait(false), stream)
                .ConfigureAwait(false);
        }

        protected override async Task<Stream> CreateContentReadStreamAsync()
        {
            var source = await _inner.ReadAsStreamAsync().ConfigureAwait(false);

            return new ThrottledStream(source);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;

            return false;
        }

        /// <summary>
        /// Copies the bytes of the content in chunks, with a pause between them (AD-16).
        /// </summary>
        /// <param name="source">Stream that holds the bytes.</param>
        /// <param name="destination">Stream that receives the bytes.</param>
        /// <returns>A task that completes when the bytes have been copied.</returns>
        private static async Task CopyAsync(Stream source, Stream destination)
        {
            var buffer = new byte[ChunkSize];

            int read;

            while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length)).ConfigureAwait(false)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);

                await destination.FlushAsync().ConfigureAwait(false);

                await Task.Delay(ChunkDelayMilliseconds).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Stream that entrega sus bytes por trozos, para que la descarga avance despacio
    /// (AD-16).
    /// </summary>
    private sealed class ThrottledStream : Stream
    {
        private readonly Stream _inner;

        public ThrottledStream(Stream inner) => _inner = inner;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => _inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) =>
            _inner.Read(buffer, offset, count);

        public override async Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            var read = await _inner
                .ReadAsync(buffer.AsMemory(offset, count), cancellationToken)
                .ConfigureAwait(false);

            await Task.Delay(ChunkDelayMilliseconds, cancellationToken).ConfigureAwait(false);

            return read;
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            new(ReadAsync(buffer.ToArray(), 0, buffer.Length, cancellationToken));

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
