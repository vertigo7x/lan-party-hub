using System.Net;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Infrastructure.Networking;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// Headers of correlation of the requests of the client (AD-15, AD-23). Every request that
/// the client sends carries the identifier of the team and the identifier of the unit of
/// work in progress.
/// </summary>
public sealed class CorrelationHeadersHandlerTests
{
    private const string ClientId = "0193f2a1b2c34d4e5f60718293a4b5c6";

    private const string OperationId = "9f2c1d3e4f5a6b7c8d9e0f1a2b3c4d5e";

    [Fact]
    public async Task Every_request_carries_the_identifier_of_the_client_and_the_operation()
    {
        var captured = new CapturingHandler();

        using var client = new HttpClient(new CorrelationHeadersHandler(new StubHeaders(ClientId, OperationId))
        {
            InnerHandler = captured,
        });

        using var response = await client.GetAsync("http://servidor/api/v1/catalog", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(captured.Request);

        Assert.Equal(
            ClientId,
            captured.Request!.Headers.GetValues(CorrelationHeadersHandler.ClientIdHeaderName).Single());

        Assert.Equal(
            OperationId,
            captured.Request.Headers.GetValues(CorrelationHeadersHandler.OperationIdHeaderName).Single());
    }

    [Fact]
    public async Task A_request_without_identifiers_is_sent_without_them()
    {
        var captured = new CapturingHandler();

        using var client = new HttpClient(new CorrelationHeadersHandler(new StubHeaders(null, null))
        {
            InnerHandler = captured,
        });

        using var response = await client.GetAsync("http://servidor/api/v1/catalog", TestContext.Current.CancellationToken);

        Assert.NotNull(captured.Request);
        Assert.False(captured.Request!.Headers.Contains(CorrelationHeadersHandler.ClientIdHeaderName));
        Assert.False(captured.Request.Headers.Contains(CorrelationHeadersHandler.OperationIdHeaderName));
    }

    [Fact]
    public async Task The_headers_of_a_request_are_replaced_and_not_duplicated()
    {
        var captured = new CapturingHandler();

        using var client = new HttpClient(new CorrelationHeadersHandler(new StubHeaders(ClientId, OperationId))
        {
            InnerHandler = captured,
        });

        using var request = new HttpRequestMessage(HttpMethod.Get, "http://servidor/api/v1/catalog");
        request.Headers.TryAddWithoutValidation(CorrelationHeadersHandler.ClientIdHeaderName, "otro-valor");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.NotNull(captured.Request);
        Assert.Single(captured.Request!.Headers.GetValues(CorrelationHeadersHandler.ClientIdHeaderName));
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
