using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LANPartyHub.Client.Application.Downloads;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Domain.Trust;
using LANPartyHub.Client.Host;
using LANPartyHub.Client.Infrastructure.Persistence;
using LANPartyHub.Server.Application.Packages;
using LANPartyHub.Server.Host;
using LANPartyHub.Server.Infrastructure.Persistence;
using LANPartyHub.Server.Infrastructure.Signing;
using LANPartyHub.Shared.Contracts;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LANPartyHub.IntegrationTests;

/// <summary>
/// The validation of the integrity of a downloaded package against the real server and the
/// real database of the client (AD-2, AD-8, AD-16, AC1..AC6).
///
/// <para>
/// El test arranca el host real, con el Kestrel real y un <c>catalog.sqlite</c> real, y
/// monta la composición real de las descargas del cliente sobre una base de datos real del
/// cliente con el ancla de confianza YA FIJADA. Así se observa el camino entero: el
/// manifiesto firmado que sirve el servidor, la validación contra el ancla, la descarga y
/// la comprobación del hash contra el manifiesto firmado (AD-2, AD-8).
/// </para>
/// <para>
/// Un paquete ALTERADO termina en <c>Failed</c>, con el parcial borrado y el error de la
/// corrupción en el log (AD-8, AC2).
/// </para>
/// </summary>
[Collection(RealHostCollection.Name)]
public sealed class DownloadIntegrityTests : IDisposable
{
    private const string ClientId = "0193f2a1b2c34d4e5f60718293a4b5c6";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly List<string> _temporaryFolders = [];

    /// <summary>
    /// A package whose bytes match the signed manifest completes the operation (AD-8,
    /// AC1).
    /// </summary>
    [Fact]
    public async Task A_package_whose_bytes_match_the_signed_manifest_completes_the_operation()
    {
        var dataDirectory = CreateTemporaryDataDirectory();
        var clientFolder = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        const string packageId = "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e01";
        var bytes = WritePackageBytes(dataDirectory, packageId, "el contenido íntegro del paquete");

        await PublishAsync(host, dataDirectory, packageId, bytes.LongLength);

        await FixAnchorAsync(clientFolder);

        using var harness = CreateComposition(host, clientFolder);

        var handle = await harness.Coordinator.StartAsync(
            packageId,
            BuildContentUri(host, packageId),
            TestContext.Current.CancellationToken);

        await WaitUntilAsync(() => handle.StateMachine.Current == DownloadState.Completed);

        Assert.Equal(DownloadState.Completed, handle.StateMachine.Current);

        // El manifiesto firmado está en la caché, porque es de fiar (AD-2, AD-20).
        Assert.True(File.Exists(Path.Combine(
            clientFolder,
            ClientDataLayout.ManifestsFolderName,
            packageId + ".json")));

        // El parcial conserva los bytes: un paquete íntegro no se borra (AD-8).
        Assert.True(File.Exists(Path.Combine(
            clientFolder,
            ClientDataLayout.DownloadsFolderName,
            packageId + ClientDataLayout.PartialFileExtension)));
    }

    /// <summary>
    /// A package that was ALTERED during the transfer is REJECTED: the operation ends in
    /// <c>Failed</c>, the partial is discarded and the progress is reset (AD-8, AC2).
    /// </summary>
    [Fact]
    public async Task A_package_that_was_altered_is_rejected_and_its_partial_is_discarded()
    {
        var dataDirectory = CreateTemporaryDataDirectory();
        var clientFolder = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        const string packageId = "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e02";
        var bytes = WritePackageBytes(dataDirectory, packageId, "el contenido original del paquete");

        await PublishAsync(host, dataDirectory, packageId, bytes.LongLength);

        await FixAnchorAsync(clientFolder);

        // El servidor sirve el contenido, pero el cliente recibe bytes ALTERADOS: el
        // hash no coincide con el del manifiesto FIRMADO (AD-8).
        using var harness = CreateComposition(host, clientFolder, alterContent: true);

        var handle = await harness.Coordinator.StartAsync(
            packageId,
            BuildContentUri(host, packageId),
            TestContext.Current.CancellationToken);

        await WaitUntilAsync(() => handle.StateMachine.Current == DownloadState.Failed);

        Assert.Equal(DownloadState.Failed, handle.StateMachine.Current);

        // AC2: el archivo parcial se DESCARTA.
        Assert.False(File.Exists(Path.Combine(
            clientFolder,
            ClientDataLayout.DownloadsFolderName,
            packageId + ClientDataLayout.PartialFileExtension)));

        // AC2: la operación NO es reparable por reanudación, y el progreso se reinicia a
        // cero, así que la descarga siguiente empieza de CERO (AD-11, AD-24).
        Assert.True(handle.StateMachine.IsTerminal);
        Assert.Equal(0, handle.BytesPersisted);
    }

    /// <summary>
    /// A package whose signed manifest does NOT validate against the fixed anchor leaves
    /// NO trace on disk: no operation is registered, no partial is created and the
    /// manifest is not cached (AD-2, AD-8, AC4).
    /// </summary>
    [Fact]
    public async Task A_manifest_that_does_not_validate_leaves_no_write_on_disk()
    {
        var dataDirectory = CreateTemporaryDataDirectory();
        var clientFolder = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        const string packageId = "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e03";
        var bytes = WritePackageBytes(dataDirectory, packageId, "el contenido del paquete");

        await PublishAsync(host, dataDirectory, packageId, bytes.LongLength);

        // El cliente fija un ancla que NO es la del servidor: la firma no valida contra
        // ella, así que el manifiesto es de OTRO emisor (AD-27).
        await FixAnchorAsync(clientFolder, useForeignKey: true);

        using var harness = CreateComposition(host, clientFolder);

        await Assert.ThrowsAsync<PackageValidationFailedException>(() =>
            harness.Coordinator.StartAsync(
                packageId,
                BuildContentUri(host, packageId),
                TestContext.Current.CancellationToken));

        // NINGUNA escritura en disco: ni el manifiesto en la caché, ni el parcial (AD-2,
        // AD-8, AD-27).
        Assert.False(File.Exists(Path.Combine(
            clientFolder,
            ClientDataLayout.ManifestsFolderName,
            packageId + ".json")));

        Assert.False(File.Exists(Path.Combine(
            clientFolder,
            ClientDataLayout.DownloadsFolderName,
            packageId + ClientDataLayout.PartialFileExtension)));

        // Y la operación NO se registró: la base de datos del cliente no tiene ninguna
        // fila de operaciones (AD-8).
        Assert.Equal(0, ReadOperationCount(clientFolder));
    }

    /// <summary>
    /// A manifest that the server signs with ANOTHER key is not trustworthy, y el ancla
    /// fijada NO se invalida (AD-27, AC3).
    /// </summary>
    [Fact]
    public async Task A_manifest_signed_with_another_key_is_not_trustworthy()
    {
        var dataDirectory = CreateTemporaryDataDirectory();
        var clientFolder = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        const string packageId = "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e04";
        var bytes = WritePackageBytes(dataDirectory, packageId, "el contenido del paquete");

        await PublishAsync(host, dataDirectory, packageId, bytes.LongLength);

        // El ancla fijada es la de OTRO equipo: la firma del servidor NO valida contra
        // ella (AD-27).
        await FixAnchorAsync(clientFolder, useForeignKey: true);

        using var harness = CreateComposition(host, clientFolder);

        var failure = await Assert.ThrowsAsync<PackageValidationFailedException>(() =>
            harness.Coordinator.StartAsync(
                packageId,
                BuildContentUri(host, packageId),
                TestContext.Current.CancellationToken));

        Assert.True(failure.Result.IsUntrusted);
        Assert.Equal(ErrorCatalog.ManifestSignatureInvalidCode, failure.Result.Error!.Code);

        // El ancla fijada NO se invalida: la fila sigue ahí (AD-27).
        Assert.Equal(1, ReadAnchorCount(clientFolder));
    }

    /// <summary>
    /// The corruption and the lack of authenticity are DIFFERENT failures, with two
    /// different stable codes (AD-15, AD-25, AC6).
    /// </summary>
    [Fact]
    public async Task The_corruption_and_the_lack_of_authenticity_are_different_failures()
    {
        var dataDirectory = CreateTemporaryDataDirectory();
        var clientFolder = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        const string packageId = "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e05";
        var bytes = WritePackageBytes(dataDirectory, packageId, "el contenido alterado");

        await PublishAsync(host, dataDirectory, packageId, bytes.LongLength);

        await FixAnchorAsync(clientFolder, useForeignKey: true);

        using var harness = CreateComposition(host, clientFolder);

        var failure = await Assert.ThrowsAsync<PackageValidationFailedException>(() =>
            harness.Coordinator.StartAsync(
                packageId,
                BuildContentUri(host, packageId),
                TestContext.Current.CancellationToken));

        // La falta de autenticidad NO es corrupción: son dos códigos distintos, y el que
        // colapsa los dos pierde la información que distingue un ataque de un archivo
        // dañado (AD-27, AC6).
        Assert.Equal(ErrorCatalog.ManifestSignatureInvalidCode, failure.Result.Error!.Code);
        Assert.NotEqual(ErrorCatalog.PackageIntegrityMismatchCode, failure.Result.Error.Code);
    }

    /// <summary>
    /// The error of the corruption reaches the LOG of the client, así que el diagnóstico
    /// conserva el fallo y su causa (AD-15, AC6, FR-7).
    /// </summary>
    [Fact]
    public async Task The_error_of_the_corruption_reaches_the_log_of_the_client()
    {
        var dataDirectory = CreateTemporaryDataDirectory();
        var clientFolder = CreateTemporaryDataDirectory();
        var logFolder = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        const string packageId = "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e06";
        var bytes = WritePackageBytes(dataDirectory, packageId, "el contenido que se altera");

        await PublishAsync(host, dataDirectory, packageId, bytes.LongLength);

        await FixAnchorAsync(clientFolder);

        using var factory = LANPartyHub.Client.Program.CreateLoggerFactory(logFolder);
        using var correlation = new ClientCorrelationFactory(factory);

        correlation.Open(ClientId);

        using var harness = CreateComposition(host, clientFolder, factory: factory, alterContent: true);

        var handle = await harness.Coordinator.StartAsync(
            packageId,
            BuildContentUri(host, packageId),
            TestContext.Current.CancellationToken);

        await WaitUntilAsync(() => handle.StateMachine.Current == DownloadState.Failed);

        // El log estructurado es el sumidero durable del diagnóstico (AD-15, AC6).
        var log = await ReadLogAsync(logFolder);

        // El log es JSON y el serializador escapa los caracteres que no son ASCII, así que
        // la comprobación mira el TEXTO con los mismos escapes que el log escribió.
        Assert.Contains("da\\u00F1ado", log, StringComparison.Ordinal);

        // Y el diagnóstico lleva el hash esperado y el calculado, que es lo que
        // distingue la corrupción de la falta de autenticidad (AC6).
        Assert.Contains("ActualSha256", log, StringComparison.Ordinal);
        Assert.Contains("ExpectedSha256", log, StringComparison.Ordinal);
    }

    /// <summary>
    /// Builds the composition of the downloads over the real host, with the real layout of
    /// the client and the real store of su base de datos (AD-16).
    /// </summary>
    /// <param name="host">The real host of the event.</param>
    /// <param name="clientFolder">Folder of the data of the client.</param>
    /// <param name="factory">Factory of the log of the client.</param>
    /// <param name="alterContent">
    /// When it is <c>true</c>, the engine serves bytes ALTERADOS, para que el hash no
    /// coincida con el del manifiesto firmado (AD-8).
    /// </param>
    /// <returns>The composition of the downloads.</returns>
    private static ClientHarness CreateComposition(
        TestServer host,
        string clientFolder,
        Microsoft.Extensions.Logging.ILoggerFactory? factory = null,
        bool alterContent = false)
    {
        var layout = ClientDataLayout.Create(clientFolder, Path.Combine(clientFolder, "install"));

        layout.EnsureCreated();

        var options = new DbContextOptionsBuilder<ClientStateDbContext>()
            .UseSqlite(
                $"Data Source={layout.StateDatabasePath};Pooling=False",
                sqlite => sqlite.MigrationsAssembly(typeof(ClientStateDbContext).Assembly.GetName().Name))
            .Options;

        var context = new ClientStateDbContext(options);

        var stateStore = new ClientStateStore(context);

        var correlation = new ClientCorrelationFactory(
            factory ?? NullLoggerFactory.Instance,
            Environment.MachineName);

        correlation.Open(ClientId);

        var transfer = new TransferComposition(correlation);

        var composition = new DownloadComposition(
            new Uri($"http://{host.BoundAddress}:{host.Port}"),
            correlation,
            stateStore,
            layout,
            alterContent ? new AlteringTransferEngine(transfer.Engine) : transfer.Engine,
            factory ?? NullLoggerFactory.Instance,
            stateStore);

        return new ClientHarness(composition, transfer, correlation, context);
    }

    /// <summary>
    /// The absolute URL of the content of a package (AD-20).
    /// </summary>
    /// <param name="host">The real host of the event.</param>
    /// <param name="packageId">Identifier of the package.</param>
    /// <returns>The URL of the content.</returns>
    private static Uri BuildContentUri(TestServer host, string packageId) =>
        new($"http://{host.BoundAddress}:{host.Port}/api/v1/packages/{packageId}/content");

    /// <summary>
    /// Fixes the ancla de confianza of the client in its real database, con la clave del
    /// servidor del evento o con la de OTRO equipo (AD-27).
    /// </summary>
    /// <param name="clientFolder">Folder of the data of the client.</param>
    /// <param name="useForeignKey">
    /// When it is <c>true</c>, the ancla fijada es la de otro equipo, así que la firma del
    /// servidor NO valida contra ella (AD-27).
    /// </param>
    /// <returns>A task that completes when the ancla is fixed.</returns>
    private static async Task FixAnchorAsync(string clientFolder, bool useForeignKey = false)
    {
        var layout = ClientDataLayout.Create(clientFolder, Path.Combine(clientFolder, "install"));

        layout.EnsureCreated();

        var options = new DbContextOptionsBuilder<ClientStateDbContext>()
            .UseSqlite(
                $"Data Source={layout.StateDatabasePath};Pooling=False",
                sqlite => sqlite.MigrationsAssembly(typeof(ClientStateDbContext).Assembly.GetName().Name))
            .Options;

        await using var context = new ClientStateDbContext(options);

        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);

        var machineName = useForeignKey ? "otro-equipo-del-evento" : Environment.MachineName;
        var publicKey = Ed25519ManifestSigner.ComputePublicKey(machineName);

        var store = new ClientStateStore(context);

        await store.SaveTrustAnchorAsync(
            new PinnedTrustAnchor(
                Convert.ToBase64String(publicKey),
                Ed25519ManifestSigner.ComputeFingerprint(publicKey),
                "2026-10-26T00:00:00.0000000Z"),
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Publishes a package in the catalog of the event, so the server signs a real
    /// manifest of real bytes (AD-2).
    /// </summary>
    /// <param name="host">The real host of the event.</param>
    /// <param name="dataDirectory">Folder of the data of the host.</param>
    /// <param name="packageId">Identifier of the package.</param>
    /// <param name="sizeBytes">Exact size of the package.</param>
    /// <returns>A task that completes when the package is published.</returns>
    private static async Task PublishAsync(
        TestServer host,
        string dataDirectory,
        string packageId,
        long sizeBytes)
    {
        using var client = host.CreateClient();

        var gameId = await RegisterGameAsync(client);

        var request = new PackageRequest
        {
            SchemaVersion = PackagesEndpoint.SupportedSchemaVersion,
            GameId = gameId,
            Package = new PackageDescriptor(
                packageId,
                gameId,
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
                new InstallationManifest.LaunchSettings("game\\game.exe", ["-novid"], "game"),
                [new InstallationManifest.Extract("game.zip", "game")]),
        };

        using var response = await client.PostAsync(
            PackagesEndpoint.Route,
            new StringContent(
                JsonSerializer.Serialize(request, SerializerOptions),
                Encoding.UTF8,
                "application/json"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    /// <summary>
    /// Registers a videogame in the catalog of the event.
    /// </summary>
    /// <param name="client">Client of the host.</param>
    /// <returns>The identifier of the videogame.</returns>
    private static async Task<string> RegisterGameAsync(HttpClient client)
    {
        var request = new GameRequest
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
                7_600_000_000,
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

        using var response = await client.PostAsync(
            GamesEndpoint.Route,
            new StringContent(
                JsonSerializer.Serialize(request, SerializerOptions),
                Encoding.UTF8,
                "application/json"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var payload = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        using var document = JsonDocument.Parse(payload);

        return document.RootElement.GetProperty("gameId").GetString() ?? string.Empty;
    }

    /// <summary>
    /// Writes the bytes of a package in the repository of packages of the server (AD-2).
    /// </summary>
    /// <param name="dataDirectory">Folder of the data of the host.</param>
    /// <param name="packageId">Identifier of the package.</param>
    /// <param name="content">Content of the package.</param>
    /// <returns>The bytes of the package.</returns>
    private static byte[] WritePackageBytes(string dataDirectory, string packageId, string content)
    {
        var packages = Path.Combine(dataDirectory, DataDirectory.PackagesFolderName);

        Directory.CreateDirectory(packages);

        var bytes = Encoding.UTF8.GetBytes(content);

        File.WriteAllBytes(Path.Combine(packages, packageId), bytes);

        return bytes;
    }

    /// <summary>
    /// Reads the number of the rows of the table of the download operations of the client
    /// (AD-11).
    /// </summary>
    /// <param name="clientFolder">Folder of the data of the client.</param>
    /// <returns>The number of the registered operations.</returns>
    private static int ReadOperationCount(string clientFolder) =>
        ReadCount(clientFolder, "SELECT COUNT(*) FROM DOWNLOAD_OPERATION");

    /// <summary>
    /// Reads the number of the rows of the table of the ancla de confianza del cliente
    /// (AD-27).
    /// </summary>
    /// <param name="clientFolder">Folder of the data of the client.</param>
    /// <returns>The number of the fixed anchors.</returns>
    private static int ReadAnchorCount(string clientFolder) =>
        ReadCount(clientFolder, "SELECT COUNT(*) FROM TRUST_ANCHOR");

    /// <summary>
    /// Reads the value of the primera columna de un query sobre la base de datos del
    /// cliente.
    /// </summary>
    /// <param name="clientFolder">Folder of the data of the client.</param>
    /// <param name="command">Query to run.</param>
    /// <returns>The value of the first column.</returns>
    private static int ReadCount(string clientFolder, string command)
    {
        using var connection = new SqliteConnection(
            $"Data Source={Path.Combine(clientFolder, ClientDataLayout.StateDatabaseFileName)};Pooling=False");

        connection.Open();

        using var reader = connection.CreateCommand();
        reader.CommandText = command;

        return Convert.ToInt32(reader.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Reads the log file that the client wrote (AD-15).
    /// </summary>
    /// <param name="logFolder">Folder of the deployment of the client.</param>
    /// <returns>The content of the log.</returns>
    private static async Task<string> ReadLogAsync(string logFolder)
    {
        var path = Path.Combine(
            logFolder,
            LANPartyHub.Client.Infrastructure.Logging.JsonFileLoggerProvider.FileName);

        var deadline = DateTime.UtcNow.AddSeconds(10);

        while (!File.Exists(path))
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("El log del cliente no apareció dentro del plazo.");
            }

            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        return await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Waits until the condition holds, so the test no tiene que dormir un tiempo fijo
    /// (AD-16).
    /// </summary>
    /// <param name="condition">Condition that has to become true.</param>
    /// <returns>A task that completes when the condition holds.</returns>
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);

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
    /// Creates a temporary folder for the test.
    /// </summary>
    /// <returns>The absolute path of the folder.</returns>
    private string CreateTemporaryDataDirectory()
    {
        var folder = Directory.CreateTempSubdirectory("lan-party-hub-download-integrity-");

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
            catch (UnauthorizedAccessException)
            {
                // The folder cannot be deleted now, and it does not fail the test.
            }
        }
    }

/// <summary>
    /// Holder of the composition of the client and of the pieces that it opens, para que
    /// la prueba los cierre todos cuando termina (AD-16).
    /// </summary>
    private sealed class ClientHarness : IDisposable
    {
        private readonly TransferComposition _transfer;
        private readonly ClientCorrelationFactory _correlation;
        private readonly ClientStateDbContext _context;

        public ClientHarness(
            DownloadComposition composition,
            TransferComposition transfer,
            ClientCorrelationFactory correlation,
            ClientStateDbContext context)
        {
            Composition = composition;
            _transfer = transfer;
            _correlation = correlation;
            _context = context;
        }

        /// <summary>The composition of the downloads of the client.</summary>
        public DownloadComposition Composition { get; }

        /// <summary>The coordinator of the download operations of the client.</summary>
        public DownloadCoordinator Coordinator => Composition.Coordinator;

        /// <inheritdoc />
        public void Dispose()
        {
            Composition.Dispose();
            _transfer.Dispose();
            _correlation.Dispose();
            _context.Dispose();
        }
    }

    /// <summary>
    /// Engine que ALTERA los bytes que escribe, para que el hash del paquete no
    /// coincida con el del manifiesto firmado (AD-8).
    /// </summary>
    private sealed class AlteringTransferEngine : ITransferEngine
    {
        private readonly ITransferEngine _inner;

        public AlteringTransferEngine(ITransferEngine inner)
        {
            _inner = inner;
        }

        public async Task<TransferOutcome> TransferAsync(
            Uri contentUri,
            long startInclusive,
            long endInclusive,
            Stream destination,
            CancellationToken cancellationToken)
        {
            var buffer = new MemoryStream();

            var outcome = await _inner
                .TransferAsync(contentUri, startInclusive, endInclusive, buffer, cancellationToken)
                .ConfigureAwait(false);

            var bytes = buffer.ToArray();

            // Un byte del contenido cambia, así que el sha256 del paquete es OTRO que el
            // del manifiesto firmado (AD-8).
            if (bytes.Length > 0)
            {
                bytes[0] = (byte)(bytes[0] ^ 0xFF);
            }

            await destination.WriteAsync(bytes.AsMemory(), cancellationToken).ConfigureAwait(false);

            return outcome;
        }
    }
}
