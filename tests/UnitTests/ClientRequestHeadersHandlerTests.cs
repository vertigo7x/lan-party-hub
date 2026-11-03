using System.Net;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Infrastructure.Networking;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// Headers of every request to the surface of the client of the event server
/// (AD-6, AD-15, AD-23).
///
/// <para>
/// Cada request que el cliente envía lleva el identificador del equipo y el identificador
/// de la unidad de trabajo en curso, y ALSO la versión del cliente y la versión del
/// protocolo: el servidor rechaza con 426 la petición que no los declara (AD-6).
/// </para>
/// </summary>
public sealed class ClientRequestHeadersHandlerTests
{
    private const string ClientId = "0193f2a1b2c34d4e5f60718293a4b5c6";

    private const string OperationId = "9f2c1d3e4f5a6b7c8d9e0f1a2b3c4d5e";

    private const string ClientVersion = "1.0.0";

    private const int ProtocolVersion = 1;

    [Fact]
    public async Task Every_request_carries_the_identifier_of_the_client_and_the_operation()
    {
        var captured = new CapturingHandler();

        using var client = new HttpClient(new ClientRequestHeadersHandler(
            new StubHeaders(ClientId, OperationId),
            new StubVersions(ClientVersion, ProtocolVersion))
        {
            InnerHandler = captured,
        });

        using var response = await client.GetAsync("http://servidor/api/v1/catalog", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(captured.Request);

        Assert.Equal(
            ClientId,
            captured.Request!.Headers.GetValues(ClientRequestHeadersHandler.ClientIdHeaderName).Single());

        Assert.Equal(
            OperationId,
            captured.Request.Headers.GetValues(ClientRequestHeadersHandler.OperationIdHeaderName).Single());
    }

    [Fact]
    public async Task Every_request_declares_the_version_of_the_client()
    {
        // AD-6: TODA petición declara la versión del cliente, sin excepción: el servidor
        // rechaza con 426 la que no la lleva.
        var captured = new CapturingHandler();

        using var client = new HttpClient(new ClientRequestHeadersHandler(
            new StubHeaders(ClientId, OperationId),
            new StubVersions(ClientVersion, ProtocolVersion))
        {
            InnerHandler = captured,
        });

        using var response = await client.GetAsync("http://servidor/api/v1/catalog", TestContext.Current.CancellationToken);

        Assert.NotNull(captured.Request);

        Assert.Equal(
            ClientVersion,
            captured.Request!.Headers.GetValues(ClientRequestHeadersHandler.ClientVersionHeaderName).Single());
    }

    [Fact]
    public async Task Every_request_declares_the_version_of_the_protocol()
    {
        var captured = new CapturingHandler();

        using var client = new HttpClient(new ClientRequestHeadersHandler(
            new StubHeaders(ClientId, OperationId),
            new StubVersions(ClientVersion, ProtocolVersion))
        {
            InnerHandler = captured,
        });

        using var response = await client.GetAsync("http://servidor/api/v1/catalog", TestContext.Current.CancellationToken);

        Assert.NotNull(captured.Request);

        Assert.Equal(
            ProtocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            captured.Request!.Headers.GetValues(ClientRequestHeadersHandler.ProtocolVersionHeaderName).Single());
    }

    [Fact]
    public async Task A_request_without_identifiers_is_sent_without_them()
    {
        var captured = new CapturingHandler();

        using var client = new HttpClient(new ClientRequestHeadersHandler(
            new StubHeaders(null, null),
            new StubVersions(ClientVersion, ProtocolVersion))
        {
            InnerHandler = captured,
        });

        using var response = await client.GetAsync("http://servidor/api/v1/catalog", TestContext.Current.CancellationToken);

        Assert.NotNull(captured.Request);
        Assert.False(captured.Request!.Headers.Contains(ClientRequestHeadersHandler.ClientIdHeaderName));
        Assert.False(captured.Request.Headers.Contains(ClientRequestHeadersHandler.OperationIdHeaderName));
    }

    [Fact]
    public async Task A_request_always_declares_the_versions_even_without_identifiers()
    {
        // La versión NO depende del identificador: es la versión del binario y la del
        // protocolo, y el servidor las necesita en TODA petición (AD-6).
        var captured = new CapturingHandler();

        using var client = new HttpClient(new ClientRequestHeadersHandler(
            new StubHeaders(null, null),
            new StubVersions(ClientVersion, ProtocolVersion))
        {
            InnerHandler = captured,
        });

        using var response = await client.GetAsync("http://servidor/api/v1/catalog", TestContext.Current.CancellationToken);

        Assert.NotNull(captured.Request);
        Assert.True(captured.Request!.Headers.Contains(ClientRequestHeadersHandler.ClientVersionHeaderName));
        Assert.True(captured.Request.Headers.Contains(ClientRequestHeadersHandler.ProtocolVersionHeaderName));
    }

    [Fact]
    public async Task The_headers_of_a_request_are_replaced_and_not_duplicated()
    {
        var captured = new CapturingHandler();

        using var client = new HttpClient(new ClientRequestHeadersHandler(
            new StubHeaders(ClientId, OperationId),
            new StubVersions(ClientVersion, ProtocolVersion))
        {
            InnerHandler = captured,
        });

        using var request = new HttpRequestMessage(HttpMethod.Get, "http://servidor/api/v1/catalog");
        request.Headers.TryAddWithoutValidation(ClientRequestHeadersHandler.ClientIdHeaderName, "otro-valor");
        request.Headers.TryAddWithoutValidation(ClientRequestHeadersHandler.ClientVersionHeaderName, "0.0.1");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.NotNull(captured.Request);
        Assert.Single(captured.Request!.Headers.GetValues(ClientRequestHeadersHandler.ClientIdHeaderName));
        Assert.Single(captured.Request.Headers.GetValues(ClientRequestHeadersHandler.ClientVersionHeaderName));

        Assert.Equal(
            ClientVersion,
            captured.Request.Headers.GetValues(ClientRequestHeadersHandler.ClientVersionHeaderName).Single());
    }

    /// <summary>
    /// Source of the identifiers that the requests carry.
    /// </summary>
    private sealed class StubHeaders : IClientRequestHeaders
    {
        public StubHeaders(string? clientId, string? operationId)
        {
            ClientId = clientId;
            OperationId = operationId;
        }

        public string? ClientId { get; }

        public string? OperationId { get; }
    }

    /// <summary>
    /// Source of the versions that the requests declare.
    /// </summary>
    private sealed class StubVersions : IClientVersionProvider
    {
        public StubVersions(string clientVersion, int protocolVersion)
        {
            ClientVersion = clientVersion;
            ProtocolVersion = protocolVersion;
        }

        public string ClientVersion { get; }

        public int ProtocolVersion { get; }
    }

    /// <summary>
    /// Handler that keeps the request instead of sending it.
    /// </summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
