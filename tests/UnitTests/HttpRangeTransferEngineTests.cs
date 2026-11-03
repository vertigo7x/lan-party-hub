using System.Net;
using System.Net.Http.Headers;
using LANPartyHub.Client.Infrastructure.Networking;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// Behaviour of the only implementation of the port of the transfer of bytes
/// (AD-4, AD-20, AD-24).
///
/// <para>
/// The engine is exercised with a simulated <see cref="HttpMessageHandler"/>, so the checks
/// observe the request that the engine builds and the answer that it interprets, without
/// opening a socket.
/// </para>
/// </summary>
public sealed class HttpRangeTransferEngineTests
{
    [Fact]
    public async Task The_engine_asks_for_the_range_with_the_range_header()
    {
        var handler = new RecordingHandler(
            _ => new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(new byte[4]),
            });

        var engine = new HttpRangeTransferEngine(new HttpClient(handler));

        using var destination = new MemoryStream();

        await engine.TransferAsync(
            new Uri("http://192.168.1.10:5000/api/v1/packages/pkg-1/content"),
            startInclusive: 100,
            endInclusive: 199,
            destination,
            TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests);

        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(
            "100-199",
            Assert.Single(request.Headers.Range!.Ranges).ToString());

        // El encabezado que sale hacia el servidor es el `Range` con su unidad, que es
        // lo que el servidor del evento lee (AD-20).
        Assert.Equal(
            "bytes=100-199",
            request.Headers.GetValues(HttpRangeTransferEngine.RangeHeaderName).Single());

        // AD-20: el motor no conoce ninguna ruta: la URL la trae el llamante.
        Assert.Equal(
            "/api/v1/packages/pkg-1/content",
            request.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task The_engine_reads_the_bytes_of_a_partial_answer()
    {
        var handler = new RecordingHandler(
            _ =>
            {
                var content = new ByteArrayContent([10, 20, 30, 40]);
                content.Headers.ContentRange = new ContentRangeHeaderValue(100, 103, 1000);

                return new HttpResponseMessage(HttpStatusCode.PartialContent)
                {
                    Content = content,
                };
            });

        var engine = new HttpRangeTransferEngine(new HttpClient(handler));

        using var destination = new MemoryStream();

        var outcome = await engine.TransferAsync(
            new Uri("http://192.168.1.10:5000/api/v1/packages/pkg-1/content"),
            startInclusive: 100,
            endInclusive: 103,
            destination,
            TestContext.Current.CancellationToken);

        Assert.Equal([10, 20, 30, 40], destination.ToArray());
        Assert.Equal(4, outcome.BytesTransferred);
        Assert.True(outcome.RangeHonoured);

        // El total del contenido sale del `Content-Range` de la respuesta parcial.
        Assert.Equal(1000, outcome.TotalBytes);
    }

    [Fact]
    public async Task The_engine_reports_an_answer_that_does_not_honour_the_range()
    {
        // El servidor ignora el rango y sirve el contenido entero: los bytes empiezan por
        // el principio, así que el offset acumulado de la reanudación no aplica.
        var handler = new RecordingHandler(
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1, 2, 3, 4, 5]),
            });

        var engine = new HttpRangeTransferEngine(new HttpClient(handler));

        using var destination = new MemoryStream();

        var outcome = await engine.TransferAsync(
            new Uri("http://192.168.1.10:5000/api/v1/packages/pkg-1/content"),
            startInclusive: 100,
            endInclusive: 104,
            destination,
            TestContext.Current.CancellationToken);

        Assert.Equal([1, 2, 3, 4, 5], destination.ToArray());
        Assert.Equal(5, outcome.BytesTransferred);
        Assert.False(outcome.RangeHonoured);
    }

    [Fact]
    public async Task The_engine_reports_a_range_that_the_server_cannot_satisfy()
    {
        // AD-20: el 416 dice que el rango no es satisfacible. Es una falla de la
        // transferencia, y el 5.2 decide qué significa para la operación.
        var handler = new RecordingHandler(
            _ => new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable));

        var engine = new HttpRangeTransferEngine(new HttpClient(handler));

        using var destination = new MemoryStream();

        var exception = await Assert.ThrowsAsync<TransferRangeNotSatisfiableException>(
            () => engine.TransferAsync(
                new Uri("http://192.168.1.10:5000/api/v1/packages/pkg-1/content"),
                startInclusive: 5000,
                endInclusive: 5999,
                destination,
                TestContext.Current.CancellationToken));

        Assert.Equal(5000, exception.StartInclusive);
        Assert.Equal(5999, exception.EndInclusive);
        Assert.Empty(destination.ToArray());
    }

    [Fact]
    public async Task The_engine_stops_when_the_transfer_is_cancelled()
    {
        using var cancellation = new CancellationTokenSource();

        var handler = new RecordingHandler(
            _ =>
            {
                cancellation.Cancel();

                return new HttpResponseMessage(HttpStatusCode.PartialContent)
                {
                    Content = new ByteArrayContent(new byte[8]),
                };
            });

        var engine = new HttpRangeTransferEngine(new HttpClient(handler));

        using var destination = new MemoryStream();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => engine.TransferAsync(
                new Uri("http://192.168.1.10:5000/api/v1/packages/pkg-1/content"),
                startInclusive: 0,
                endInclusive: 7,
                destination,
                cancellation.Token));
    }

    [Fact]
    public async Task The_engine_rejects_a_url_that_is_not_absolute()
    {
        var handler = new RecordingHandler(
            _ => new HttpResponseMessage(HttpStatusCode.OK));

        var engine = new HttpRangeTransferEngine(new HttpClient(handler));

        using var destination = new MemoryStream();

        await Assert.ThrowsAsync<ArgumentException>(
            () => engine.TransferAsync(
                new Uri("/api/v1/packages/pkg-1/content", UriKind.Relative),
                startInclusive: 0,
                endInclusive: 7,
                destination,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_engine_rejects_a_range_that_ends_before_it_starts()
    {
        var handler = new RecordingHandler(
            _ => new HttpResponseMessage(HttpStatusCode.OK));

        var engine = new HttpRangeTransferEngine(new HttpClient(handler));

        using var destination = new MemoryStream();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => engine.TransferAsync(
                new Uri("http://192.168.1.10:5000/api/v1/packages/pkg-1/content"),
                startInclusive: 100,
                endInclusive: 99,
                destination,
                TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Handler that records the requests that the engine builds and answers with what the
    /// test declares.
    /// </summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _answer;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> answer)
        {
            _answer = answer;
        }

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);

            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(_answer(request));
        }
    }
}
