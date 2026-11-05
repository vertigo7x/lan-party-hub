using System.Net;
using System.Text;
using System.Text.Json;
using LANPartyHub.Client.Application.Downloads;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Domain.Trust;
using LANPartyHub.Client.Host;
using LANPartyHub.Client.Infrastructure.Persistence;
using LANPartyHub.Client.Presentation.Downloads;
using LANPartyHub.Client.Presentation.Errors;
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
/// The presentation of a FAILED VALIDATION over the REAL client and the REAL server
/// (AC1, AC2, AC3, AC4, UX-DR-6, UX-DR-33, UX-DR-36, AD-8, AD-16).
///
/// <para>
/// El test arranca el host real, con el Kestrel real y un <c>catalog.sqlite</c> real, y
/// monta la composición real de las descargas del cliente sobre una base de datos real del
/// cliente con el ancla de confianza YA FIJADA. Así la microcopia diferenciada y el
/// <c>toast-error</c> con sus tres partes se leen DEL CLIENTE, con una validación fallida
/// REAL (AD-8, AD-16).
/// </para>
/// <para>
/// Los dos fallos son los dos reales: los BYTES alterados hacen un paquete DAÑADO, y el
/// manifiesto firmado con OTRA clave hace un paquete NO CONFIABLE (AD-2, AD-8, AD-27).
/// </para>
/// </summary>
[Collection(RealHostCollection.Name)]
public sealed class ValidationFailurePresentationTests : IDisposable
{
    private const string ClientId = "0193f2a1b2c34d4e5f60718293a4b5c6";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly List<string> _temporaryFolders = [];

    /// <summary>
    /// A package whose bytes were ALTERED is shown to the player as DAMAGED, con la
    /// microcopia exacta del spine de la experiencia (AC1, UX-DR-6).
    /// </summary>
    [Fact]
    public async Task A_package_with_altered_bytes_is_shown_as_damaged()
    {
        var dataDirectory = CreateTemporaryDataDirectory();
        var clientFolder = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        const string packageId = "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5f01";
        var bytes = WritePackageBytes(dataDirectory, packageId, "el contenido íntegro del paquete");

        await PublishAsync(host, dataDirectory, packageId, bytes.LongLength);

        await FixAnchorAsync(clientFolder);

        using var harness = CreateComposition(host, clientFolder, alterContent: true);

        var handle = await harness.Coordinator.StartAsync(
            packageId,
            BuildContentUri(host, packageId),
            TestContext.Current.CancellationToken);

        await WaitUntilAsync(() => handle.StateMachine.Current == DownloadState.Failed);
        await handle.WaitForActiveRunAsync();

        var viewModel = new DownloadProgressViewModel();

        viewModel.Apply(handle);

        // La microcopia diferenciada se LEE DEL CLIENTE, con la validación fallida real
        // (AC1, UX-DR-6).
        Assert.Equal(
            "Paquete dañado: el hash no coincide",
            viewModel.ValidationStateText);
    }

    /// <summary>
    /// The state of the damaged package carries the size of the package, as «Paquete
    /// dañado: el hash no coincide · 7,6 GB» (AC1, UX-DR-6, UX-DR-33).
    /// </summary>
    [Fact]
    public async Task The_state_of_the_damaged_package_carries_the_size()
    {
        var dataDirectory = CreateTemporaryDataDirectory();
        var clientFolder = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        const string packageId = "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5f02";
        var bytes = WritePackageBytes(dataDirectory, packageId, "el contenido íntegro del paquete");

        await PublishAsync(host, dataDirectory, packageId, bytes.LongLength);

        await FixAnchorAsync(clientFolder);

        using var harness = CreateComposition(host, clientFolder, alterContent: true);

        var handle = await harness.Coordinator.StartAsync(
            packageId,
            BuildContentUri(host, packageId),
            TestContext.Current.CancellationToken);

        await WaitUntilAsync(() => handle.StateMachine.Current == DownloadState.Failed);
        await handle.WaitForActiveRunAsync();

        var viewModel = new DownloadProgressViewModel();

        viewModel.Apply(handle);

        Assert.Equal(
            "Paquete dañado: el hash no coincide · 0,0 GB",
            viewModel.StateTextWithSize);
    }

    /// <summary>
    /// A package whose signed manifest does NOT validate against the fixed anchor is shown
    /// to the player as NOT TRUSTWORTHY, con la microcopia exacta del spine de la
    /// experiencia (AC2, UX-DR-6).
    /// </summary>
    [Fact]
    public async Task A_package_that_is_not_trustworthy_is_shown_as_untrusted()
    {
        var dataDirectory = CreateTemporaryDataDirectory();
        var clientFolder = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        const string packageId = "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5f03";
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

        // La falta de autenticidad se presenta con la microcopia diferenciada, y NO con
        // la del paquete dañado (AC2, UX-DR-6).
        var viewModel = new ErrorMessageViewModel(failure.Result);

        Assert.Equal(
            ErrorCatalog.ManifestSignatureInvalidCode,
            viewModel.Code);

        Assert.Equal(
            "Paquete no confiable: no se pudo validar contra la raíz de confianza",
            StateDisplayMap.GetText(viewModel.Code));
    }

    /// <summary>
    /// The two failures have DIFFERENT microcopy and DIFFERENT causes, así que el jugador
    /// sabe si el problema es la red o la confianza (AC1, AC2).
    /// </summary>
    [Fact]
    public async Task The_two_failures_have_different_microcopy_and_different_causes()
    {
        var damaged = ErrorCatalog.Resolve(ErrorCatalog.PackageIntegrityMismatchCode);
        var untrusted = ErrorCatalog.Resolve(ErrorCatalog.ManifestSignatureInvalidCode);

        Assert.NotEqual(damaged.Message, untrusted.Message);
        Assert.NotEqual(damaged.Cause, untrusted.Cause);

        Assert.NotEqual(
            StateDisplayMap.GetText(ErrorCatalog.PackageIntegrityMismatchCode),
            StateDisplayMap.GetText(ErrorCatalog.ManifestSignatureInvalidCode));
    }

    /// <summary>
    /// The notice of error of the client carries the THREE parts of the catalog, con la
    /// validación fallida real (AC3, UX-DR-36).
    /// </summary>
    [Fact]
    public async Task The_error_notice_carries_the_three_parts_of_the_real_failure()
    {
        var dataDirectory = CreateTemporaryDataDirectory();
        var clientFolder = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        const string packageId = "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5f04";
        var bytes = WritePackageBytes(dataDirectory, packageId, "el contenido íntegro del paquete");

        await PublishAsync(host, dataDirectory, packageId, bytes.LongLength);

        await FixAnchorAsync(clientFolder);

        using var harness = CreateComposition(host, clientFolder, alterContent: true);

        var handle = await harness.Coordinator.StartAsync(
            packageId,
            BuildContentUri(host, packageId),
            TestContext.Current.CancellationToken);

        await WaitUntilAsync(() => handle.StateMachine.Current == DownloadState.Failed);
        await handle.WaitForActiveRunAsync();

        var viewModel = new DownloadProgressViewModel();

        viewModel.Apply(handle);

        var error = viewModel.Error!;

        // El <c>toast-error</c> muestra qué falló, por qué y qué hacer ahora (AC3,
        // UX-DR-36).
        var toast = new ErrorToast(error);

        toast.Show(error);

        Assert.Equal(error.Message, toast.MessageText.Text);
        Assert.Equal(error.Cause, toast.CauseText.Text);
        Assert.Equal(error.NextStep, toast.NextStepText.Text);

        // Y las tres partes son las del catálogo, sin reescribir (AD-15).
        var catalog = ErrorCatalog.Resolve(ErrorCatalog.PackageIntegrityMismatchCode);

        Assert.Equal(catalog.Message, toast.MessageText.Text);
        Assert.Equal(catalog.Cause, toast.CauseText.Text);
        Assert.Equal(catalog.NextStep, toast.NextStepText.Text);
    }

    /// <summary>
    /// The alta de la instalación is BLOCKED with a VISIBLE reason, y no con el código
    /// estable del catálogo (AC1, UX-DR-33, AC4).
    /// </summary>
    [Fact]
    public async Task The_installation_registration_is_blocked_with_a_visible_reason()
    {
        var dataDirectory = CreateTemporaryDataDirectory();
        var clientFolder = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        const string packageId = "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5f05";
        var bytes = WritePackageBytes(dataDirectory, packageId, "el contenido íntegro del paquete");

        await PublishAsync(host, dataDirectory, packageId, bytes.LongLength);

        await FixAnchorAsync(clientFolder);

        using var harness = CreateComposition(host, clientFolder, alterContent: true);

        var handle = await harness.Coordinator.StartAsync(
            packageId,
            BuildContentUri(host, packageId),
            TestContext.Current.CancellationToken);

        await WaitUntilAsync(() => handle.StateMachine.Current == DownloadState.Failed);
        await handle.WaitForActiveRunAsync();

        var viewModel = new DownloadProgressViewModel();

        viewModel.Apply(handle);

        Assert.True(viewModel.IsInstallBlocked);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.InstallBlockedReason));

        // El código estable NUNCA es el texto del motivo (AC3, AC4).
        Assert.DoesNotContain(
            ErrorCatalog.PackageIntegrityMismatchCode,
            viewModel.InstallBlockedReason!,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Builds the composition of the downloads over the real host, with the real layout of
    /// the client and the real store of su base de datos (AD-16).
    /// </summary>
    /// <param name="host">The real host of the event.</param>
    /// <param name="clientFolder">Folder of the data of the client.</param>
    /// <param name="alterContent">
    /// When it is <c>true</c>, the engine serves bytes ALTERADOS, para que el hash no
    /// coincida con el del manifiesto firmado (AD-8).
    /// </param>
    /// <returns>The composition of the downloads.</returns>
    private static ClientHarness CreateComposition(
        TestServer host,
        string clientFolder,
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
            NullLoggerFactory.Instance,
            Environment.MachineName);

        correlation.Open(ClientId);

        var transfer = new TransferComposition(correlation);

        var composition = new DownloadComposition(
            new Uri($"http://{host.BoundAddress}:{host.Port}"),
            correlation,
            stateStore,
            layout,
            alterContent ? new AlteringTransferEngine(transfer.Engine) : transfer.Engine,
            NullLoggerFactory.Instance,
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
        var folder = Directory.CreateTempSubdirectory("lan-party-hub-validation-presentation-");

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
