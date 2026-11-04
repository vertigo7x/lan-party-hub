using System.Text.Json;
using LANPartyHub.Server.Application.Ports;
using LANPartyHub.Server.Infrastructure.Networking;
using LANPartyHub.Shared.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Server.Host;

/// <summary>
/// Surface of the client that serves the bytes of a published package (AD-20, AC7).
///
/// <para>
/// El servidor sirve el paquete por HTTP con soporte de <c>Range</c> requests, sobre una
/// SOLA conexión y sin chunking multipart (AD-20). La ruta es de la superficie del
/// cliente, así que exige el identificador del cliente y está correlacionada por el
/// middleware de la correlación (AD-15, AD-23).
/// </para>
/// <para>
/// La respuesta es una de TRES, y solo tres:
/// </para>
/// <list type="bullet">
/// <item>
/// <c>206 Partial Content</c>: el servidor honró el rango y sirvió el tramo pedido,
/// declarando el tramo en <c>Content-Range</c> y el total en <c>Content-Length</c>.
/// </item>
/// <item>
/// <c>200 OK</c>: la petición NO llevaba rango, así que el servidor sirvió el contenido
/// entero.
/// </item>
/// <item>
/// <c>416 Range Not Satisfiable</c>: el rango pedido no es satisfacible porque empieza
/// más allá del final del contenido.
/// </item>
/// </list>
/// <para>
/// AD-14: el reparto del enlace es POR CLIENTE, así que el servidor admite un número
/// CONFIGURABLE de respuestas de rango simultáneas por cliente, y todas las conexiones de
/// un cliente beben del mismo cubo. Un cliente que abre más descargas de las que su
/// límite admite espera su turno sin perder los bytes que ya tenía (AD-14, AD-20).
/// </para>
/// </summary>
public static class PackageContentEndpoint
{
    /// <summary>Route that serves the bytes of a published package.</summary>
    public const string Route = "/api/v1/packages/{packageId}/content";

    /// <summary>Header that carries the identifier of the client (AD-23).</summary>
    public const string ClientIdHeaderName = "X-Client-Id";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Registers the service of the content of the packages in the application.
    /// </summary>
    /// <param name="app">Application of the server.</param>
    public static void Map(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet(Route, async (
            HttpContext context,
            IPackageContentStore contentStore,
            PerClientShareLimiter shareLimiter,
            IMetricsCounters metrics,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("LANPartyHub.Server.PackageContent");

            var packageId = context.Request.RouteValues["packageId"] as string;

            if (string.IsNullOrWhiteSpace(packageId))
            {
                await WriteErrorAsync(
                    context,
                    metrics,
                    logger,
                    StatusCodes.Status400BadRequest,
                    ErrorCatalog.Resolve(ErrorCatalog.DownloadPackageMissingCode),
                    null)
                    .ConfigureAwait(false);

                return;
            }

            PackageContentStream? content;

            try
            {
                content = await contentStore
                    .OpenAsync(packageId, context.RequestAborted)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(
                    exception,
                    "No se pudo abrir el contenido del paquete {PackageId}.",
                    packageId);

                await WriteErrorAsync(
                    context,
                    metrics,
                    logger,
                    StatusCodes.Status500InternalServerError,
                    ErrorCatalog.Resolve(ErrorCatalog.UnknownErrorCode),
                    null)
                    .ConfigureAwait(false);

                return;
            }

            if (content is null)
            {
                // El paquete no está en el repositorio del servidor: no hay bytes que
                // servir y la ruta no existe (AD-9, AD-22).
                await WriteErrorAsync(
                    context,
                    metrics,
                    logger,
                    StatusCodes.Status404NotFound,
                    ErrorCatalog.Resolve(ErrorCatalog.DownloadPackageNotFoundCode),
                    packageId)
                    .ConfigureAwait(false);

                return;
            }

            await using (content.Content)
            {
                var rangeHeader = context.Request.Headers.Range;
                var hasRange = !string.IsNullOrWhiteSpace(rangeHeader);
                var range = ParseRange(rangeHeader, content.Length);

                if (hasRange && range is null)
                {
                    // AD-20, AC7: el rango no es satisfacible, así que la respuesta es un
                    // 416 y NO un 200 con el contenido entero: el cliente tiene que saber
                    // que su offset ya no vale.
                    context.Response.Headers.ContentRange = $"bytes */{content.Length}";

                    await WriteErrorAsync(
                        context,
                        metrics,
                        logger,
                        StatusCodes.Status416RangeNotSatisfiable,
                        ErrorCatalog.Resolve(ErrorCatalog.DownloadRangeNotSatisfiableCode),
                        null)
                        .ConfigureAwait(false);

                    return;
                }

                var clientId = ReadClientId(context);

                // AD-14: el turno es POR CLIENTE. El servidor admite un número limitado de
                // respuestas de rango simultáneas por cliente, y las conexiones que
                // superan el límite esperan a que una termine su tramo.
                using var lease = await shareLimiter
                    .AcquireAsync(clientId, context.RequestAborted)
                    .ConfigureAwait(false);

                if (hasRange)
                {
                    var requestedRange = range!;

                    context.Response.StatusCode = StatusCodes.Status206PartialContent;
                    context.Response.Headers.ContentRange =
                        $"bytes {requestedRange.Start}-{requestedRange.End}/{content.Length}";
                    context.Response.ContentLength = requestedRange.Length;

                    await CopyRangeAsync(
                        context,
                        content,
                        requestedRange,
                        logger)
                        .ConfigureAwait(false);

                    return;
                }

                context.Response.StatusCode = StatusCodes.Status200OK;
                context.Response.ContentLength = content.Length;

                await CopyRangeAsync(
                    context,
                    content,
                    new RangeSatisfiable(0, content.Length - 1),
                    logger)
                    .ConfigureAwait(false);
            }
        });
    }

    /// <summary>
    /// Copies the bytes of the range of the content into the response (AD-20).
    /// </summary>
    /// <param name="context">Context of the request.</param>
    /// <param name="content">Content of the package.</param>
    /// <param name="range">Range to serve.</param>
    /// <param name="logger">Log of the service of the content.</param>
    /// <returns>A task that completes when the bytes have been written.</returns>
    private static async Task CopyRangeAsync(
        HttpContext context,
        PackageContentStream content,
        RangeSatisfiable range,
        ILogger logger)
    {
        try
        {
            if (range.Start > 0)
            {
                content.Content.Seek(range.Start, SeekOrigin.Begin);
            }

            var remaining = range.Length;
            var buffer = new byte[81920];

            while (remaining > 0)
            {
                var read = await content.Content
                    .ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), context.RequestAborted)
                    .ConfigureAwait(false);

                if (read <= 0)
                {
                    break;
                }

                await context.Response
                    .Body.WriteAsync(buffer.AsMemory(0, read), context.RequestAborted)
                    .ConfigureAwait(false);

                remaining -= read;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // El cliente cerró la conexión a mitad del tramo: el log lo dice y la
            // respuesta ya está empezada, así que no se puede cambiar su estado (AD-15).
            logger.LogWarning(
                exception,
                "La transferencia del contenido se interrumpió: el cliente cerró la conexión.");
        }
    }

    /// <summary>
    /// Reads the identifier of la correlación del cliente, o un valor estable cuando la
    /// petición no lo lleva (AD-14, AD-23).
    ///
    /// <para>
    /// La superficie del cliente exige el identificador y el middleware de la correlación
    /// lo rechaza cuando falta, así que aquí el valor NUNCA es vacío: el caso de que
    /// llegase vacío es un fallo del middleware, y un cubo compartido por todos esos
    /// casos sería un cubo compartido por todo el evento (AD-14).
    /// </para>
    /// </summary>
    /// <param name="context">Context of the request.</param>
    /// <returns>The identifier of the client.</returns>
    private static string ReadClientId(HttpContext context)
    {
        var values = context.Request.Headers[ClientIdHeaderName];

        if (values.Count == 1)
        {
            var value = values[0]?.Trim();

            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return "anonymous";
    }

    /// <summary>
    /// Parses the header <c>Range</c> of the request (AD-20).
    ///
    /// <para>
    /// Solo se acepta el rango de BYTES, y solo uno: el MVP sirve el paquete sobre una
    /// sola conexión y sin chunking multipart, así que un rango múltiple o de otro tipo
    /// no se sirve (AD-20).
    /// </para>
    /// </summary>
    /// <param name="header">Value of the header <c>Range</c>, or <c>null</c> when it does not arrive.</param>
    /// <param name="length">Exact length of the content.</param>
    /// <returns>
    /// The range to serve, or <c>null</c> when the range asked is not satisfacible.
    /// </returns>
    private static RangeSatisfiable? ParseRange(string? header, long length)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return new RangeSatisfiable(0, length - 1);
        }

        var value = header.Trim();

        const string prefix = "bytes=";

        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var specification = value[prefix.Length..].Trim();

        // Un rango múltiple o un rango de otro tipo no se sirve en el MVP: el paquete se
        // sirve sobre una sola conexión y sin chunking multipart (AD-20).
        if (specification.Contains(',', StringComparison.Ordinal))
        {
            return null;
        }

        var separator = specification.IndexOf('-', StringComparison.Ordinal);

        if (separator < 0)
        {
            return null;
        }

        var firstText = specification[..separator].Trim();
        var lastText = specification[(separator + 1)..].Trim();

        if (firstText.Length == 0)
        {
            // La forma "bytes=-N" pide los ÚLTIMOS N bytes: se sirve como un rango
            // explícito, con el mismo camino que el resto (AD-20).
            if (!long.TryParse(lastText, out var suffix) || suffix <= 0)
            {
                return null;
            }

            var start = suffix >= length ? 0 : length - suffix;

            return new RangeSatisfiable(start, length - 1);
        }

        if (!long.TryParse(firstText, out var requestedStart) || requestedStart < 0)
        {
            return null;
        }

        long requestedEnd;

        if (lastText.Length == 0)
        {
            // La forma "bytes=N-" pide desde N hasta el final del contenido (AD-20).
            requestedEnd = length - 1;
        }
        else if (!long.TryParse(lastText, out requestedEnd))
        {
            return null;
        }

        if (requestedStart >= length)
        {
            // El rango empieza más allá del final del contenido: no es satisfacible y la
            // respuesta es un 416 (AD-20, AC7).
            return null;
        }

        if (requestedEnd >= length)
        {
            requestedEnd = length - 1;
        }

        if (requestedEnd < requestedStart)
        {
            return null;
        }

        return new RangeSatisfiable(requestedStart, requestedEnd);
    }

    /// <summary>
    /// Writes the single error shape of the system in the response and counts the error
    /// under its stable code (AD-15, AD-25).
    /// </summary>
    /// <param name="context">Context of the request.</param>
    /// <param name="metrics">Counters of the host.</param>
    /// <param name="logger">Log of the service of the content.</param>
    /// <param name="statusCode">Status of the response.</param>
    /// <param name="error">Error to write.</param>
    /// <param name="field">Concrete field that fails, when the rejection names one.</param>
    /// <returns>A task that completes when the error has been written.</returns>
    private static async Task WriteErrorAsync(
        HttpContext context,
        IMetricsCounters metrics,
        ILogger logger,
        int statusCode,
        ApiError error,
        string? field)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(error);

        metrics.RecordError(error.Code);

        logger.LogWarning(
            "Se rechaza la petición del contenido con el código {Code} y el estado {StatusCode}.",
            error.Code,
            statusCode);

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";

        await context.Response
            .WriteAsync(JsonSerializer.Serialize(error, SerializerOptions), context.RequestAborted)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// A range of the content that the server can serve (AD-20).
    /// </summary>
    /// <param name="Start">First byte of the range, included.</param>
    /// <param name="End">Last byte of the range, included.</param>
    public sealed record RangeSatisfiable(long Start, long End)
    {
        /// <summary>Number of bytes of the range.</summary>
        public long Length => End - Start + 1;
    }
}
