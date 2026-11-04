using LANPartyHub.Client.Application.Ports;

namespace LANPartyHub.Client.Infrastructure.Networking;

/// <summary>
/// Handler that stamps the headers of every request to the surface of the client of the
/// event server (AD-6, AD-15, AD-23).
///
/// <para>
/// El manejador es el ÚNICO sitio que estampa cabeceras: la correlación y las versiones
/// viajan juntas en <c>X-Client-Id</c>, <c>X-Operation-Id</c>, <c>X-Client-Version</c> y
/// <c>X-Protocol-Version</c>, así que el cliente NUNCA envía una petición sin declarar su
/// versión (AD-6).
/// </para>
/// <para>
/// La versión del cliente es una DECLARACIÓN y no una credencial: el servidor la compara
/// con la política que publica y no habilita nada más (AD-6, AD-12, R-MVP-2).
/// </para>
/// <para>
/// El manejador no conoce la dirección del servidor y no abre ninguna conexión: el
/// llamante trae el cliente con su dirección base e instala el manejador en él.
/// </para>
/// </summary>
public sealed class ClientRequestHeadersHandler : DelegatingHandler
{
    /// <summary>Header that carries the identifier of the client (AD-23).</summary>
    public const string ClientIdHeaderName = "X-Client-Id";

    /// <summary>Header that carries the identifier of the unit of work (AD-12).</summary>
    public const string OperationIdHeaderName = "X-Operation-Id";

    /// <summary>Header that carries the version of the binary of the client (AD-6).</summary>
    public const string ClientVersionHeaderName = "X-Client-Version";

    /// <summary>Header that carries the version of the protocol that the client speaks (AD-6).</summary>
    public const string ProtocolVersionHeaderName = "X-Protocol-Version";

    /// <summary>
    /// Header that carries the human label of the team (AD-14, AD-30).
    ///
    /// <para>
    /// La etiqueta de la máquina NO es una credencial: es el nombre humano del equipo, y el
    /// servidor la usa para el reparto equitativo del enlace, porque la UNIDAD de reparto
    /// es el cliente y no la conexión (AD-14, AD-30).
    /// </para>
    /// </summary>
    public const string MachineNameHeaderName = "X-Client-Machine-Name";

    private readonly IClientRequestHeaders _headers;
    private readonly IClientVersionProvider _versions;

    /// <summary>Creates the handler of the headers of the requests.</summary>
    /// <param name="headers">Source of the identifiers that the requests carry.</param>
    /// <param name="versions">Source of the versions that the requests declare.</param>
    public ClientRequestHeadersHandler(
        IClientRequestHeaders headers,
        IClientVersionProvider versions)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(versions);

        _headers = headers;
        _versions = versions;
    }

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Stamp(request, ClientIdHeaderName, _headers.ClientId);
        Stamp(request, OperationIdHeaderName, _headers.OperationId);

        // AD-14, AD-30: la etiqueta de la máquina es el nombre humano del equipo, y el
        // servidor la usa para distinguir a los clientes en el reparto del enlace. No es
        // una credencial: no habilita nada.
        Stamp(request, MachineNameHeaderName, _headers.MachineName);

        // AD-6: TODA petición declara la versión del cliente y la versión del protocolo,
        // sin excepción: el servidor rechaza con 426 la que no las declara.
        Stamp(request, ClientVersionHeaderName, _versions.ClientVersion);
        Stamp(request, ProtocolVersionHeaderName, _versions.ProtocolVersion.ToString(
            System.Globalization.CultureInfo.InvariantCulture));

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
