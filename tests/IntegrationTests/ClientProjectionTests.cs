using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LANPartyHub.Client.Infrastructure.Networking;
using LANPartyHub.Server.Host;
using LANPartyHub.Server.Infrastructure.Http;
using LANPartyHub.Shared.Contracts;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LANPartyHub.IntegrationTests;

/// <summary>
/// Projection of the REAL state that a client publishes to the running server
/// (AD-18, AD-22, AD-25, AD-29, AD-33, FR-10).
///
/// <para>
/// The tests exercise the real SignalR hub, the real REST surface and the real SQLite
/// persistence. No payload is only inspected in isolation: every assertion observes the
/// data that the server persisted or returned to the organizer.
/// </para>
/// </summary>
public sealed class ClientProjectionTests
{
    private const string ClientId = "0193f2a1b2c34d4e5f60718293a4b5c6";
    private const string OtherClientId = "0193f2a1b2c34d4e5f60718293a4b5c7";
    private const string GameId = "0193f2a1-b2c3-4d4e-5f60-718293a4b5c6";
    private const string OtherGameId = "0193f2a1-b2c3-4d4e-5f60-718293a4b5c7";
    private const string DownloadOperationId = "0193f2a1-b2c3-4d4e-5f60-718293a4b5c8";
    private const string InstallationOperationId = "0193f2a1-b2c3-4d4e-5f60-718293a4b5c9";

    [Fact]
    public async Task The_state_that_the_client_publishes_is_projected_in_the_rest_view()
    {
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);
        using var http = host.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(http, ClientId)).StatusCode);

        await using var connection = await ConnectAsync(host);
        await connection.InvokeAsync(
            ClientStateHubPublisher.ClientStateChangedMethodName,
            new ClientStateChanged(
                ClientId,
                ConnectionState.AnchorVerified,
                [
                    new ClientStateChanged.InstalledGame(
                        GameId,
                        "1.2.3",
                        InstallationState.Installed)
                ],
                [
                    new ClientStateChanged.ActiveOperation(
                        DownloadOperationId,
                        "download",
                        nameof(DownloadState.Downloading))
                ]),
            TestContext.Current.CancellationToken);

        var client = Assert.Single(await ReadClientsAsync(http));
        var installed = Assert.Single(client.GetProperty("installedGames").EnumerateArray());
        var operation = Assert.Single(client.GetProperty("activeOperations").EnumerateArray());

        Assert.Equal(GameId, installed.GetProperty("gameId").GetString());
        Assert.Equal("1.2.3", installed.GetProperty("installedVersion").GetString());
        Assert.Equal(
            StateDisplayMap.GetText(InstallationState.Installed),
            installed.GetProperty("validationStateText").GetString());
        Assert.Equal(DownloadOperationId, operation.GetProperty("operationId").GetString());
        Assert.Equal("download", operation.GetProperty("operationType").GetString());
        Assert.Equal(
            StateDisplayMap.GetText(DownloadState.Downloading),
            operation.GetProperty("stateText").GetString());
        Assert.Equal(JsonValueKind.Null, operation.GetProperty("phase").ValueKind);
    }

    [Fact]
    public async Task The_installations_are_persisted_per_client_and_game_and_survive_the_restart()
    {
        var dataDirectory = Directory.CreateTempSubdirectory("lan-party-hub-projection-").FullName;
        var port = GetFreeTcpPort();

        try
        {
            await using (var host = await TestServer.StartAsync(dataDirectory, port, TestContext.Current.CancellationToken))
            {
                using var http = host.CreateClient();

                Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(http, ClientId)).StatusCode);
                Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(http, OtherClientId)).StatusCode);

                await using var connection = await ConnectAsync(host);
                await connection.InvokeAsync(
                    ClientStateHubPublisher.ClientStateChangedMethodName,
                    new ClientStateChanged(
                        ClientId,
                        ConnectionState.AnchorVerified,
                        [
                            new ClientStateChanged.InstalledGame(GameId, "1.0.0", InstallationState.Installed),
                            new ClientStateChanged.InstalledGame(OtherGameId, "2.0.0", InstallationState.Repairable),
                        ],
                        []),
                    TestContext.Current.CancellationToken);

                await connection.InvokeAsync(
                    ClientStateHubPublisher.ClientStateChangedMethodName,
                    new ClientStateChanged(
                        OtherClientId,
                        ConnectionState.AnchorVerified,
                        [
                            new ClientStateChanged.InstalledGame(GameId, "9.0.0", InstallationState.Installed)
                        ],
                        []),
                    TestContext.Current.CancellationToken);
            }

            await using (var host = await TestServer.StartAsync(dataDirectory, port, TestContext.Current.CancellationToken))
            {
                using var http = host.CreateClient();
                var clients = await ReadClientsAsync(http);
                var first = Assert.Single(clients, candidate =>
                    candidate.GetProperty("clientId").GetString() == ClientId);
                var second = Assert.Single(clients, candidate =>
                    candidate.GetProperty("clientId").GetString() == OtherClientId);

                var firstGames = first.GetProperty("installedGames").EnumerateArray().ToArray();
                var secondGames = second.GetProperty("installedGames").EnumerateArray().ToArray();

                Assert.Equal(2, firstGames.Length);
                Assert.Single(secondGames);
                Assert.Equal(GameId, secondGames[0].GetProperty("gameId").GetString());
                Assert.Equal("9.0.0", secondGames[0].GetProperty("installedVersion").GetString());

                var rows = ReadInstallationRows(dataDirectory);
                Assert.Equal(3, rows.Count);
                Assert.Contains(rows, row =>
                    row.ClientId == ClientId && row.GameId == GameId && row.Version == "1.0.0");
                Assert.Contains(rows, row =>
                    row.ClientId == OtherClientId && row.GameId == GameId && row.Version == "9.0.0");
                Assert.All(rows, row =>
                    Assert.DoesNotContain("installPath", row.ValidationStateText, StringComparison.OrdinalIgnoreCase));
            }
        }
        finally
        {
            try
            {
                Directory.Delete(dataDirectory, recursive: true);
            }
            catch (IOException)
            {
                // The operating system can still hold one of the files.
            }
        }
    }

    [Fact]
    public async Task The_installation_progress_projects_the_real_phase_without_an_invented_percentage()
    {
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);
        using var http = host.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(http, ClientId)).StatusCode);

        await using var connection = await ConnectAsync(host);
        await connection.InvokeAsync(
            ClientStateHubPublisher.ClientStateChangedMethodName,
            new ClientStateChanged(
                ClientId,
                ConnectionState.AnchorVerified,
                [],
                [
                    new ClientStateChanged.ActiveOperation(
                        InstallationOperationId,
                        "installation",
                        nameof(InstallationState.Installing))
                ]),
            TestContext.Current.CancellationToken);

        await connection.InvokeAsync(
            ClientStateHubPublisher.InstallationProgressUpdatedMethodName,
            new InstallationProgressUpdated(
                ClientId,
                InstallationOperationId,
                InstallationState.Installing,
                "Extracting"),
            TestContext.Current.CancellationToken);

        var client = Assert.Single(await ReadClientsAsync(http));
        var operation = Assert.Single(client.GetProperty("activeOperations").EnumerateArray());

        Assert.Equal("Extracting", operation.GetProperty("phase").GetString());
        Assert.Equal(JsonValueKind.Null, operation.GetProperty("bytesTransferred").ValueKind);
        Assert.Equal(JsonValueKind.Null, operation.GetProperty("totalBytes").ValueKind);
    }

    [Fact]
    public async Task The_real_phase_survives_the_snapshot_that_the_client_publishes_afterwards()
    {
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);
        using var http = host.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(http, ClientId)).StatusCode);

        await using var connection = await ConnectAsync(host);
        await connection.InvokeAsync(
            ClientStateHubPublisher.ClientStateChangedMethodName,
            new ClientStateChanged(
                ClientId,
                ConnectionState.AnchorVerified,
                [],
                [
                    new ClientStateChanged.ActiveOperation(
                        InstallationOperationId,
                        "installation",
                        nameof(InstallationState.Installing))
                ]),
            TestContext.Current.CancellationToken);

        await connection.InvokeAsync(
            ClientStateHubPublisher.InstallationProgressUpdatedMethodName,
            new InstallationProgressUpdated(
                ClientId,
                InstallationOperationId,
                InstallationState.Installing,
                "Extracting"),
            TestContext.Current.CancellationToken);

        // AD-22: el cliente vuelve a publicar su estado COMPLETO, y la fase REAL que ya
        // declaró NO se pierde: la lista declara el tipo y el estado de la operación, no
        // su progreso.
        await connection.InvokeAsync(
            ClientStateHubPublisher.ClientStateChangedMethodName,
            new ClientStateChanged(
                ClientId,
                ConnectionState.AnchorVerified,
                [],
                [
                    new ClientStateChanged.ActiveOperation(
                        InstallationOperationId,
                        "installation",
                        nameof(InstallationState.Installing))
                ]),
            TestContext.Current.CancellationToken);

        var client = Assert.Single(await ReadClientsAsync(http));
        var operation = Assert.Single(client.GetProperty("activeOperations").EnumerateArray());

        Assert.Equal(InstallationOperationId, operation.GetProperty("operationId").GetString());
        Assert.Equal("Extracting", operation.GetProperty("phase").GetString());
    }

    [Fact]
    public async Task The_operation_that_the_client_no_longer_declares_leaves_the_view()
    {
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);
        using var http = host.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(http, ClientId)).StatusCode);

        await using var connection = await ConnectAsync(host);
        await connection.InvokeAsync(
            ClientStateHubPublisher.ClientStateChangedMethodName,
            new ClientStateChanged(
                ClientId,
                ConnectionState.AnchorVerified,
                [],
                [
                    new ClientStateChanged.ActiveOperation(
                        InstallationOperationId,
                        "installation",
                        nameof(InstallationState.Installing))
                ]),
            TestContext.Current.CancellationToken);

        // AD-22: la operación TERMINADA deja de declararse en la lista de operaciones en
        // curso, así que el servidor deja de proyectarla.
        await connection.InvokeAsync(
            ClientStateHubPublisher.ClientStateChangedMethodName,
            new ClientStateChanged(ClientId, ConnectionState.AnchorVerified, [], []),
            TestContext.Current.CancellationToken);

        var client = Assert.Single(await ReadClientsAsync(http));

        Assert.Empty(client.GetProperty("activeOperations").EnumerateArray());
    }

    [Fact]
    public async Task A_withdrawn_package_is_a_result_and_not_a_client_error()
    {
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);
        using var http = host.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(http, ClientId)).StatusCode);

        await using var connection = await ConnectAsync(host);
        await connection.InvokeAsync(
            ClientStateHubPublisher.ClientStateChangedMethodName,
            new ClientStateChanged(
                ClientId,
                ConnectionState.AnchorVerified,
                [
                    new ClientStateChanged.InstalledGame(GameId, "1.0.0", InstallationState.Installed)
                ],
                []),
            TestContext.Current.CancellationToken);

        await connection.InvokeAsync(
            ClientStateHubPublisher.OperationOutcomeReportedMethodName,
            new OperationOutcomeReported(
                ClientId,
                DownloadOperationId,
                OperationOutcome.PackageWithdrawn),
            TestContext.Current.CancellationToken);

        var client = Assert.Single(await ReadClientsAsync(http));
        var outcome = Assert.Single(client.GetProperty("recentOutcomes").EnumerateArray());

        Assert.Equal(DownloadOperationId, outcome.GetProperty("operationId").GetString());
        Assert.Equal(
            StateDisplayMap.GetText(OperationOutcome.PackageWithdrawn),
            outcome.GetProperty("outcomeText").GetString());
        Assert.Empty(client.GetProperty("errors").EnumerateArray());
        Assert.Single(client.GetProperty("installedGames").EnumerateArray());
    }

    [Fact]
    public async Task The_ordinary_download_progress_creates_the_active_operation_with_its_real_bytes()
    {
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);
        using var http = host.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(http, ClientId)).StatusCode);

        await using var connection = await ConnectAsync(host);

        // AD-22: el progreso de una descarga ORDINARIA es la PRIMERA noticia que el
        // servidor tiene de esa operación, así que la proyección la CREA con el estado
        // REAL que declara el propio progreso. El cliente NO publica ningún
        // `ClientStateChanged` con la operación, que es lo que pasa en una descarga normal.
        await connection.InvokeAsync(
            ClientStateHubPublisher.DownloadProgressUpdatedMethodName,
            new DownloadProgressUpdated(
                ClientId,
                DownloadOperationId,
                1024,
                4096,
                DownloadState.Downloading),
            TestContext.Current.CancellationToken);

        var client = Assert.Single(await ReadClientsAsync(http));
        var operation = Assert.Single(client.GetProperty("activeOperations").EnumerateArray());

        Assert.Equal(DownloadOperationId, operation.GetProperty("operationId").GetString());
        Assert.Equal("download", operation.GetProperty("operationType").GetString());
        Assert.Equal(
            StateDisplayMap.GetText(DownloadState.Downloading),
            operation.GetProperty("stateText").GetString());
        Assert.Equal(1024, operation.GetProperty("bytesTransferred").GetInt64());
        Assert.Equal(4096, operation.GetProperty("totalBytes").GetInt64());
        Assert.Equal(JsonValueKind.Null, operation.GetProperty("phase").ValueKind);
    }

    [Fact]
    public async Task The_progress_of_a_download_updates_the_bytes_that_the_operation_already_declared()
    {
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);
        using var http = host.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(http, ClientId)).StatusCode);

        await using var connection = await ConnectAsync(host);

        await connection.InvokeAsync(
            ClientStateHubPublisher.ClientStateChangedMethodName,
            new ClientStateChanged(
                ClientId,
                ConnectionState.AnchorVerified,
                [],
                [
                    new ClientStateChanged.ActiveOperation(
                        DownloadOperationId,
                        "download",
                        nameof(DownloadState.Queued))
                ]),
            TestContext.Current.CancellationToken);

        await connection.InvokeAsync(
            ClientStateHubPublisher.DownloadProgressUpdatedMethodName,
            new DownloadProgressUpdated(
                ClientId,
                DownloadOperationId,
                2048,
                4096,
                DownloadState.Downloading),
            TestContext.Current.CancellationToken);

        var client = Assert.Single(await ReadClientsAsync(http));
        var operation = Assert.Single(client.GetProperty("activeOperations").EnumerateArray());

        // AD-22: el estado REAL del progreso sustituye al que la lista declaraba, y los
        // bytes son los REALES que el cliente declaró.
        Assert.Equal(
            StateDisplayMap.GetText(DownloadState.Downloading),
            operation.GetProperty("stateText").GetString());
        Assert.Equal(2048, operation.GetProperty("bytesTransferred").GetInt64());
        Assert.Equal(4096, operation.GetProperty("totalBytes").GetInt64());
    }

    [Fact]
    public async Task The_terminal_progress_of_a_download_leaves_the_active_operations()
    {
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);
        using var http = host.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(http, ClientId)).StatusCode);

        await using var connection = await ConnectAsync(host);

        await connection.InvokeAsync(
            ClientStateHubPublisher.DownloadProgressUpdatedMethodName,
            new DownloadProgressUpdated(
                ClientId,
                DownloadOperationId,
                4096,
                4096,
                DownloadState.Downloading),
            TestContext.Current.CancellationToken);

        Assert.Single(
            Assert.Single(await ReadClientsAsync(http))
                .GetProperty("activeOperations").EnumerateArray());

        // AD-10, AD-22: la descarga TERMINADA deja de estar en curso, así que la tabla del
        // organizador no muestra una operación que ya terminó.
        await connection.InvokeAsync(
            ClientStateHubPublisher.DownloadProgressUpdatedMethodName,
            new DownloadProgressUpdated(
                ClientId,
                DownloadOperationId,
                4096,
                4096,
                DownloadState.Completed),
            TestContext.Current.CancellationToken);

        var client = Assert.Single(await ReadClientsAsync(http));

        Assert.Empty(client.GetProperty("activeOperations").EnumerateArray());
    }

    [Fact]
    public async Task The_terminal_progress_of_a_download_only_closes_the_operation_of_its_own_client()
    {
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);
        using var http = host.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(http, ClientId)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(http, OtherClientId)).StatusCode);

        await using var connection = await ConnectAsync(host);

        await connection.InvokeAsync(
            ClientStateHubPublisher.DownloadProgressUpdatedMethodName,
            new DownloadProgressUpdated(
                ClientId,
                DownloadOperationId,
                1024,
                4096,
                DownloadState.Downloading),
            TestContext.Current.CancellationToken);

        await connection.InvokeAsync(
            ClientStateHubPublisher.DownloadProgressUpdatedMethodName,
            new DownloadProgressUpdated(
                OtherClientId,
                DownloadOperationId,
                512,
                4096,
                DownloadState.Downloading),
            TestContext.Current.CancellationToken);

        // AD-22: la operación es del cliente que la declaró, así que el progreso terminal
        // de un cliente NUNCA cierra la operación de otro.
        await connection.InvokeAsync(
            ClientStateHubPublisher.DownloadProgressUpdatedMethodName,
            new DownloadProgressUpdated(
                ClientId,
                DownloadOperationId,
                4096,
                4096,
                DownloadState.Completed),
            TestContext.Current.CancellationToken);

        var clients = await ReadClientsAsync(http);
        var first = Assert.Single(clients, candidate =>
            candidate.GetProperty("clientId").GetString() == ClientId);
        var second = Assert.Single(clients, candidate =>
            candidate.GetProperty("clientId").GetString() == OtherClientId);

        Assert.Empty(first.GetProperty("activeOperations").EnumerateArray());
        Assert.Single(second.GetProperty("activeOperations").EnumerateArray());
    }

    [Fact]
    public async Task The_terminal_outcome_of_a_download_leaves_the_active_operations()
    {
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);
        using var http = host.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(http, ClientId)).StatusCode);

        await using var connection = await ConnectAsync(host);

        await connection.InvokeAsync(
            ClientStateHubPublisher.DownloadProgressUpdatedMethodName,
            new DownloadProgressUpdated(
                ClientId,
                DownloadOperationId,
                1024,
                4096,
                DownloadState.Downloading),
            TestContext.Current.CancellationToken);

        Assert.Single(
            Assert.Single(await ReadClientsAsync(http))
                .GetProperty("activeOperations").EnumerateArray());

        // AD-33: el resultado terminal cierra la operación en curso del cliente que lo
        // declara, y el resultado sigue visible como RESULTADO y no como error.
        await connection.InvokeAsync(
            ClientStateHubPublisher.OperationOutcomeReportedMethodName,
            new OperationOutcomeReported(
                ClientId,
                DownloadOperationId,
                OperationOutcome.PackageWithdrawn),
            TestContext.Current.CancellationToken);

        var client = Assert.Single(await ReadClientsAsync(http));

        Assert.Empty(client.GetProperty("activeOperations").EnumerateArray());

        var outcome = Assert.Single(client.GetProperty("recentOutcomes").EnumerateArray());

        Assert.Equal(DownloadOperationId, outcome.GetProperty("operationId").GetString());
        Assert.Empty(client.GetProperty("errors").EnumerateArray());
    }

    [Fact]
    public async Task The_terminal_outcome_of_a_client_only_closes_the_operation_of_that_client()
    {
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);
        using var http = host.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(http, ClientId)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(http, OtherClientId)).StatusCode);

        await using var connection = await ConnectAsync(host);

        await connection.InvokeAsync(
            ClientStateHubPublisher.DownloadProgressUpdatedMethodName,
            new DownloadProgressUpdated(
                ClientId,
                DownloadOperationId,
                1024,
                4096,
                DownloadState.Downloading),
            TestContext.Current.CancellationToken);

        await connection.InvokeAsync(
            ClientStateHubPublisher.DownloadProgressUpdatedMethodName,
            new DownloadProgressUpdated(
                OtherClientId,
                DownloadOperationId,
                512,
                4096,
                DownloadState.Downloading),
            TestContext.Current.CancellationToken);

        // AD-22: el resultado terminal de un cliente NUNCA cierra la operación de otro,
        // aunque los dos compartan el identificador de la operación.
        await connection.InvokeAsync(
            ClientStateHubPublisher.OperationOutcomeReportedMethodName,
            new OperationOutcomeReported(
                ClientId,
                DownloadOperationId,
                OperationOutcome.PackageWithdrawn),
            TestContext.Current.CancellationToken);

        var clients = await ReadClientsAsync(http);
        var first = Assert.Single(clients, candidate =>
            candidate.GetProperty("clientId").GetString() == ClientId);
        var second = Assert.Single(clients, candidate =>
            candidate.GetProperty("clientId").GetString() == OtherClientId);

        Assert.Empty(first.GetProperty("activeOperations").EnumerateArray());
        Assert.Single(second.GetProperty("activeOperations").EnumerateArray());
    }

    [Fact]
    public async Task The_terminal_outcome_is_persisted_in_the_real_download_intent_of_its_client()
    {
        var dataDirectory = Directory.CreateTempSubdirectory("lan-party-hub-intent-outcome-").FullName;
        var port = GetFreeTcpPort();

        try
        {
            await using var host = await TestServer.StartAsync(dataDirectory, port, TestContext.Current.CancellationToken);
            using var http = host.CreateClient();

            Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(http, ClientId)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(http, OtherClientId)).StatusCode);

            var packageId = await PublishPackageAsync(http, dataDirectory);

            var operationId = await RegisterDownloadIntentAsync(http, ClientId, packageId);
            var otherOperationId = await RegisterDownloadIntentAsync(http, OtherClientId, packageId);

            await using var connection = await ConnectAsync(host);
            await connection.InvokeAsync(
                ClientStateHubPublisher.OperationOutcomeReportedMethodName,
                new OperationOutcomeReported(
                    ClientId,
                    operationId,
                    OperationOutcome.PackageWithdrawn),
                TestContext.Current.CancellationToken);

            var rows = ReadDownloadIntentRows(dataDirectory);

            var own = Assert.Single(rows, row => row.OperationId == operationId);
            var other = Assert.Single(rows, row => row.OperationId == otherOperationId);

            Assert.Equal(ClientId, own.ClientId);
            Assert.Equal(nameof(OperationOutcome.PackageWithdrawn), own.Outcome);
            Assert.Equal(OtherClientId, other.ClientId);
            Assert.Equal(string.Empty, other.Outcome);
        }
        finally
        {
            try
            {
                Directory.Delete(dataDirectory, recursive: true);
            }
            catch (IOException)
            {
                // The operating system can still hold one of the files.
            }
        }
    }

    [Fact]
    public async Task A_real_client_error_is_exposed_by_the_single_clients_get_with_cause_and_next_step()
    {
        var dataDirectory = Directory.CreateTempSubdirectory("lan-party-hub-client-error-").FullName;
        var port = GetFreeTcpPort();

        try
        {
            await using var host = await TestServer.StartAsync(dataDirectory, port, TestContext.Current.CancellationToken);
            using var http = host.CreateClient();

            Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(http, ClientId)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(http, OtherClientId)).StatusCode);

            InsertClientError(
                dataDirectory,
                "0193f2a1-b2c3-4d4e-5f60-718293a4b5ca",
                ClientId,
                DownloadOperationId,
                "DOWNLOAD_FAILED",
                "No se pudo completar la descarga.",
                "El servidor cerró la transferencia.",
                "Reintenta la descarga.");

            var clients = await ReadClientsAsync(http);
            var first = Assert.Single(clients, candidate =>
                candidate.GetProperty("clientId").GetString() == ClientId);
            var second = Assert.Single(clients, candidate =>
                candidate.GetProperty("clientId").GetString() == OtherClientId);

            var error = Assert.Single(first.GetProperty("errors").EnumerateArray());

            Assert.Equal("0193f2a1-b2c3-4d4e-5f60-718293a4b5ca", error.GetProperty("errorId").GetString());
            Assert.Equal(DownloadOperationId, error.GetProperty("operationId").GetString());
            Assert.Equal("DOWNLOAD_FAILED", error.GetProperty("code").GetString());
            Assert.Equal("No se pudo completar la descarga.", error.GetProperty("message").GetString());
            Assert.Equal("El servidor cerró la transferencia.", error.GetProperty("cause").GetString());
            Assert.Equal("Reintenta la descarga.", error.GetProperty("nextStep").GetString());
            Assert.Empty(second.GetProperty("errors").EnumerateArray());
        }
        finally
        {
            try
            {
                Directory.Delete(dataDirectory, recursive: true);
            }
            catch (IOException)
            {
                // The operating system can still hold one of the files.
            }
        }
    }

    [Fact]
    public async Task The_error_that_the_client_reports_is_registered_with_its_cause_and_next_step()
    {
        var dataDirectory = Directory.CreateTempSubdirectory("lan-party-hub-reported-error-").FullName;
        var port = GetFreeTcpPort();

        try
        {
            await using var host = await TestServer.StartAsync(dataDirectory, port, TestContext.Current.CancellationToken);
            using var http = host.CreateClient();

            Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(http, ClientId)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(http, OtherClientId)).StatusCode);

            // AD-15, AC1: el error que viaja en el payload es del ÚNICO tipo de error del
            // sistema, `ApiError`, con su código estable, su mensaje, su causa y su
            // siguiente paso.
            var error = ErrorCatalog.Resolve(ErrorCatalog.PackageIntegrityMismatchCode);

            await using var connection = await ConnectAsync(host);
            await connection.InvokeAsync(
                CatalogHub.ClientErrorReportedMethodName,
                new ClientErrorReported(ClientId, DownloadOperationId, error),
                TestContext.Current.CancellationToken);

            var clients = await ReadClientsAsync(http);
            var first = Assert.Single(clients, candidate =>
                candidate.GetProperty("clientId").GetString() == ClientId);
            var second = Assert.Single(clients, candidate =>
                candidate.GetProperty("clientId").GetString() == OtherClientId);

            var registered = Assert.Single(first.GetProperty("errors").EnumerateArray());

            // AC1: el `ClientError` persistido lleva TODAS sus partes.
            Assert.False(string.IsNullOrWhiteSpace(registered.GetProperty("errorId").GetString()));
            Assert.Equal(DownloadOperationId, registered.GetProperty("operationId").GetString());
            Assert.Equal(ErrorCatalog.PackageIntegrityMismatchCode, registered.GetProperty("code").GetString());
            Assert.Equal(error.Message, registered.GetProperty("message").GetString());
            Assert.Equal(error.Cause, registered.GetProperty("cause").GetString());
            Assert.Equal(error.NextStep, registered.GetProperty("nextStep").GetString());
            Assert.False(string.IsNullOrWhiteSpace(registered.GetProperty("occurredAt").GetString()));

            // AC1, AD-29: el error es del cliente que lo reportó y de ningún otro.
            Assert.Empty(second.GetProperty("errors").EnumerateArray());

            // AC1: la fila del servidor guarda el error con TODAS sus partes.
            var rows = ReadClientErrorRows(dataDirectory);
            var row = Assert.Single(rows);

            Assert.Equal(ClientId, row.ClientId);
            Assert.Equal(DownloadOperationId, row.OperationId);
            Assert.Equal(ErrorCatalog.PackageIntegrityMismatchCode, row.Code);
            Assert.Equal(error.Message, row.Message);
            Assert.Equal(error.Cause, row.Cause);
            Assert.Equal(error.NextStep, row.NextStep);
            Assert.False(string.IsNullOrWhiteSpace(row.ErrorId));
            Assert.False(string.IsNullOrWhiteSpace(row.OccurredAt));
        }
        finally
        {
            try
            {
                Directory.Delete(dataDirectory, recursive: true);
            }
            catch (IOException)
            {
                // The operating system can still hold one of the files.
            }
        }
    }

    [Fact]
    public async Task The_error_of_a_client_that_is_not_registered_does_not_create_a_row()
    {
        var dataDirectory = Directory.CreateTempSubdirectory("lan-party-hub-unknown-error-").FullName;
        var port = GetFreeTcpPort();

        try
        {
            await using var host = await TestServer.StartAsync(dataDirectory, port, TestContext.Current.CancellationToken);
            using var http = host.CreateClient();

            var error = ErrorCatalog.Resolve(ErrorCatalog.PackageIntegrityMismatchCode);

            await using var connection = await ConnectAsync(host);
            await connection.InvokeAsync(
                CatalogHub.ClientErrorReportedMethodName,
                new ClientErrorReported(OtherClientId, DownloadOperationId, error),
                TestContext.Current.CancellationToken);

            // AD-30, AD-32: el alta del equipo es la operación de REGISTRO, así que el
            // error de un cliente que no está registrado no crea ninguna fila.
            Assert.Empty(await ReadClientsAsync(http));
            Assert.Empty(ReadClientErrorRows(dataDirectory));
        }
        finally
        {
            try
            {
                Directory.Delete(dataDirectory, recursive: true);
            }
            catch (IOException)
            {
                // The operating system can still hold one of the files.
            }
        }
    }

    [Fact]
    public async Task The_organizer_reads_all_the_clients_with_one_get_and_no_request_per_client()
    {        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);
        using var http = host.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(http, ClientId)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(http, OtherClientId)).StatusCode);

        using var request = BuildRequest(HttpMethod.Get, ClientsEndpoint.Route);
        using var response = await http.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var clients = await ReadArrayAsync(response);
        Assert.Equal(2, clients.GetArrayLength());
    }

    [Fact]
    public async Task A_client_that_reconnects_replaces_the_real_state_without_creating_a_second_row()
    {
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);
        using var http = host.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(http, ClientId)).StatusCode);

        await using var firstConnection = await ConnectAsync(host);
        await firstConnection.InvokeAsync(
            ClientStateHubPublisher.ClientStateChangedMethodName,
            new ClientStateChanged(
                ClientId,
                ConnectionState.AnchorVerified,
                [
                    new ClientStateChanged.InstalledGame(GameId, "1.0.0", InstallationState.Installed)
                ],
                [
                    new ClientStateChanged.ActiveOperation(
                        DownloadOperationId,
                        "download",
                        nameof(DownloadState.Downloading))
                ]),
            TestContext.Current.CancellationToken);

        await firstConnection.StopAsync(TestContext.Current.CancellationToken);
        await firstConnection.DisposeAsync();

        Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(http, ClientId)).StatusCode);

        await using var reconnected = await ConnectAsync(host);
        await reconnected.InvokeAsync(
            ClientStateHubPublisher.ClientStateChangedMethodName,
            new ClientStateChanged(ClientId, ConnectionState.AnchorVerified, [], []),
            TestContext.Current.CancellationToken);

        var client = Assert.Single(await ReadClientsAsync(http));
        Assert.Empty(client.GetProperty("activeOperations").EnumerateArray());

        // La instantánea completa y vacía posterior a la reconexión confirma que el
        // cliente ya no tiene ese juego; la proyección se sincroniza con esa instantánea.
        Assert.Empty(client.GetProperty("installedGames").EnumerateArray());
    }

    [Fact]
    public async Task A_valid_empty_snapshot_removes_only_the_installations_of_that_client()
    {
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);
        using var http = host.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(http, ClientId)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(http, OtherClientId)).StatusCode);

        await using var connection = await ConnectAsync(host);
        await connection.InvokeAsync(
            ClientStateHubPublisher.ClientStateChangedMethodName,
            new ClientStateChanged(
                ClientId,
                ConnectionState.AnchorVerified,
                [
                    new ClientStateChanged.InstalledGame(GameId, "1.0.0", InstallationState.Installed)
                ],
                []),
            TestContext.Current.CancellationToken);

        await connection.InvokeAsync(
            ClientStateHubPublisher.ClientStateChangedMethodName,
            new ClientStateChanged(
                OtherClientId,
                ConnectionState.AnchorVerified,
                [
                    new ClientStateChanged.InstalledGame(OtherGameId, "2.0.0", InstallationState.Installed)
                ],
                []),
            TestContext.Current.CancellationToken);

        // La instantánea válida y vacía confirma que el cliente ya no tiene ese juego.
        await connection.InvokeAsync(
            ClientStateHubPublisher.ClientStateChangedMethodName,
            new ClientStateChanged(ClientId, ConnectionState.AnchorVerified, [], []),
            TestContext.Current.CancellationToken);

        var clients = await ReadClientsAsync(http);
        var first = Assert.Single(clients, candidate =>
            candidate.GetProperty("clientId").GetString() == ClientId);
        var second = Assert.Single(clients, candidate =>
            candidate.GetProperty("clientId").GetString() == OtherClientId);

        Assert.Empty(first.GetProperty("installedGames").EnumerateArray());
        Assert.Single(second.GetProperty("installedGames").EnumerateArray());
    }

    [Fact]
    public async Task The_connection_only_event_preserves_installations_and_operations()
    {
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);
        using var http = host.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(http, ClientId)).StatusCode);

        await using var connection = await ConnectAsync(host);
        await connection.InvokeAsync(
            ClientStateHubPublisher.ClientStateChangedMethodName,
            new ClientStateChanged(
                ClientId,
                ConnectionState.AnchorVerified,
                [
                    new ClientStateChanged.InstalledGame(GameId, "1.0.0", InstallationState.Installed)
                ],
                [
                    new ClientStateChanged.ActiveOperation(
                        DownloadOperationId,
                        "download",
                        nameof(DownloadState.Downloading))
                ]),
            TestContext.Current.CancellationToken);

        await connection.InvokeAsync(
            ClientStateHubPublisher.ClientConnectionChangedMethodName,
            new ClientConnectionChanged(ClientId, ConnectionState.Disconnected),
            TestContext.Current.CancellationToken);

        var client = Assert.Single(await ReadClientsAsync(http));

        Assert.Equal(
            (int)ConnectionState.Disconnected,
            client.GetProperty("connectionState").GetInt32());
        Assert.Single(client.GetProperty("installedGames").EnumerateArray());
        Assert.Single(client.GetProperty("activeOperations").EnumerateArray());
    }

    [Fact]
    public async Task The_connection_only_event_does_not_create_an_unregistered_client()
    {
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);
        using var http = host.CreateClient();

        await using var connection = await ConnectAsync(host);
        await connection.InvokeAsync(
            ClientStateHubPublisher.ClientConnectionChangedMethodName,
            new ClientConnectionChanged(OtherClientId, ConnectionState.AnchorVerified),
            TestContext.Current.CancellationToken);

        Assert.Empty(await ReadClientsAsync(http));
    }

    [Fact]
    public async Task The_connection_only_event_keeps_the_instant_of_the_registration()
    {
        // AD-30: el `lastSeenAt` es el instante del REGISTRO del cliente, no el del
        // último evento. El evento de conexión NO lo mueve: solo cambia el estado de
        // conexión de la fila (AD-22, AD-29).
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);
        using var http = host.CreateClient();

        using var registration = await RegisterAsync(http, ClientId);

        Assert.Equal(HttpStatusCode.OK, registration.StatusCode);

        using var registrationDocument = JsonDocument.Parse(
            await registration.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var registeredAt = registrationDocument.RootElement.GetProperty("lastSeenAt").GetDateTimeOffset();

        await using var connection = await ConnectAsync(host);
        await connection.InvokeAsync(
            ClientStateHubPublisher.ClientStateChangedMethodName,
            new ClientStateChanged(
                ClientId,
                ConnectionState.AnchorVerified,
                [
                    new ClientStateChanged.InstalledGame(GameId, "1.0.0", InstallationState.Installed)
                ],
                [
                    new ClientStateChanged.ActiveOperation(
                        DownloadOperationId,
                        "download",
                        nameof(DownloadState.Downloading))
                ]),
            TestContext.Current.CancellationToken);

        await connection.InvokeAsync(
            ClientStateHubPublisher.ClientConnectionChangedMethodName,
            new ClientConnectionChanged(ClientId, ConnectionState.Disconnected),
            TestContext.Current.CancellationToken);

        var client = Assert.Single(await ReadClientsAsync(http));

        // El estado de conexión SÍ cambia.
        Assert.Equal(
            (int)ConnectionState.Disconnected,
            client.GetProperty("connectionState").GetInt32());

        // El instante del registro NO cambia.
        Assert.Equal(
            registeredAt.ToUniversalTime(),
            client.GetProperty("lastSeenAt").GetDateTimeOffset().ToUniversalTime());

        // Las instalaciones y las operaciones en curso se conservan.
        Assert.Single(client.GetProperty("installedGames").EnumerateArray());
        Assert.Single(client.GetProperty("activeOperations").EnumerateArray());
    }

    private static async Task<string> PublishPackageAsync(HttpClient http, string dataDirectory)
    {
        var gameRequest = new GameRequest
        {
            SchemaVersion = GamesEndpoint.SupportedSchemaVersion,
            Game = new GameDescriptor(
                string.Empty,
                "Juego de prueba",
                "Juego publicado para registrar una intención real.",
                ["Prueba"],
                ["proyeccion"],
                "cover.png",
                ["captura-1.png"],
                "1.0.0",
                1024,
                1,
                1,
                "Windows",
                "Servidor",
                "Portable",
                new InstallationManifest.LaunchSettings("game.exe", [], "game"),
                ["DirectX 12"],
                "Propietaria",
                "Solo para el evento"),
        };

        using var gameResponse = await http.PostAsync(
            GamesEndpoint.Route,
            JsonContent.Create(gameRequest),
            TestContext.Current.CancellationToken);

        Assert.True(gameResponse.StatusCode == HttpStatusCode.Created,
            await gameResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var game = await gameResponse.Content.ReadFromJsonAsync<GameDescriptor>(
            TestContext.Current.CancellationToken);

        Assert.NotNull(game);

        var packageId = "0193f2a1-b2c3-4d4e-5f60-718293a4b5cb";
        var packages = Path.Combine(dataDirectory, LANPartyHub.Server.Infrastructure.Persistence.DataDirectory.PackagesFolderName);
        Directory.CreateDirectory(packages);
        File.WriteAllBytes(Path.Combine(packages, packageId), new byte[1024]);
        var packageRequest = new PackageRequest
        {
            SchemaVersion = PackagesEndpoint.SupportedSchemaVersion,
            GameId = game!.GameId,
            Package = new PackageDescriptor(
                packageId,
                game.GameId,
                "1.0.0",
                1024,
                0,
                string.Empty,
                string.Empty),
            Manifest = new InstallationManifest(
                LANPartyHub.Server.Application.Packages.InstallationManifestSchema.Version,
                packageId,
                "1.0.0",
                1024,
                string.Empty,
                new InstallationManifest.LaunchSettings("game.exe", [], "game"),
                [new InstallationManifest.CreateDirectory("game")]),
        };

        using var packageResponse = await http.PostAsync(
            PackagesEndpoint.Route,
            JsonContent.Create(packageRequest),
            TestContext.Current.CancellationToken);

        Assert.True(packageResponse.StatusCode == HttpStatusCode.Created,
            await packageResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        return packageId;
    }

    private static async Task<string> RegisterDownloadIntentAsync(
        HttpClient http,
        string clientId,
        string packageId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, DownloadsEndpoint.Route);
        request.Headers.Add(CorrelationMiddleware.ClientIdHeaderName, clientId);
        request.Headers.TryAddWithoutValidation(ClientVersionMiddleware.ClientVersionHeaderName, "1.0.0");
        request.Headers.TryAddWithoutValidation(
            ClientVersionMiddleware.ProtocolVersionHeaderName,
            VersionInfo.CurrentProtocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        request.Content = JsonContent.Create(new DownloadIntentRequest
        {
            SchemaVersion = DownloadsEndpoint.SupportedSchemaVersion,
            PackageId = packageId,
        });

        using var response = await http.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<DownloadIntentResponse>(
            TestContext.Current.CancellationToken);

        Assert.NotNull(payload);
        Assert.Equal(packageId, payload!.PackageId);

        return payload.OperationId;
    }

    private static async Task<HttpResponseMessage> RegisterAsync(HttpClient http, string clientId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, ClientsEndpoint.Route);
        request.Headers.Add(CorrelationMiddleware.ClientIdHeaderName, clientId);
        request.Headers.TryAddWithoutValidation(ClientVersionMiddleware.ClientVersionHeaderName, "1.0.0");
        request.Headers.TryAddWithoutValidation(
            ClientVersionMiddleware.ProtocolVersionHeaderName,
            VersionInfo.CurrentProtocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        request.Content = JsonContent.Create(new
        {
            clientId,
            machineName = clientId == ClientId ? "EQ-PC" : "Sala-2",
            clientVersion = "1.0.0",
            protocolVersion = VersionInfo.CurrentProtocolVersion,
        });

        return await http.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<HubConnection> ConnectAsync(TestServer host)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri($"http://{host.BoundAddress}:{host.Port}{CatalogHub.Route}"))
            .Build();

        await connection.StartAsync(TestContext.Current.CancellationToken);

        return connection;
    }

    private static async Task<JsonElement[]> ReadClientsAsync(HttpClient http)
    {
        using var request = BuildRequest(HttpMethod.Get, ClientsEndpoint.Route);
        using var response = await http.SendAsync(request, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();

        return (await ReadArrayAsync(response)).EnumerateArray().ToArray();
    }

    private static HttpRequestMessage BuildRequest(HttpMethod method, string route)
    {
        var request = new HttpRequestMessage(method, route);
        request.Headers.Add(CorrelationMiddleware.ClientIdHeaderName, ClientId);
        request.Headers.TryAddWithoutValidation(ClientVersionMiddleware.ClientVersionHeaderName, "1.0.0");
        request.Headers.TryAddWithoutValidation(
            ClientVersionMiddleware.ProtocolVersionHeaderName,
            VersionInfo.CurrentProtocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));

        return request;
    }

    private static async Task<JsonElement> ReadArrayAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        return document.RootElement.Clone();
    }

    private static IReadOnlyList<InstallationRow> ReadInstallationRows(string dataDirectory)
    {
        var databasePath = Path.Combine(dataDirectory, "catalog.sqlite");
        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT clientId, gameId, installedVersion, validationStateText FROM CLIENT_INSTALLATION_VIEW";

        using var reader = command.ExecuteReader();
        var rows = new List<InstallationRow>();

        while (reader.Read())
        {
            rows.Add(new InstallationRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3)));
        }

        return rows;
    }

    private static IReadOnlyList<DownloadIntentRow> ReadDownloadIntentRows(string dataDirectory)
    {
        var databasePath = Path.Combine(dataDirectory, "catalog.sqlite");
        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT operationId, clientId, outcome FROM DOWNLOAD_INTENT ORDER BY operationId";

        using var reader = command.ExecuteReader();
        var rows = new List<DownloadIntentRow>();

        while (reader.Read())
        {
            rows.Add(new DownloadIntentRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2)));
        }

        return rows;
    }

    private static IReadOnlyList<ClientErrorRow> ReadClientErrorRows(string dataDirectory)
    {
        var databasePath = Path.Combine(dataDirectory, "catalog.sqlite");
        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT errorId, clientId, operationId, code, message, cause, nextStep, occurredAt
            FROM CLIENT_ERROR
            ORDER BY errorId
            """;

        using var reader = command.ExecuteReader();
        var rows = new List<ClientErrorRow>();

        while (reader.Read())
        {
            rows.Add(new ClientErrorRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7)));
        }

        return rows;
    }

    private static void InsertClientError(        string dataDirectory,
        string errorId,
        string clientId,
        string operationId,
        string code,
        string message,
        string cause,
        string nextStep)
    {
        var databasePath = Path.Combine(dataDirectory, "catalog.sqlite");
        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO CLIENT_ERROR
                (errorId, clientId, operationId, code, message, cause, nextStep, occurredAt)
            VALUES
                ($errorId, $clientId, $operationId, $code, $message, $cause, $nextStep, $occurredAt)
            """;
        command.Parameters.AddWithValue("$errorId", errorId);
        command.Parameters.AddWithValue("$clientId", clientId);
        command.Parameters.AddWithValue("$operationId", operationId);
        command.Parameters.AddWithValue("$code", code);
        command.Parameters.AddWithValue("$message", message);
        command.Parameters.AddWithValue("$cause", cause);
        command.Parameters.AddWithValue("$nextStep", nextStep);
        command.Parameters.AddWithValue(
            "$occurredAt",
            DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));

        command.ExecuteNonQuery();
    }

    private static int GetFreeTcpPort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private sealed record InstallationRow(
        string ClientId,
        string GameId,
        string Version,
        string ValidationStateText);

    private sealed record DownloadIntentRow(
        string OperationId,
        string ClientId,
        string Outcome);

    private sealed record ClientErrorRow(
        string ErrorId,
        string ClientId,
        string OperationId,
        string Code,
        string Message,
        string Cause,
        string NextStep,
        string OccurredAt);
}
