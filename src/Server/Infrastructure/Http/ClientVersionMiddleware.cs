using System.Text.Json;
using LANPartyHub.Server.Application.Ports;
using LANPartyHub.Server.Domain.Clients;
using LANPartyHub.Server.Host;
using LANPartyHub.Shared.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Server.Infrastructure.Http;

/// <summary>
/// Gate of the compatibility of the versions of the clients (AD-6, FR-4).
///
/// <para>
/// The middleware reads the <c>X-Client-Version</c> and the <c>X-Protocol-Version</c>
/// headers that EVERY request of the surface of the client carries, and it compares the
/// declared protocol version against the range that the server supports. A request whose
/// headers are absent, unreadable or outside of the range is answered with a
/// <c>426 Upgrade Required</c> whose body is the single error shape of the system,
/// <see cref="ApiError"/>, taken from the catalog of errors (AD-6, AD-15).
/// </para>
/// <para>
/// The answer carries ALSO the headers <c>X-Server-Version</c> and
/// <c>X-Minimum-Protocol-Version</c>, so a client reads the version of the server and the
/// range mínimo que soporta SIN tener que interpretar texto (AD-6).
/// </para>
/// <para>
/// The middleware is the ONLY control of access of the surface: there are no tokens, no
/// sessions and no roles. The version of the client is a declaration and NOT a
/// credential: it never authenticates anything (AD-6, AD-12, R-MVP-2).
/// </para>
/// <para>
/// The rejection is counted in the metric of errors per code and the log registers the
/// version of the client y la incompatibilidad detectada, so the organizer knows which
/// client is out of date (AD-15, FR-4).
/// </para>
/// </summary>
public sealed class ClientVersionMiddleware
{
    /// <summary>Header that carries the version of the binary of the client (AD-6).</summary>
    public const string ClientVersionHeaderName = "X-Client-Version";

    /// <summary>Header that carries the version of the protocol that the client speaks (AD-6).</summary>
    public const string ProtocolVersionHeaderName = "X-Protocol-Version";

    /// <summary>Header that carries the version of the binary of the server (AD-6).</summary>
    public const string ServerVersionHeaderName = "X-Server-Version";

    /// <summary>
    /// Header that carries the oldest protocol version that the server accepts (AD-6).
    /// </summary>
    public const string MinimumProtocolVersionHeaderName = "X-Minimum-Protocol-Version";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly RequestDelegate _next;
    private readonly IMetricsCounters _metrics;
    private readonly ServerConfiguration _configuration;
    private readonly ILogger<ClientVersionMiddleware> _logger;

    /// <summary>Creates the middleware of the compatibility of the versions.</summary>
    /// <param name="next">Next element of the pipeline of the request.</param>
    /// <param name="metrics">Counters of the host, fed by the requests.</param>
    /// <param name="configuration">Effective configuration of the server, with the policy of the minimum version.</param>
    /// <param name="logger">Log of the middleware.</param>
    public ClientVersionMiddleware(
        RequestDelegate next,
        IMetricsCounters metrics,
        ServerConfiguration configuration,
        ILogger<ClientVersionMiddleware> logger)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);

        _next = next;
        _metrics = metrics;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Serves the request when the client is compatible, and rejects it with a
    /// <c>426 Upgrade Required</c> when it is not (AD-6).
    /// </summary>
    /// <param name="context">Context of the request.</param>
    /// <returns>A task that completes when the request has been served.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var clientVersion = ReadHeader(context.Request, ClientVersionHeaderName);
        var protocolVersion = ReadHeader(context.Request, ProtocolVersionHeaderName);

        if (TryReadProtocolVersion(protocolVersion, out var declaredProtocolVersion)
            && ProtocolCompatibility.IsCompatible(
                declaredProtocolVersion,
                _configuration.MinimumProtocolVersion,
                ServerVersion.ProtocolVersion))
        {
            await _next(context).ConfigureAwait(false);

            return;
        }

        await RejectAsync(context, clientVersion, protocolVersion).ConfigureAwait(false);
    }

    /// <summary>
    /// Rejects the request of a client that no es compatible. The body is the single error
    /// shape of the system y la respuesta lleva la versión del servidor y el rango mínimo
    /// que soporta (AD-6, AD-15).
    /// </summary>
    /// <param name="context">Context of the request.</param>
    /// <param name="clientVersion">Version of the client that the request declared.</param>
    /// <param name="protocolVersion">Protocol version that the request declared.</param>
    /// <returns>A task that completes when the rejection has been written.</returns>
    private async Task RejectAsync(
        HttpContext context,
        string clientVersion,
        string protocolVersion)
    {
        var error = ErrorCatalog.Resolve(ErrorCatalog.ClientVersionIncompatibleCode);

        _metrics.RecordError(error.Code);

        // La versión del cliente y la incompatibilidad detectada quedan en el log, para
        // que el organizador sepa qué cliente está desactualizado (FR-4, AD-15).
        _logger.LogWarning(
            "El cliente {ClientVersion} con la versión de protocolo {ProtocolVersion} no es compatible con el servidor {ServerVersion}, que acepta el rango [{MinimumProtocolVersion}, {CurrentProtocolVersion}].",
            string.IsNullOrWhiteSpace(clientVersion) ? "(desconocida)" : clientVersion,
            string.IsNullOrWhiteSpace(protocolVersion) ? "(desconocida)" : protocolVersion,
            ServerVersion.Current,
            _configuration.MinimumProtocolVersion,
            ServerVersion.ProtocolVersion);

        context.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
        context.Response.ContentType = "application/json; charset=utf-8";

        // La versión del servidor y el rango mínimo viajan como cabeceras: el cliente los
        // lee sin interpretar texto (AD-6).
        context.Response.Headers[ServerVersionHeaderName] = ServerVersion.Current;
        context.Response.Headers[MinimumProtocolVersionHeaderName] =
            _configuration.MinimumProtocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);

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
    /// Reads the protocol version that the request declares. A header that no llega, que
    /// llega vacía o que no lleva un entero positivo se lee como ausente: el cliente que
    /// no declara su versión no puede ser compatible con nadie (AD-6).
    /// </summary>
    /// <param name="value">Value of the header.</param>
    /// <param name="protocolVersion">The declared protocol version.</param>
    /// <returns><c>true</c> when the header carries a usable protocol version.</returns>
    private static bool TryReadProtocolVersion(string value, out int protocolVersion)
    {
        protocolVersion = 0;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!int.TryParse(
                value,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out protocolVersion)
            || protocolVersion < 1)
        {
            return false;
        }

        return true;
    }
}
