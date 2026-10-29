using System.Net;
using System.Text.Json;
using LANPartyHub.Server.Host;
using LANPartyHub.Server.Infrastructure.Http;
using LANPartyHub.Shared.Contracts;
using Xunit;

namespace LANPartyHub.IntegrationTests;

/// <summary>
/// Correlation of the requests of the client against the real host (AD-15, AD-23).
///
/// <para>
/// The tests start the real host, with the real Kestrel and the real sockets, because the
/// correlation is only observable with the server in march: the scope of the log, the
/// rejection of the request that does not carry the identifier of the client and the
/// metrics that the host publishes locally.
/// </para>
/// </summary>
[Collection(LogCaptureCollection.Name)]
public sealed class ServerCorrelationTests
{
    private const string ClientId = "0193f2a1b2c34d4e5f60718293a4b5c6";

    private const string OperationId = "9f2c1d3e4f5a6b7c8d9e0f1a2b3c4d5e";

    private const string UnknownClientId = "0000000000000000000000000000ffff";

    /// <summary>
    /// Category of the log of the use case that registers a videogame. The entry of the
    /// registration is observed by this category and not by the one of the middleware,
    /// because both live in the same scope of correlation (AD-15).
    /// </summary>
    private const string RegisterGameCategory = "LANPartyHub.Server.Application.Catalog.RegisterGame";

    [Fact]
    public async Task A_request_with_the_two_headers_is_correlated_and_its_log_entry_carries_them()
    {
        await using var capture = await ConsoleLogCapture.StartAsync();

        await using (var host = await TestServer.StartAsync(TestContext.Current.CancellationToken))
        {
            using var client = host.CreateClient();

            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/catalog");
            request.Headers.Add(CorrelationMiddleware.ClientIdHeaderName, ClientId);
            request.Headers.Add(CorrelationMiddleware.OperationIdHeaderName, OperationId);

            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

            // The request is not rejected: the identifier of the client is there.
            Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);

            // The effective identifier of the operation travels back to the client.
            Assert.Equal(
                OperationId,
                response.Headers.GetValues(CorrelationMiddleware.OperationIdHeaderName).Single());
        }

        var entry = Assert.Single(
            capture.Entries,
            candidate => LogEntries.IsProduct(candidate) && LogEntries.HasScope(candidate, OperationId, ClientId));

        var scope = LogEntries.CorrelationScope(entry, ClientId);

        Assert.NotNull(scope);
        Assert.Equal(OperationId, scope!.Value.GetProperty("OperationId").GetString());
        Assert.Equal(ClientId, scope.Value.GetProperty("ClientId").GetString());
    }

    [Fact]
    public async Task A_request_without_the_client_identifier_is_rejected_with_the_error_of_the_catalog()
    {
        await using var capture = await ConsoleLogCapture.StartAsync();

        string body;

        await using (var host = await TestServer.StartAsync(TestContext.Current.CancellationToken))
        {
            using var client = host.CreateClient();

            using var response = await client.GetAsync("/api/v1/catalog", TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        }

        AssertRejected(body, ErrorCatalog.ClientIdMissingCode);

        // The rejection leaves a trace in the log with the code of the catalog.
        var rejection = Assert.Single(
            capture.Entries,
            candidate => HasStateField(candidate, "code", ErrorCatalog.ClientIdMissingCode));

        Assert.Equal(
            ErrorCatalog.ClientIdMissingCode,
            rejection.GetProperty("State").GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_request_with_an_empty_client_identifier_is_rejected()
    {
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/catalog");
        request.Headers.TryAddWithoutValidation(CorrelationMiddleware.ClientIdHeaderName, string.Empty);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        AssertRejected(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            ErrorCatalog.ClientIdMissingCode);
    }

    [Fact]
    public async Task A_request_with_the_client_identifier_repeated_is_rejected()
    {
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/catalog");
        request.Headers.TryAddWithoutValidation(CorrelationMiddleware.ClientIdHeaderName, ClientId);
        request.Headers.TryAddWithoutValidation(CorrelationMiddleware.ClientIdHeaderName, UnknownClientId);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // The repeated header arrives as a value that is not the canonical form of an
        // identifier, so the rejection is the one of an invalid identifier.
        AssertRejected(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            ErrorCatalog.ClientIdInvalidCode);
    }

    [Fact]
    public async Task A_request_with_a_client_identifier_that_is_not_canonical_is_rejected()
    {
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/catalog");
        request.Headers.TryAddWithoutValidation(CorrelationMiddleware.ClientIdHeaderName, "no-es-un-identificador");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        AssertRejected(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            ErrorCatalog.ClientIdInvalidCode);
    }

    [Fact]
    public async Task The_rejection_carries_the_identifier_of_the_unit_of_work()
    {
        await using var capture = await ConsoleLogCapture.StartAsync();

        await using (var host = await TestServer.StartAsync(TestContext.Current.CancellationToken))
        {
            using var client = host.CreateClient();

            using var response = await client.GetAsync("/api/v1/catalog", TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            // The identifier of the unit of work travels back to the client, so the
            // rejection can be joined with the log of the client.
            Assert.False(string.IsNullOrWhiteSpace(
                response.Headers.GetValues(CorrelationMiddleware.OperationIdHeaderName).Single()));
        }

        // The entry of the rejection is written inside the scope, so it carries the
        // identifier of the unit of work.
        var rejection = Assert.Single(
            capture.Entries,
            candidate => HasStateField(candidate, "code", ErrorCatalog.ClientIdMissingCode));

        var scopes = LogEntries.Scopes(rejection).ToArray();

        Assert.NotEmpty(scopes);

        // One of the scopes is the one of the correlation, and it carries the identifier
        // of the unit of work.
        Assert.Contains(
            scopes,
            scope => scope.TryGetProperty("OperationId", out var operationId)
                && !string.IsNullOrWhiteSpace(operationId.GetString()));
    }

    [Fact]
    public async Task A_request_without_the_operation_identifier_gets_one_from_the_server()
    {
        await using var capture = await ConsoleLogCapture.StartAsync();

        await using (var host = await TestServer.StartAsync(TestContext.Current.CancellationToken))
        {
            using var client = host.CreateClient();

            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/catalog");
            request.Headers.Add(CorrelationMiddleware.ClientIdHeaderName, ClientId);

            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);

            // The identifier that the server generated travels back to the client.
            Assert.False(string.IsNullOrWhiteSpace(
                response.Headers.GetValues(CorrelationMiddleware.OperationIdHeaderName).Single()));
        }

        var entry = Assert.Single(
            capture.Entries,
            candidate => LogEntries.IsProduct(candidate) && LogEntries.HasScope(candidate, null, ClientId));

        var scope = LogEntries.CorrelationScope(entry, ClientId);

        Assert.NotNull(scope);
        Assert.False(string.IsNullOrWhiteSpace(scope!.Value.GetProperty("OperationId").GetString()));
    }

    [Fact]
    public async Task A_request_with_an_operation_identifier_that_is_not_canonical_gets_a_new_one()
    {
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/catalog");
        request.Headers.Add(CorrelationMiddleware.ClientIdHeaderName, ClientId);
        request.Headers.TryAddWithoutValidation(CorrelationMiddleware.OperationIdHeaderName, "no-es-un-identificador");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);

        // The identifier that arrived is ignored and another one is generated.
        var operationId = response.Headers.GetValues(CorrelationMiddleware.OperationIdHeaderName).Single();

        Assert.NotEqual("no-es-un-identificador", operationId);
        Assert.Equal(CorrelationMiddleware.CanonicalIdentifierLength, operationId.Length);
    }

    [Fact]
    public async Task An_unknown_client_identifier_is_accepted_and_correlated()
    {
        await using var capture = await ConsoleLogCapture.StartAsync();

        await using (var host = await TestServer.StartAsync(TestContext.Current.CancellationToken))
        {
            using var client = host.CreateClient();

            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/catalog");
            request.Headers.Add(CorrelationMiddleware.ClientIdHeaderName, UnknownClientId);

            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

            // An unknown identifier is a new client, and it is not rejected (AD-23).
            Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
        }

        Assert.Contains(
            capture.Entries,
            candidate => LogEntries.IsProduct(candidate) && LogEntries.HasScope(candidate, null, UnknownClientId));
    }

    [Fact]
    public async Task The_metrics_of_the_host_answer_without_the_headers_of_the_client()
    {
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        using var response = await client.GetAsync(MetricsEndpoint.Route, TestContext.Current.CancellationToken);
        var payload = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.True(response.IsSuccessStatusCode, payload);
        Assert.Contains("transferRateBytesPerSecond", payload);
        Assert.Contains("activeOperations", payload);
        Assert.Contains("errorsByCode", payload);
    }

    [Fact]
    public async Task The_rejection_counts_the_error_by_code_and_the_bytes_that_the_host_writes()
    {
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        using var response = await client.GetAsync("/api/v1/catalog", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var document = JsonDocument.Parse(
            await client.GetStringAsync(MetricsEndpoint.Route, TestContext.Current.CancellationToken));

        var errors = document.RootElement.GetProperty("errorsByCode");

        Assert.Equal(1, errors.GetProperty(ErrorCatalog.ClientIdMissingCode).GetInt64());

        // The bytes of the rejection leave zero: the transfer rate is instrumented.
        Assert.True(
            document.RootElement.GetProperty("transferRateBytesPerSecond").GetInt64() > 0,
            "The transfer rate of the host is still zero after a response has been served.");
    }

    [Fact]
    public async Task The_number_of_active_operations_goes_back_to_zero_when_the_request_ends()
    {
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/catalog");
        request.Headers.Add(CorrelationMiddleware.ClientIdHeaderName, ClientId);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        using var document = JsonDocument.Parse(
            await client.GetStringAsync(MetricsEndpoint.Route, TestContext.Current.CancellationToken));

        Assert.Equal(0, document.RootElement.GetProperty("activeOperations").GetInt32());
    }

    [Fact]
    public async Task The_server_opens_no_outbound_tcp_connection()
    {
        var connectionsBefore = ProcessTcpConnections.Get(Environment.ProcessId)
            .Select(Describe)
            .ToHashSet(StringComparer.Ordinal);

        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        var newConnections = ProcessTcpConnections.GetConnected(Environment.ProcessId)
            .Select(Describe)
            .Where(connection => !connectionsBefore.Contains(connection))
            .ToArray();

        // The server only opens listeners: it never reaches out to any service (NFR-10).
        Assert.Empty(newConnections);
    }

    [Fact]
    public async Task The_administration_surface_does_not_ask_for_the_identifier_of_the_client()
    {
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        // El organizador no es un cliente de la LAN, así que la superficie de la
        // administración no exige la cabecera X-Client-Id (AD-30).
        using var request = new HttpRequestMessage(HttpMethod.Post, GamesEndpoint.Route);
        request.Content = BuildValidGameBody();

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        // El alta de un videogame responde 201, y no un estado que solo diga que la
        // petición no fue rechazada por falta de identificador de cliente.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task The_administration_surface_is_still_correlated_by_the_identifier_of_the_operation()
    {
        await using var capture = await ConsoleLogCapture.StartAsync();

        await using (var host = await TestServer.StartAsync(TestContext.Current.CancellationToken))
        {
            using var client = host.CreateClient();

            using var request = new HttpRequestMessage(HttpMethod.Post, GamesEndpoint.Route);
            request.Content = BuildValidGameBody();
            request.Headers.Add(AdminCorrelationMiddleware.OperationIdHeaderName, OperationId);

            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            // La operación sigue correlacionada por su identificador, que es lo que AD-15
            // exige para toda operación.
            Assert.Equal(
                OperationId,
                response.Headers.GetValues(AdminCorrelationMiddleware.OperationIdHeaderName).Single());
        }

        // La entrada del log de la operación lleva el identificador de la unidad de
        // trabajo, y no el del cliente, porque la administración no lo tiene (AD-30).
        // La entrada que se observa es la del caso de uso y no la del middleware: las dos
        // están dentro del mismo scope de correlación, y observar cualquiera de las dos
        // dejaría la entrada del caso de uso sin comprobar.
        var entry = Assert.Single(
            capture.Entries,
            candidate => LogEntries.HasCategory(candidate, RegisterGameCategory)
                && LogEntries.Scopes(candidate).Any(scope =>
                    scope.TryGetProperty("OperationId", out var operationId)
                    && operationId.GetString() == OperationId));

        var scopes = LogEntries.Scopes(entry).ToArray();

        Assert.Contains(
            scopes,
            scope => scope.TryGetProperty("OperationId", out var operationId)
                && operationId.GetString() == OperationId);
    }

    [Fact]
    public async Task The_administration_surface_gets_an_identifier_of_the_operation_when_the_request_does_not_carry_one()
    {
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, GamesEndpoint.Route);
        request.Content = BuildValidGameBody();

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var operationId = response.Headers.GetValues(AdminCorrelationMiddleware.OperationIdHeaderName).Single();

        Assert.Equal(AdminCorrelationMiddleware.CanonicalIdentifierLength, operationId.Length);
        Assert.NotEqual(string.Empty, operationId);
    }

    /// <summary>
    /// Builds a body of the administration of the catalog with the eighteen metadata de
    /// FR-1 (AD-34).
    /// </summary>
    /// <returns>The content of the request.</returns>
    private static StringContent BuildValidGameBody()
    {
        var body = new
        {
            schemaVersion = GamesEndpoint.SupportedSchemaVersion,
            game = new
            {
                gameId = string.Empty,
                name = "Counter-Strike 2",
                description = "Disparadores en primera persona.",
                categories = new[] { "Shooter" },
                tags = new[] { "fps" },
                coverImage = "cover.png",
                images = new[] { "captura-1.png" },
                version = "1.41.3",
                sizeBytes = 7_600_000_000,
                minPlayers = 1,
                maxPlayers = 10,
                operatingSystem = "Windows",
                distributionType = "Servidor",
                installationType = "Portable",
                launchConfiguration = new
                {
                    executablePath = "bin\\cs2.exe",
                    arguments = new[] { "-novid" },
                    workingDirectory = "game\\bin",
                },
                dependencies = new[] { "DirectX 12" },
                license = "Propietaria",
                distributionRestrictions = "Solo para el evento",
            },
        };

        return new StringContent(
            JsonSerializer.Serialize(body),
            System.Text.Encoding.UTF8,
            "application/json");
    }

    /// <summary>
    /// Asserts that the body of a rejection is the single shape of error of the system,
    /// taken from the catalog.
    /// </summary>
    private static void AssertRejected(string body, string expectedCode)
    {
        using var document = JsonDocument.Parse(body);

        Assert.Equal(expectedCode, document.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("message").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("cause").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("nextStep").GetString()));
    }

    private static string Describe(ProcessTcpConnection connection) =>
        $"{connection.LocalEndPoint}|{connection.RemoteEndPoint}|{connection.State}";

    /// <summary>
    /// Tells whether the entry carries the given field with the given value in its state.
    /// </summary>
    private static bool HasStateField(JsonElement entry, string field, string value)
    {
        foreach (var candidate in LogEntries.StateFields(entry))
        {
            if (candidate.Name == field && candidate.Value.GetString() == value)
            {
                return true;
            }
        }

        return false;
    }
}
