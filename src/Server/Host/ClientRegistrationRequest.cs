using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Server.Host;

/// <summary>
/// Body of the registration of a client of the event (AD-32).
///
/// <para>
/// The body carries the EXACT shape <c>{ clientId, machineName, clientVersion,
/// protocolVersion }</c>. The <c>machineName</c> is the PROPOSED name: the server
/// validates it and, when it collides with the one of another team, resolves the
/// conflict with a derived unique name and notifies it in the response (AD-30,
/// AD-31).
/// </para>
/// <para>
/// The versions travel in the body AND in the headers <c>X-Client-Version</c> and
/// <c>X-Protocol-Version</c>, and they are the SAME values: the body does not open a
/// second source of truth (AD-6, AD-32).
/// </para>
/// <para>
/// El cuerpo NO lleva <c>schemaVersion</c>: el registro es la ÚNICA mutación exenta
/// de la versión del esquema, y la exención es nominal y acotada, no una puerta
/// abierta (AD-5, AD-32).
/// </para>
/// <para>
/// El cuerpo NO lleva tampoco el estado de conexión: la FORMA del cuerpo es EXACTA y
/// el estado de conexión lo publica el cliente en el evento <c>ClientStateChanged</c>
/// del hub, que es el canal sancionado para el estado del cliente (AD-18, AD-29).
/// </para>
/// <para>
/// The body is a type of the server and not a contract of <c>Shared</c>: AD-18 fixes
/// exhaustively the contracts of <c>Shared</c> and this body is consumed only by the
/// server, never by the client of the LAN.
/// </para>
/// </summary>
public sealed class ClientRegistrationRequest
{
    /// <summary>
    /// Canonical identifier of the client: the UUID v7 that the client emits once and
    /// persists (AD-23).
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Name proposed by the client for its team. The server validates it, makes it
    /// unique and answers with the effective name (AD-30).
    /// </summary>
    public string? MachineName { get; set; }

    /// <summary>Version of the binary of the client.</summary>
    public string? ClientVersion { get; set; }

    /// <summary>Protocol version that the client speaks.</summary>
    public int ProtocolVersion { get; set; }
}
