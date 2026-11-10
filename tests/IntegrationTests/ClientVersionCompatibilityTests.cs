using System.Net;
using System.Text.Json;
using LANPartyHub.Server.Host;
using LANPartyHub.Server.Infrastructure.Http;
using LANPartyHub.Shared.Contracts;
using Xunit;

namespace LANPartyHub.IntegrationTests;

/// <summary>
/// Compatibility of the versions of the clients against the real host (AD-6, FR-4).
///
/// <para>
/// The tests start the real host, with the real Kestrel and the real sockets, because the
/// <c>426 Upgrade Required</c> and the shape of the diagnostic of the version are only
/// observable with the server in march: the answer with the version of the server and the
/// range mínimo, and the entry of the log that registers the version del cliente y la
/// incompatibilidad (AD-6, AD-15).
/// </para>
/// </summary>
[Collection(LogCaptureCollection.Name)]
public sealed class ClientVersionCompatibilityTests
{
    private const string ClientId = "0193f2a1b2c34d4e5f60718293a4b5c6";

    /// <summary>
    /// Category of the log of the middleware de la compatibilidad. The entry of the
    /// rejection is observed by this category (AD-15).
    /// </summary>
    private const string VersionMiddlewareCategory =
        "LANPartyHub.Server.Infrastructure.Http.ClientVersionMiddleware";

    [Fact]
    public async Task A_compatible_client_is_served_with_normality()
    {
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        using var response = await client.SendAsync(
            BuildCatalogRequest(VersionInfo.CurrentProtocolVersion),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_route_of_the_server_publishes_the_versions()
    {
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        using var response = await client.SendAsync(
            BuildRequest(ServerInfoEndpoint.Route, VersionInfo.CurrentProtocolVersion),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal(
            VersionInfo.CurrentProtocolVersion,
            document.RootElement.GetProperty("protocolVersion").GetInt32());

        Assert.False(string.IsNullOrWhiteSpace(
            document.RootElement.GetProperty("serverVersion").GetString()));
    }

    [Fact]
    public async Task The_route_of_the_server_publishes_the_range_that_it_supports()
    {
        // AD-6: el rango mínimo se puede leer de las cabeceras, SIN interpretar texto.
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        using var response = await client.SendAsync(
            BuildRequest(ServerInfoEndpoint.Route, VersionInfo.CurrentProtocolVersion),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.False(string.IsNullOrWhiteSpace(
            response.Headers.GetValues(ClientVersionMiddleware.ServerVersionHeaderName).Single()));

        Assert.Equal(
            VersionInfo.CurrentProtocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            response.Headers.GetValues(ClientVersionMiddleware.MinimumProtocolVersionHeaderName).Single());
    }

    [Fact]
    public async Task A_client_with_a_protocol_version_of_the_future_is_rejected_with_upgrade_required()
    {
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        using var response = await client.SendAsync(
            BuildCatalogRequest(VersionInfo.CurrentProtocolVersion + 1),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UpgradeRequired, response.StatusCode);
    }

    [Fact]
    public async Task A_client_with_a_protocol_version_of_the_past_is_rejected_with_upgrade_required()
    {
        // AD-6, AD-30: el mínimo que el servidor declara por omisión es la MISMA versión
        // de protocolo que habla, así que un cliente que declara la versión anterior es
        // demasiado viejo y recibe el mismo rechazo que el cliente demasiado nuevo: la
        // forma nueva del evento de conexión NO le permite eludir la puerta (AD-6).
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        using var response = await client.SendAsync(
            BuildCatalogRequest(VersionInfo.CurrentProtocolVersion - 1),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UpgradeRequired, response.StatusCode);
    }

    [Fact]
    public async Task A_client_with_the_exact_protocol_version_of_the_server_is_served_with_normality()
    {
        // AD-6, AD-30: el cliente que declara EXACTAMENTE la versión que habla el
        // servidor está dentro del rango, así que se sirve con normalidad.
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        using var response = await client.SendAsync(
            BuildCatalogRequest(VersionInfo.CurrentProtocolVersion),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_request_without_the_version_headers_is_rejected_with_upgrade_required()
    {
        // AD-6: TODA petición declara la versión. La que no la declara no puede ser
        // compatible con nadie.
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/catalog");
        request.Headers.Add(CorrelationMiddleware.ClientIdHeaderName, ClientId);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UpgradeRequired, response.StatusCode);
    }

    [Fact]
    public async Task A_request_with_an_unreadable_protocol_version_is_rejected_with_upgrade_required()
    {
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/catalog");
        request.Headers.Add(CorrelationMiddleware.ClientIdHeaderName, ClientId);
        request.Headers.TryAddWithoutValidation(ClientVersionMiddleware.ClientVersionHeaderName, "1.0.0");
        request.Headers.TryAddWithoutValidation(ClientVersionMiddleware.ProtocolVersionHeaderName, "no-es-un-numero");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UpgradeRequired, response.StatusCode);
    }

    [Fact]
    public async Task The_rejection_carries_the_error_of_the_catalog_with_its_three_parts()
    {
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        using var response = await client.SendAsync(
            BuildCatalogRequest(VersionInfo.CurrentProtocolVersion + 1),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UpgradeRequired, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        using var document = JsonDocument.Parse(body);

        Assert.Equal(
            ErrorCatalog.ClientVersionIncompatibleCode,
            document.RootElement.GetProperty("code").GetString());

        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("message").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("cause").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("nextStep").GetString()));
    }

    [Fact]
    public async Task The_rejection_carries_the_version_of_the_server_and_the_minimum_range()
    {
        // AD-6: el cliente lee la versión del servidor y el rango mínimo de las
        // CABECERAS, sin tener que interpretar texto.
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        using var response = await client.SendAsync(
            BuildCatalogRequest(VersionInfo.CurrentProtocolVersion + 1),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UpgradeRequired, response.StatusCode);

        Assert.False(string.IsNullOrWhiteSpace(
            response.Headers.GetValues(ClientVersionMiddleware.ServerVersionHeaderName).Single()));

        Assert.Equal(
            VersionInfo.CurrentProtocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            response.Headers.GetValues(ClientVersionMiddleware.MinimumProtocolVersionHeaderName).Single());
    }

    [Fact]
    public async Task The_route_of_the_catalog_also_responds_with_upgrade_required()
    {
        // La puerta es de TODA la superficie del cliente, no de una ruta: el catálogo
        // responde igual (AD-6).
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        using var response = await client.SendAsync(
            BuildCatalogRequest(VersionInfo.CurrentProtocolVersion + 1),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UpgradeRequired, response.StatusCode);
    }

    [Fact]
    public async Task The_log_registers_the_version_of_the_client_and_the_incompatibility()
    {
        // FR-4, AD-15: el servidor registra la versión del cliente y la incompatibilidad
        // detectada.
        await using var capture = await ConsoleLogCapture.StartAsync();

        await using (var host = await TestServer.StartAsync(TestContext.Current.CancellationToken))
        {
            using var client = host.CreateClient();

            using var response = await client.SendAsync(
                BuildCatalogRequest(VersionInfo.CurrentProtocolVersion + 1),
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.UpgradeRequired, response.StatusCode);
        }

        var entry = Assert.Single(
            capture.Entries,
            candidate => LogEntries.IsProduct(candidate)
                && LogEntries.HasCategory(candidate, VersionMiddlewareCategory)
                && candidate.GetProperty("Message").GetString()!.Contains(
                    "no es compatible",
                    StringComparison.Ordinal));

        var message = entry.GetProperty("Message").GetString()!;

        Assert.Contains("1.0.0", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_rejection_is_counted_in_the_metric_of_errors_by_code()
    {
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        using var response = await client.SendAsync(
            BuildCatalogRequest(VersionInfo.CurrentProtocolVersion + 1),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UpgradeRequired, response.StatusCode);

        using var document = JsonDocument.Parse(
            await client.GetStringAsync(MetricsEndpoint.Route, TestContext.Current.CancellationToken));

        var errors = document.RootElement.GetProperty("errorsByCode");

        Assert.Equal(
            1,
            errors.GetProperty(ErrorCatalog.ClientVersionIncompatibleCode).GetInt64());
    }

    [Fact]
    public async Task The_administration_surface_is_not_gated_by_the_version()
    {
        // AD-30: el organizador NO es un cliente de la LAN y su binario no declara una
        // versión de protocolo, así que la superficie de la administración no pasa por la
        // puerta de la versión.
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, GamesEndpoint.Route);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new
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
            }),
            System.Text.Encoding.UTF8,
            "application/json");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task The_metrics_of_the_host_are_not_gated_by_the_version()
    {
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        using var response = await client.GetAsync(MetricsEndpoint.Route, TestContext.Current.CancellationToken);

        Assert.True(response.IsSuccessStatusCode);
    }

    /// <summary>
    /// Builds a request of the surface of the client that declares una versión de
    /// protocolo.
    /// </summary>
    /// <param name="route">Route of the request.</param>
    /// <param name="protocolVersion">Protocol version that the request declares.</param>
    /// <returns>The request.</returns>
    private static HttpRequestMessage BuildRequest(string route, int protocolVersion)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, route);

        request.Headers.Add(CorrelationMiddleware.ClientIdHeaderName, ClientId);
        request.Headers.TryAddWithoutValidation(ClientVersionMiddleware.ClientVersionHeaderName, "1.0.0");
        request.Headers.TryAddWithoutValidation(
            ClientVersionMiddleware.ProtocolVersionHeaderName,
            protocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));

        return request;
    }

    /// <summary>
    /// Builds a request of the surface del catálogo que declara una versión de protocolo.
    /// </summary>
    /// <param name="protocolVersion">Protocol version that the request declares.</param>
    /// <returns>The request.</returns>
    private static HttpRequestMessage BuildCatalogRequest(int protocolVersion) =>
        BuildRequest("/api/v1/catalog", protocolVersion);
}
