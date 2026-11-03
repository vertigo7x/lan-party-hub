using System.Net;
using System.Text.Json;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Host;
using LANPartyHub.Client.Infrastructure;
using LANPartyHub.Client.Infrastructure.Networking;
using LANPartyHub.Server.Host;
using LANPartyHub.Server.Infrastructure.Http;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LANPartyHub.IntegrationTests;

/// <summary>
/// The probe of the versions of the server against the real host (AD-6, AD-15, AD-23).
///
/// <para>
/// The tests start the real host, with the real Kestrel and the real sockets, and they
/// drive the REAL adapter of the probe of the client, because the route that publishes
/// the versions belongs to the surface of the client: the server REJECTS with 400 the
/// request that does not carry the canonical identifier of the team (AD-23), and a probe
/// that travels without it can NEVER hear the verdict of the server.
/// </para>
/// </summary>
[Collection(RealHostCollection.Name)]
public sealed class ServerVersionProbeTests
{
    /// <summary>
    /// Canonical identifier of a team: 32 lowercase hexadecimal digits (AD-23, AD-31).
    /// </summary>
    private const string ClientId = "0193f2a1b2c34d4e5f60718293a4b5c6";

    [Fact]
    public async Task The_route_of_the_versions_rejects_the_request_without_the_client_identifier()
    {
        // AD-23: la ruta de las versiones es de la superficie del cliente, así que la
        // correlación la cubre y rechaza con 400 la petición que no lleva el
        // identificador canónico del equipo. Este es el fallo que hacía que el sondeo
        // real NUNCA pudiera oír al servidor.
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, ServerInfoEndpoint.Route);
        request.Headers.TryAddWithoutValidation(ClientVersionMiddleware.ClientVersionHeaderName, "1.0.0");
        request.Headers.TryAddWithoutValidation(
            ClientVersionMiddleware.ProtocolVersionHeaderName,
            VersionInfo.CurrentProtocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal(
            ErrorCatalog.ClientIdMissingCode,
            document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task The_route_of_the_versions_answers_with_the_versions_to_a_client_with_the_identifier()
    {
        // La contracara del caso anterior: con el identificador canónico, la misma ruta
        // responde 200 y publica el contrato VersionInfo (AD-27).
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, ServerInfoEndpoint.Route);
        request.Headers.Add(CorrelationMiddleware.ClientIdHeaderName, ClientId);
        request.Headers.TryAddWithoutValidation(ClientVersionMiddleware.ClientVersionHeaderName, "1.0.0");
        request.Headers.TryAddWithoutValidation(
            ClientVersionMiddleware.ProtocolVersionHeaderName,
            VersionInfo.CurrentProtocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

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
    public async Task The_real_probe_gets_the_verdict_of_the_server_and_the_version_that_it_publishes()
    {
        // El sondeo REAL contra el host REAL: la sonda estampa el identificador del
        // equipo, el servidor responde 200 y la sonda lee la versión del servidor. Sin
        // el identificador, la sonda SIEMPRE recibiría un 400 y volvería con la versión
        // a null (AD-6, AD-15, AD-23).
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        using var httpClient = BuildProbeHttpClient(new StaticClientRequestHeaders(ClientId));

        var probe = new HttpServerVersionProbe(
            httpClient,
            NullLogger<HttpServerVersionProbe>.Instance);

        var result = await probe.ProbeAsync(
            new ServerEndpoint(host.BoundAddress.ToString(), host.Port),
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken);

        Assert.True(result.Compatible);
        Assert.NotNull(result.ServerVersion);
        Assert.False(string.IsNullOrWhiteSpace(result.ServerVersion));
        Assert.Equal(VersionInfo.CurrentProtocolVersion, result.MinimumProtocolVersion);
    }

    [Fact]
    public async Task The_real_probe_without_the_client_identifier_cannot_hear_the_server()
    {
        // La sombra del caso anterior: la misma sonda sin identificador recibe el 400 del
        // servidor y la versión del servidor queda a null. Es la prueba que fija el
        // fallo que existía antes del arreglo.
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        using var httpClient = BuildProbeHttpClient(new StaticClientRequestHeaders(clientId: null));

        var probe = new HttpServerVersionProbe(
            httpClient,
            NullLogger<HttpServerVersionProbe>.Instance);

        var result = await probe.ProbeAsync(
            new ServerEndpoint(host.BoundAddress.ToString(), host.Port),
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken);

        // El sondeo NO afirma la incompatibilidad: el servidor no se ha podido
        // pronunciarse (AC3, UX-DR-36).
        Assert.True(result.Compatible);
        Assert.Null(result.ServerVersion);
    }

    [Fact]
    public async Task The_composition_of_the_connection_probes_the_versions_with_the_identifier_of_the_team()
    {
        // La composición es la que abre el sondeo en el cliente real: con el scope de
        // correlación ya abierto con el identificador canónico, la sonda estampa un
        // identificador REAL y el servidor NO rechaza la petición (AD-15, AD-23).
        //
        // La observación es la del SERVIDOR: el contador de errores por código del host
        // cuenta los rechazos de la correlación, así que un sondeo que viaja sin el
        // identificador se ve aquí como un rechazo CLIENT_ID_MISSING. Esa es la única
        // diferencia observable entre un sondeo que oye al servidor y uno que no.
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        using var loggerFactory = NullLoggerFactory.Instance;
        using var scope = new ClientCorrelationFactory(loggerFactory);

        scope.Open(ClientId, Guid.CreateVersion7().ToString("N"));

        using var composition = new ConnectionComposition(scope, loggerFactory);

        var result = await composition.ConnectionService.ConnectAsync(
            $"{host.BoundAddress}:{host.Port}",
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken);

        // La conexión se abrió y el sondeo NO recibió un rechazo del servidor.
        Assert.True(result.IsConnected, result.Error?.Message ?? string.Empty);
        Assert.Null(result.Error);

        using var client = host.CreateClient();
        using var document = JsonDocument.Parse(
            await client.GetStringAsync(MetricsEndpoint.Route, TestContext.Current.CancellationToken));

        var errors = document.RootElement.GetProperty("errorsByCode");

        Assert.Equal(
            0,
            errors.TryGetProperty(ErrorCatalog.ClientIdMissingCode, out var missing)
                ? missing.GetInt64()
                : 0);

        Assert.Equal(
            0,
            errors.TryGetProperty(ErrorCatalog.ClientIdInvalidCode, out var invalid)
                ? invalid.GetInt64()
                : 0);
    }

    /// <summary>
    /// Builds the client of HTTP of the probe, with the same handler that the
    /// composition installs (AD-6, AD-15).
    /// </summary>
    /// <param name="headers">Source of the identifiers of correlation of the request.</param>
    /// <returns>The client of HTTP.</returns>
    private static HttpClient BuildProbeHttpClient(IClientRequestHeaders headers) =>
        new(
            new ClientRequestHeadersHandler(headers, new ClientBuildVersion())
            {
                InnerHandler = new HttpClientHandler(),
            });

    /// <summary>
    /// Identifiers of correlation with a fixed value, so the probe of the test declares
    /// exactly lo que the test wants (AD-23).
    /// </summary>
    private sealed class StaticClientRequestHeaders : IClientRequestHeaders
    {
        public StaticClientRequestHeaders(string? clientId)
        {
            ClientId = clientId;
        }

        public string? ClientId { get; }

        public string? OperationId => null;

        public string? MachineName => null;
    }
}
