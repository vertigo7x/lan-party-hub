using LANPartyHub.Client.Application.Ports;

namespace LANPartyHub.Client.Infrastructure.Networking;

/// <summary>
/// Handler that stamps the headers of correlation on every request to the surface of the
/// client of the event server (AD-15, AD-23).
///
/// <para>
/// The handler reads the identifiers from the correlation scope of the client and stamps
/// them on the request, so the client never sends a request without them: the identifier
/// of the team and the identifier of the unit of work in progress travel in the
/// <c>X-Client-Id</c> and the <c>X-Operation-Id</c> headers.
/// </para>
/// <para>
/// The handler does not know the address of the server and it does not open any
/// connection: the caller brings the client with its base address and installs the handler
/// in it.
/// </para>
/// </summary>
public sealed class CorrelationHeadersHandler : DelegatingHandler
{
    /// <summary>Header that carries the identifier of the client (AD-23).</summary>
    public const string ClientIdHeaderName = "X-Client-Id";

    /// <summary>Header that carries the identifier of the unit of work (AD-12).</summary>
    public const string OperationIdHeaderName = "X-Operation-Id";

    private readonly IClientRequestHeaders _headers;

    /// <summary>Creates the handler of the headers of correlation.</summary>
    /// <param name="headers">Source of the identifiers that the requests carry.</param>
    public CorrelationHeadersHandler(IClientRequestHeaders headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        _headers = headers;
    }

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Stamp(request, ClientIdHeaderName, _headers.ClientId);
        Stamp(request, OperationIdHeaderName, _headers.OperationId);

        return base.SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// Puts a header in the request. A header that arrives with no value is not stamped:
    /// the server rejects a request without the identifier of the client, and it is better
    /// to say that the client does not know it yet than to send an empty value.
    /// </summary>
    /// <param name="request">Request of the client.</param>
    /// <param name="name">Name of the header.</param>
    /// <param name="value">Value of the header.</param>
    private static void Stamp(HttpRequestMessage request, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        request.Headers.Remove(name);
        request.Headers.TryAddWithoutValidation(name, value);
    }
}
