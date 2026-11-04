using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using LANPartyHub.Client.Application.Downloads;
using LANPartyHub.Client.Application.Ports;

namespace LANPartyHub.Client.Infrastructure.Networking;

/// <summary>
/// The only implementation of the port of the transfer of bytes of the MVP
/// (AD-4, AD-20, AD-24, AD-28).
///
/// <para>
/// The engine asks for a range of the content over HTTP with the header <c>Range</c> and it
/// interprets the answer of the server:
/// </para>
/// <list type="bullet">
/// <item>
/// <c>206 Partial Content</c>: the server honoured the range and it declares the range
/// that it served in <c>Content-Range</c>. The bytes are copied and the total of the
/// content is read from the header.
/// </item>
/// <item>
/// <c>200 OK</c>: the server ignored the range and it served the whole content. The bytes
/// are copied from the beginning, so the accumulated offset of the partial file does not
/// apply and the range was not honoured.
/// </item>
/// <list type="bullet">
/// <item>
/// <c>416 Range Not Satisfiable</c>: the range cannot be satisfied. It is a failure of the
/// transfer, and the caller decides what it means for the operation.
/// </item>
/// </list>
/// <para>
/// The engine knows NO route and resolves NO host: the caller brings the absolute URL of the
/// content and the range, so the engine cannot download from the Internet on its own.
/// </para>
/// </summary>
public sealed class HttpRangeTransferEngine : ITransferEngine
{
    /// <summary>Header that asks for a range of the content.</summary>
    public const string RangeHeaderName = "Range";

    /// <summary>Header that declares the range that the server served.</summary>
    public const string ContentRangeHeaderName = "Content-Range";

    /// <summary>Header that declares the total size of the content.</summary>
    public const string ContentLengthHeaderName = "Content-Length";

    /// <summary>
    /// Sentinel that declares that the range ends at the END of the content, so the
    /// request travels as <c>bytes=&lt;start&gt;-</c> (AD-20).
    /// </summary>
    public const long UnboundedEnd = long.MaxValue;

    private readonly HttpClient _httpClient;

    /// <summary>Creates the engine over a client of the event server.</summary>
    /// <param name="httpClient">
    /// Client of the event server. Its base address is not used by the engine: the caller
    /// brings the absolute URL of the content in every call.
    /// </param>
    public HttpRangeTransferEngine(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task<TransferOutcome> TransferAsync(
        Uri contentUri,
        long startInclusive,
        long endInclusive,
        Stream destination,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contentUri);
        ArgumentNullException.ThrowIfNull(destination);

        if (!contentUri.IsAbsoluteUri)
        {
            throw new ArgumentException(
                "La URL del contenido tiene que ser absoluta.",
                nameof(contentUri));
        }

        if (startInclusive < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(startInclusive),
                "El inicio del rango no puede ser negativo.");
        }

        if (endInclusive < startInclusive)
        {
            throw new ArgumentOutOfRangeException(
                nameof(endInclusive),
                "El final del rango no puede ser anterior al inicio.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, contentUri);

        // AD-20: el rango que llega al servidor es el que empieza en el offset
        // acumulado. Un rango SIN final declarado viaja como «bytes=&lt;start&gt;-», que es
        // la forma en la que el servidor sirve el resto del contenido (AD-20).
        request.Headers.Range = endInclusive == UnboundedEnd
            ? new RangeHeaderValue(startInclusive, null)
            : new RangeHeaderValue(startInclusive, endInclusive);

        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);

        // AD-20: el servidor sirve el paquete por HTTP con Range requests. Un 416 dice
        // que el rango no es satisfacible, y el 5.2 decide qué significa para la
        // operación; el motor lo reporta como una falla de la transferencia.
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            throw new TransferRangeNotSatisfiableException(startInclusive, endInclusive);
        }

        response.EnsureSuccessStatusCode();

        // El servidor honró el rango cuando answered con 206. Un 200 significa que lo
        // ignoró y sirvió el contenido entero: los bytes empiezan por el principio, así
        // que el offset acumulado de la reanudación no aplica y el destino se deja como
        // estaba.
        var rangeHonoured = response.StatusCode == HttpStatusCode.PartialContent;

        // AD-20: el total del contenido llega en la cabecera de la respuesta, ANTES del
        // cuerpo, así que el motor lo declara al destino y el cliente puede mostrar el
        // porcentaje y el par de tamaños mientras la descarga sigue en curso (UX-DR-5).
        var totalBytes = rangeHonoured
            ? ReadTotalBytes(response)
            : ReadContentLength(response);

        if (destination is IContentTotalAware aware && totalBytes is { } declared)
        {
            aware.DeclareTotal(declared);
        }

        var transferred = await CopyAsync(response, destination, cancellationToken).ConfigureAwait(false);

        return new TransferOutcome(transferred, totalBytes, rangeHonoured);
    }

    /// <summary>
    /// Sends the request and TRANSLATES a failure of the transport into the failure that
    /// the Application layer understands (AD-1, AD-11, AD-24, SM-2).
    ///
    /// <para>
    /// Un fallo de transporte (el servidor no está, la red se cayó, la respuesta tardó) NO
    /// es una falla de la operación: el parcial y su offset persistido siguen valiendo, así
    /// que el siguiente arranque reintenta el <c>Range</c> desde ese offset (SM-2). La
    /// traducción la hace el ADAPTADOR porque es el único que sabe qué es un fallo de su
    /// transporte (AD-1).
    /// </para>
    /// </summary>
    /// <param name="request">Request to send.</param>
    /// <param name="cancellationToken">Token that cancels the transfer.</param>
    /// <returns>The response of the server.</returns>
    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (IsTransportFailure(exception, cancellationToken))
        {
            throw new TransferTransportException(
                "El transporte no pudo traer los bytes del contenido.",
                exception);
        }
    }

    /// <summary>
    /// Tells whether a failure is a failure of the TRANSPORT and not a CANCELLATION
    /// (AD-11, AD-24).
    ///
    /// <para>
    /// La cancelación NO es un fallo de transporte: la pidió el jugador o el cierre del
    /// cliente, así que viaja intacta y la operación conserva su estado (AC3, AD-11).
    /// </para>
    /// <para>
    /// El tiempo de espera agotado de <see cref="HttpClient"/> llega como una
    /// <see cref="TaskCanceledException"/> que ENVUELVE un <see cref="TimeoutException"/>:
    /// esa sí es un fallo de transporte, porque el token del llamante no está cancelado.
    /// </para>
    /// </summary>
    /// <param name="exception">Failure that the transport raised.</param>
    /// <param name="cancellationToken">Token of the caller.</param>
    /// <returns><c>true</c> when the failure is a failure of the transport.</returns>
    private static bool IsTransportFailure(Exception exception, CancellationToken cancellationToken)
    {
        if (exception is HttpRequestException or IOException or TimeoutException or SocketException)
        {
            return true;
        }

        // El tiempo de espera agotado del cliente HTTP llega como una cancelación con un
        // TimeoutException dentro. El token del llamante NO está cancelado, así que no es
        // una cancelación del jugador.
        return exception is TaskCanceledException { InnerException: TimeoutException }
            && !cancellationToken.IsCancellationRequested;
    }

    /// <summary>
    /// Reads the total size of the content that the server declares in the
    /// <c>Content-Range</c> of a partial answer.
    /// </summary>
    /// <param name="response">Response of the server.</param>
    /// <returns>The total size of the content, or <c>null</c> when it is not declared.</returns>
    private static long? ReadTotalBytes(HttpResponseMessage response)
    {
        if (response.Content.Headers.ContentRange is not { } contentRange)
        {
            return null;
        }

        return contentRange.Length;
    }

    /// <summary>
    /// Reads the size of the answer that the server declares in <c>Content-Length</c>.
    /// </summary>
    /// <param name="response">Response of the server.</param>
    /// <returns>The size of the answer, or <c>null</c> when it is not declared.</returns>
    private static long? ReadContentLength(HttpResponseMessage response) =>
        response.Content.Headers.ContentLength;

    /// <summary>
    /// Copies the body of the answer into the destination and counts the bytes that it
    /// wrote.
    /// </summary>
    /// <param name="response">Response of the server.</param>
    /// <param name="destination">Stream that receives the bytes.</param>
    /// <param name="cancellationToken">Token that cancels the transfer.</param>
    /// <returns>The number of bytes that the transfer wrote.</returns>
    private static async Task<long> CopyAsync(
        HttpResponseMessage response,
        Stream destination,
        CancellationToken cancellationToken)
    {
        await using var source = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        var buffer = new byte[81920];

        long transferred = 0;

        int read;

        while ((read = await source
            .ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
            .ConfigureAwait(false)) > 0)
        {
            await destination
                .WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                .ConfigureAwait(false);

            transferred += read;
        }

        return transferred;
    }
}
