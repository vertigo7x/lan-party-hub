using LANPartyHub.Server.Application.Ports;
using LANPartyHub.Server.Infrastructure.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Server.Infrastructure.Http;

/// <summary>
/// Correlation of the requests of the administration surface (AD-15).
///
/// <para>
/// The surface of the administration is not the surface of the client: the organizer is
/// not a client of the LAN and does not carry the identifier of a client, so this
/// middleware does not ask for it. The <c>clientId</c> is a key of correlation and not a
/// credential (AD-30), and the policy of the repository forbids adding an authentication of
/// the client (AD-6).
/// </para>
/// <para>
/// The operation is a unit of work all the same, so the middleware opens the correlation
/// scope with the identifier of the operation, generated when the request does not carry a
/// usable one, and it publishes that identifier in the response so the log of the server
/// and the log of the organizer can be joined (AD-15).
/// </para>
/// </summary>
public sealed class AdminCorrelationMiddleware
{
    /// <summary>Header that carries the identifier of the unit of work (AD-12).</summary>
    public const string OperationIdHeaderName = "X-Operation-Id";

    /// <summary>
    /// Number of hexadecimal digits of the canonical form of an identifier (AD-23, AD-31).
    /// </summary>
    public const int CanonicalIdentifierLength = 32;

    private readonly RequestDelegate _next;
    private readonly CorrelationScopeFactory _scopeFactory;
    private readonly IMetricsCounters _metrics;
    private readonly ILogger<AdminCorrelationMiddleware> _logger;

    /// <summary>Creates the middleware of the correlation of the administration.</summary>
    /// <param name="next">Next element of the pipeline of the request.</param>
    /// <param name="scopeFactory">Factory that opens the correlation scope in the log.</param>
    /// <param name="metrics">Counters of the host, fed by the requests.</param>
    /// <param name="logger">Log of the middleware.</param>
    public AdminCorrelationMiddleware(
        RequestDelegate next,
        CorrelationScopeFactory scopeFactory,
        IMetricsCounters metrics,
        ILogger<AdminCorrelationMiddleware> logger)
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

        var operationId = ReadCanonicalIdentifier(context.Request, OperationIdHeaderName)
            ?? GenerateOperationId();

        context.Response.Headers[OperationIdHeaderName] = operationId;

        using var scope = _scopeFactory.Open(operationId, clientId: null);

        _metrics.OperationStarted();

        try
        {
            await _next(context).ConfigureAwait(false);

            _logger.LogInformation(
                "Petición de la superficie de administración atendida con el estado {StatusCode}.",
                context.Response.StatusCode);
        }
        finally
        {
            _metrics.OperationCompleted();
        }
    }

    /// <summary>
    /// Reads the value of a header when it is in the canonical form of an identifier: 32
    /// lowercase hexadecimal digits and nothing else (AD-23, AD-31). A header that does not
    /// arrive, that arrives repeated, that arrives empty or that carries another form is
    /// read as absent.
    /// </summary>
    /// <param name="request">Request of the organizer.</param>
    /// <param name="name">Name of the header.</param>
    /// <returns>The canonical identifier, or <c>null</c>.</returns>
    private static string? ReadCanonicalIdentifier(HttpRequest request, string name)
    {
        var values = request.Headers[name];

        if (values.Count != 1)
        {
            return null;
        }

        var value = values[0]?.Trim() ?? string.Empty;

        return value is { Length: CanonicalIdentifierLength }
            && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f')
            ? value
            : null;
    }

    /// <summary>
    /// Generates the identifier of a unit of work. It is an opaque UUID v7, like the rest of
    /// the identifiers of the system (AD-23).
    /// </summary>
    /// <returns>The generated identifier, in its canonical form.</returns>
    private static string GenerateOperationId() => Guid.CreateVersion7().ToString("N");
}
