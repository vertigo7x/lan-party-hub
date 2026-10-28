using System.Collections;
using System.Collections.Generic;
using System.Text.Json;
using LANPartyHub.Server.Application.Ports;
using LANPartyHub.Server.Infrastructure.Logging;
using LANPartyHub.Shared.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Server.Infrastructure.Http;

/// <summary>
/// Correlation of the requests of the client (AD-15, AD-23).
///
/// <para>
/// The middleware opens the correlation scope of every request of the surface of the client:
/// it reads the <c>X-Client-Id</c> and the <c>X-Operation-Id</c> headers, it generates the
/// identifier of the operation when the client does not send a usable one, and it publishes
/// the identifiers in the scope of the log so that every entry written while the request is
/// being served carries them.
/// </para>
/// <para>
/// The <c>clientId</c> is mandatory in every request of the surface <c>/api/v1</c> and it
/// has to be in its canonical form: a request that does not carry it, or that carries one
/// that is not the canonical form, is rejected with a <c>400</c> whose body is the single
/// error shape of the system, <see cref="ApiError"/>. The identifier is a key of
/// correlation and not a credential: it never authenticates anything (AD-30).
/// </para>
/// <para>
/// The rejection is a unit of work too: it happens inside the scope, so the entry that
/// tells the operator why the request was rejected carries the identifier of the
/// operation, and the effective identifier travels back in the response so the log of the
/// server and the log of the client can be joined.
/// </para>
/// <para>
/// The surface of the host, <c>/metrics</c>, is not the surface of the client: it is not
/// covered by this middleware, so the diagnosis of the server never depends on the
/// identifier of a client.
/// </para>
/// </summary>
public sealed class CorrelationMiddleware
{
    /// <summary>Header that carries the identifier of the client (AD-23).</summary>
    public const string ClientIdHeaderName = "X-Client-Id";

    /// <summary>Header that carries the identifier of the unit of work (AD-12).</summary>
    public const string OperationIdHeaderName = "X-Operation-Id";

    /// <summary>
    /// Number of hexadecimal digits of the canonical form of an identifier (AD-23, AD-31).
    /// </summary>
    public const int CanonicalIdentifierLength = 32;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly RequestDelegate _next;
    private readonly CorrelationScopeFactory _scopeFactory;
    private readonly IMetricsCounters _metrics;
    private readonly ILogger<CorrelationMiddleware> _logger;

    /// <summary>Creates the middleware of the correlation.</summary>
    /// <param name="next">Next element of the pipeline of the request.</param>
    /// <param name="scopeFactory">Factory that opens the correlation scope in the log.</param>
    /// <param name="metrics">Counters of the host, fed by the requests.</param>
    /// <param name="logger">Log of the middleware.</param>
    public CorrelationMiddleware(
        RequestDelegate next,
        CorrelationScopeFactory scopeFactory,
        IMetricsCounters metrics,
        ILogger<CorrelationMiddleware> logger)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(logger);

        _next = next;
        _scopeFactory = scopeFactory;
        _metrics = metrics;
        _logger = logger;
    }

    /// <summary>
    /// Serves the request with its correlation scope open.
    /// </summary>
    /// <param name="context">Context of the request.</param>
    /// <returns>A task that completes when the request has been served.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var originalBody = context.Response.Body;
        var counter = new CountingResponseStream(originalBody);

        context.Response.Body = counter;

        try
        {
            // The identifier of the unit of work comes first: a request that is rejected is
            // a unit of work too, and the operator needs its identifier to join the log of
            // the server with the log of the client.
            var operationId = ReadCanonicalIdentifier(context.Request, OperationIdHeaderName)
                ?? GenerateOperationId();

            // The effective identifier travels back to the client, so the two logs can be
            // joined even when the client did not send one.
            context.Response.Headers[OperationIdHeaderName] = operationId;

            var clientId = ReadCanonicalIdentifier(context.Request, ClientIdHeaderName);

            using var scope = _scopeFactory.Open(operationId, clientId);

            _metrics.OperationStarted();

            try
            {
                if (clientId is null)
                {
                    await RejectAsync(context).ConfigureAwait(false);

                    return;
                }

                await _next(context).ConfigureAwait(false);

                _logger.LogInformation(
                    "Petición de la superficie de cliente atendida con el estado {StatusCode}.",
                    context.Response.StatusCode);
            }
            finally
            {
                _metrics.OperationCompleted();
            }
        }
        finally
        {
            // The bytes that the host has written are counted before the stream of the
            // response is restored, so the metric does not depend on the restoration.
            _metrics.RecordBytesWritten(counter.BytesWritten);

            var installed = context.Response.Body;

            context.Response.Body = originalBody;

            if (!ReferenceEquals(installed, counter))
            {
                // A middleware that runs after this one can replace the stream of the
                // response. Its stream is closed and not discarded in silence: the content
                // it holds reaches the response through the counting stream.
                installed.Dispose();
            }
        }
    }

    /// <summary>
    /// Rejects the request that does not carry a usable identifier of the client. The body
    /// of the rejection is the single error shape of the system, taken from the catalog of
    /// errors, and the rejection is counted in the metric of errors per code.
    /// </summary>
    /// <param name="context">Context of the request.</param>
    /// <returns>A task that completes when the rejection has been written.</returns>
    private async Task RejectAsync(HttpContext context)
    {
        var rawClientId = ReadHeader(context.Request, ClientIdHeaderName);

        var error = rawClientId.Length == 0
            ? ErrorCatalog.Resolve(ErrorCatalog.ClientIdMissingCode)
            : ErrorCatalog.Resolve(ErrorCatalog.ClientIdInvalidCode);

        _metrics.RecordError(error.Code);

        // The code travels in the structured field of the entry and not in the text of the
        // message: the code is an identifier and never the text that the operator reads
        // first (AD-15, AD-25).
        _logger.Log(
            LogLevel.Warning,
            new EventId(0, "ClientRequestRejected"),
            new CatalogCodeState(error.Code),
            exception: null,
            formatter: static (_, _) => CatalogCodeState.RejectionMessage);

        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        context.Response.ContentType = "application/json; charset=utf-8";

        await context.Response
            .WriteAsync(JsonSerializer.Serialize(error, SerializerOptions), context.RequestAborted)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the single value of a header. A header that arrives repeated or with an empty
    /// value is read as absent.
    /// </summary>
    /// <param name="request">Request of the client.</param>
    /// <param name="name">Name of the header.</param>
    /// <returns>The value of the header, or an empty string.</returns>
    private static string ReadHeader(HttpRequest request, string name)
    {
        var values = request.Headers[name];

        if (values.Count != 1)
        {
            return string.Empty;
        }

        return values[0]?.Trim() ?? string.Empty;
    }

    /// <summary>
    /// Reads the value of a header when it is in the canonical form of an identifier: 32
    /// lowercase hexadecimal digits and nothing else (AD-23, AD-31). A header that does not
    /// arrive, that arrives repeated, that arrives empty or that carries another form is
    /// read as absent.
    /// </summary>
    /// <param name="request">Request of the client.</param>
    /// <param name="name">Name of the header.</param>
    /// <returns>The canonical identifier, or <c>null</c>.</returns>
    private static string? ReadCanonicalIdentifier(HttpRequest request, string name)
    {
        var value = ReadHeader(request, name);

        return IsCanonicalIdentifier(value) ? value : null;
    }

    /// <summary>
    /// Tells whether a value is the canonical form of an identifier.
    /// </summary>
    /// <param name="value">Value to check.</param>
    /// <returns><c>true</c> when the value is canonical.</returns>
    private static bool IsCanonicalIdentifier(string? value) =>
        value is { Length: CanonicalIdentifierLength }
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    /// <summary>
    /// Generates the identifier of a unit of work. It is an opaque UUID v7, like the rest of
    /// the identifiers of the system (AD-23).
    /// </summary>
    /// <returns>The generated identifier, in its canonical form.</returns>
    private static string GenerateOperationId() => Guid.CreateVersion7().ToString("N");

    /// <summary>
    /// State of a log entry that carries the code of the catalog of errors as a structured
    /// field and keeps it out of the text of the message (AD-15, AD-25).
    /// </summary>
    private sealed class CatalogCodeState : IReadOnlyList<KeyValuePair<string, object?>>
    {
        private readonly KeyValuePair<string, object?>[] _fields;

        public CatalogCodeState(string code)
        {
            _fields = [new KeyValuePair<string, object?>("code", code)];
        }

        public int Count => _fields.Length;

        public KeyValuePair<string, object?> this[int index] => _fields[index];

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() =>
            ((IEnumerable<KeyValuePair<string, object?>>)_fields).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => _fields.GetEnumerator();

        /// <summary>
        /// The text of the state is the text that the operator reads: the code of the
        /// catalog never reaches it.
        /// </summary>
        public override string ToString() => RejectionMessage;

        /// <summary>Text of the entry that tells that the request has been rejected.</summary>
        public const string RejectionMessage =
            "Se rechaza la petición de la superficie de cliente porque no lleva un identificador de cliente válido.";
    }
}
