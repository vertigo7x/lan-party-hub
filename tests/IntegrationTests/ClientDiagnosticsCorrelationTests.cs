using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LANPartyHub.Client.Application;
using LANPartyHub.Client.Application.Clients;
using LANPartyHub.Client.Application.Diagnostics;
using LANPartyHub.Client.Application.Downloads;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Domain;
using LANPartyHub.Client.Domain.StateMachines;
using LANPartyHub.Client.Infrastructure.Networking;
using LANPartyHub.Client.Infrastructure.Persistence;
using LANPartyHub.Client.Presentation.Diagnostics;
using LANPartyHub.Server.Host;
using LANPartyHub.Server.Infrastructure.Http;
using LANPartyHub.Shared.Contracts;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LANPartyHub.IntegrationTests;

/// <summary>
/// The CORRELATION of the diagnostics of the client with the log of the server
/// (AC1, AC2, AC3, NFR-7, AD-15, AD-22, AD-23, AR-20).
///
/// <para>
/// El error de una descarga queda PERSISTIDO en la base de datos del cliente, PUBLICADO
/// al servidor como <c>ClientErrorReported</c> y PRESENTADO en el visor de diagnóstico,
/// y el <c>operationId</c> y el <c>clientId</c> que lleva son los MISMOS que viajan en el
/// log del servidor, así que un error del servidor y su correspondiente en el cliente se
/// pueden UNIR (AC3).
/// </para>
/// <para>
/// El test arranca el host REAL y usa el cliente REAL de SignalR, porque el enlace del
/// nombre del método del hub NO es observable con una prueba estructural: un nombre
/// distinto hace que el servidor responda «Method does not exist», el publicador se come
/// el error y el diagnóstico se pierde en silencio (AD-16).
/// </para>
/// </summary>
public sealed class ClientDiagnosticsCorrelationTests : IDisposable
{
    private const string ClientId = "0193f2a1b2c34d4e5f60718293a4b5c6";
    private const string PackageId = "paquete-1";
    private const string ContentUri = "http://192.168.1.10:5000/api/v1/packages/paquete-1/content";

    private readonly string _workingDirectory;
    private readonly string _databasePath;

    /// <summary>Creates an isolated working directory for the test.</summary>
    public ClientDiagnosticsCorrelationTests()
    {
        _workingDirectory = Path.Combine(
            Path.GetTempPath(),
            "lanpartyhub-integration",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(_workingDirectory);

        _databasePath = Path.Combine(_workingDirectory, "state.sqlite");
    }

    /// <summary>Removes the working directory of the test.</summary>
    public void Dispose()
    {
        if (Directory.Exists(_workingDirectory))
        {
            Directory.Delete(_workingDirectory, recursive: true);
        }
    }

    /// <summary>
    /// AC1, AC2, AC3: el error de una descarga fallida queda persistido en el cliente,
    /// publicado al servidor y presentado en el visor con los identificadores que comparte
    /// con el log del servidor (NFR-7, AD-23, AR-20).
    /// </summary>
    [Fact]
    public async Task The_error_of_a_failed_download_is_persisted_published_and_presented()
    {
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        using var http = host.CreateClient();

        await RegisterAsync(http);

        await using var context = CreateContext();

        var store = new ClientStateStore(context);

        await store.MigrateAsync(TestContext.Current.CancellationToken);

        await using var publisher = new ClientStateHubPublisher(
            new Uri($"http://{host.BoundAddress}:{host.Port}"),
            NullLogger<ClientStateHubPublisher>.Instance);

        var recorder = new RecordClientError(
            store,
            publisher,
            new FixedIdentity(),
            NullLogger<RecordClientError>.Instance);

        var coordinator = new DownloadCoordinator(
            new FailingTransferEngine(),
            new FixedIntentClient(),
            store,
            CreateLayout(),
            NullLogger<DownloadCoordinator>.Instance,
            errorRecorder: recorder);

        var handle = await coordinator.StartAsync(
            PackageId,
            new Uri(ContentUri),
            TestContext.Current.CancellationToken);

        await WaitUntilAsync(() => handle.StateMachine.Current == DownloadState.Failed);

        // AD-11, NFR-7: el error queda PERSISTIDO en la base de datos del cliente, así
        // que el diagnóstico sobrevive al reinistro del equipo.
        var persisted = await store.LoadErrorsAsync(TestContext.Current.CancellationToken);

        var row = Assert.Single(persisted);

        Assert.Equal(ClientId, row.ClientId);
        Assert.Equal(handle.OperationId, row.OperationId);
        Assert.Equal(OperationTypeNames.Download, row.OperationType);
        Assert.Equal(ErrorCatalog.UnknownErrorCode, row.Code);
        Assert.False(string.IsNullOrWhiteSpace(row.Message));
        Assert.False(string.IsNullOrWhiteSpace(row.Cause));
        Assert.False(string.IsNullOrWhiteSpace(row.NextStep));

        // AD-18, AD-22: el error PUBLICADO llega al servidor con el contrato exacto, y
        // el servidor lo persiste en su propio registro.
        var serverView = await ReadServerClientAsync(http);

        var serverErrors = serverView.GetProperty("errors");

        var serverError = Assert.Single(serverErrors.EnumerateArray());

        // AC3, AD-23: el log del cliente y el del servidor COMPARTEN el `clientId` y el
        // `operationId`, así que los dos diagnósticos se pueden UNIR.
        Assert.Equal(ClientId, serverView.GetProperty("clientId").GetString());
        Assert.Equal(handle.OperationId, serverError.GetProperty("operationId").GetString());
        Assert.Equal(row.Code, serverError.GetProperty("code").GetString());
        Assert.Equal(row.Message, serverError.GetProperty("message").GetString());
        Assert.Equal(row.Cause, serverError.GetProperty("cause").GetString());
        Assert.Equal(row.NextStep, serverError.GetProperty("nextStep").GetString());

        // AC1, AC2: el VISOR de diagnóstico presenta el error con sus cuatro partes y con
        // la asociación a la descarga que lo produjo.
        var viewModel = new DiagnosticsLogViewModel();

        viewModel.ApplyErrors(persisted);

        var entry = Assert.Single(viewModel.Entries);

        Assert.Equal(row.Code, entry.Code);
        Assert.Equal(row.Message, entry.Message);
        Assert.Equal(row.Cause, entry.Cause);
        Assert.Equal(row.NextStep, entry.NextStep);
        Assert.Equal(handle.OperationId, entry.OperationId);
        Assert.Equal(ClientId, entry.ClientId);
        Assert.Equal(OperationTypeNames.Download, entry.OperationType);
    }

    /// <summary>
    /// AD-1, AD-11, AD-15: un fallo del hub NO puede impedir que el error quede
    /// PERSISTIDO en el cliente, así que el diagnóstico no se pierde por un servidor que no
    /// está (NFR-7).
    /// </summary>
    [Fact]
    public async Task A_failing_hub_does_not_stop_the_error_from_being_persisted()
    {
        await using var context = CreateContext();

        var store = new ClientStateStore(context);

        await store.MigrateAsync(TestContext.Current.CancellationToken);

        // El publicador apunta a un puerto que no escucha: el hub no está.
        await using var publisher = new ClientStateHubPublisher(
            new Uri("http://127.0.0.1:1"),
            NullLogger<ClientStateHubPublisher>.Instance);

        var recorder = new RecordClientError(
            store,
            publisher,
            new FixedIdentity(),
            NullLogger<RecordClientError>.Instance);

        await recorder.RecordAsync(
            ErrorCatalog.Resolve(ErrorCatalog.PackageIntegrityMismatchCode),
            "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e70",
            OperationTypeNames.Download,
            TestContext.Current.CancellationToken);

        // AD-1, AD-11: el error queda en la base de datos del cliente aunque la
        // publicación al servidor no llegue.
        var persisted = await store.LoadErrorsAsync(TestContext.Current.CancellationToken);

        var row = Assert.Single(persisted);

        Assert.Equal(ErrorCatalog.PackageIntegrityMismatchCode, row.Code);
        Assert.Equal("0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e70", row.OperationId);
        Assert.Equal(ClientId, row.ClientId);
    }

    /// <summary>
    /// AD-22: el MISMO error registrado dos veces deja UNA sola fila, así que un
    /// reintento de la operación no duplica el diagnóstico que el jugador ve (NFR-7).
    /// </summary>
    [Fact]
    public async Task The_same_error_registered_twice_leaves_one_row()
    {
        await using var context = CreateContext();

        var store = new ClientStateStore(context);

        await store.MigrateAsync(TestContext.Current.CancellationToken);

        var recorder = new RecordClientError(
            store,
            publisher: null,
            new FixedIdentity(),
            NullLogger<RecordClientError>.Instance);

        var error = ErrorCatalog.Resolve(ErrorCatalog.PackageIntegrityMismatchCode);

        await recorder.RecordAsync(
            error,
            "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e70",
            OperationTypeNames.Download,
            TestContext.Current.CancellationToken);

        await recorder.RecordAsync(
            error,
            "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e70",
            OperationTypeNames.Download,
            TestContext.Current.CancellationToken);

        var persisted = await store.LoadErrorsAsync(TestContext.Current.CancellationToken);

        Assert.Single(persisted);
    }

    /// <summary>
    /// Registers the team against the real host, porque un error de un cliente que NO
    /// está registrado no crea ninguna fila (AD-30, AD-32).
    /// </summary>
    /// <param name="http">Client of the host.</param>
    /// <returns>A task that completes when the team has been registered.</returns>
    private static async Task RegisterAsync(HttpClient http)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, ClientsEndpoint.Route);

        request.Headers.Add(CorrelationMiddleware.ClientIdHeaderName, ClientId);
        request.Headers.TryAddWithoutValidation(
            ClientVersionMiddleware.ClientVersionHeaderName,
            "1.0.0");
        request.Headers.TryAddWithoutValidation(
            ClientVersionMiddleware.ProtocolVersionHeaderName,
            VersionInfo.CurrentProtocolVersion.ToString(
                System.Globalization.CultureInfo.InvariantCulture));

        request.Content = JsonContent.Create(new
        {
            clientId = ClientId,
            machineName = "EQ-PC",
            clientVersion = "1.0.0",
            protocolVersion = VersionInfo.CurrentProtocolVersion,
        });

        using var registration = await http.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, registration.StatusCode);
    }

    /// <summary>
    /// Reads the row of the team in the view of organization, así que la publicación se
    /// observa por el MISMO camino que el organizador usa (AD-22, FR-10).
    /// </summary>
    /// <param name="http">Client of the host.</param>
    /// <returns>The row of the team in the view of organization.</returns>
    private static async Task<JsonElement> ReadServerClientAsync(HttpClient http)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, ClientsEndpoint.Route);

        request.Headers.Add(CorrelationMiddleware.ClientIdHeaderName, ClientId);
        request.Headers.TryAddWithoutValidation(
            ClientVersionMiddleware.ClientVersionHeaderName,
            "1.0.0");
        request.Headers.TryAddWithoutValidation(
            ClientVersionMiddleware.ProtocolVersionHeaderName,
            VersionInfo.CurrentProtocolVersion.ToString(
                System.Globalization.CultureInfo.InvariantCulture));

        using var list = await http.SendAsync(request, TestContext.Current.CancellationToken);

        using var document = JsonDocument.Parse(
            await list.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var root = document.RootElement;

        Assert.Equal(JsonValueKind.Array, root.ValueKind);
        Assert.True(root.GetArrayLength() > 0, "El servidor no publicó ninguna fila de cliente.");

        return root[0].Clone();
    }

    /// <summary>
    /// Creates the context over the real SQLite file of the test (AD-11, AD-16).
    /// </summary>
    /// <returns>The context of the database of the client.</returns>
    private ClientStateDbContext CreateContext()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Pooling = false,
        }.ToString();

        var options = new DbContextOptionsBuilder<ClientStateDbContext>()
            .UseSqlite(
                connectionString,
                sqlite => sqlite.MigrationsAssembly(typeof(ClientStateDbContext).Assembly.GetName().Name))
            .Options;

        return new ClientStateDbContext(options);
    }

    /// <summary>
    /// Creates the layout of the data folder of the client (AD-9).
    /// </summary>
    /// <returns>The layout of the data folder.</returns>
    private ClientDataLayout CreateLayout() =>
        ClientDataLayout.Create(
            Path.Combine(_workingDirectory, "data"),
            Path.Combine(_workingDirectory, "install"));

    /// <summary>
    /// Waits until the condition holds, así que la prueba no depende de un plazo fijo
    /// (AD-16).
    /// </summary>
    /// <param name="condition">Condition that the test waits for.</param>
    /// <returns>A task that completes when the condition holds.</returns>
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);

        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(25, TestContext.Current.CancellationToken);
        }

        Assert.Fail("The expected state was not reached before the deadline.");
    }

    /// <summary>
    /// Source of the canonical identifier of the team, que el cliente ya emitió (AD-23).
    /// </summary>
    private sealed class FixedIdentity : ClientIdentityProvider
    {
        public string CanonicalId => ClientId;
    }

    /// <summary>
    /// Intención de descarga que devuelve un identificador de operación fijo, así que la
    /// prueba sabe qué <c>operationId</c> espera (AD-23).
    /// </summary>
    private sealed class FixedIntentClient : IDownloadIntentClient
    {
        private const string OperationId = "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e70";

        public Task<string> RegisterAsync(string packageId, CancellationToken cancellationToken) =>
            Task.FromResult(OperationId);
    }

    /// <summary>
    /// Motor de transferencia que FALLA, así que la descarga termina en «Failed» y el
    /// error queda registrado (AC5).
    /// </summary>
    private sealed class FailingTransferEngine : ITransferEngine
    {
        public Task<TransferOutcome> TransferAsync(
            Uri contentUri,
            long startInclusive,
            long endInclusive,
            Stream destination,
            CancellationToken cancellationToken) =>
            throw new IOException("El servidor no sirvió los bytes del rango.");
    }
}
