namespace LANPartyHub.Client.Application.Ports;

/// <summary>
/// Version of the client that every request to the event server declares (AD-6, FR-4).
///
/// <para>
/// The port is declared in the Application layer and it is implemented by the build of
/// the client. The adapter that sends the requests reads the versions from it and stamps
/// them on every request, so el cliente NUNCA envía una petición sin declarar su
/// versión (AD-6).
/// </para>
/// <para>
/// La versión del cliente NO es una credencial: es una declaración que el servidor
/// compara con la política que él publica, y el único control de acceso del protocolo
/// sigue siendo la compatibilidad de versión (AD-6, AD-12, R-MVP-2).
/// </para>
/// </summary>
public interface IClientVersionProvider
{
    /// <summary>
    /// Version of the binary of the client, as it is declared in the
    /// <c>X-Client-Version</c> header.
    /// </summary>
    string ClientVersion { get; }

    /// <summary>
    /// Version of the protocol that the client speaks, as it is declared in the
    /// <c>X-Protocol-Version</c> header (AD-6).
    /// </summary>
    int ProtocolVersion { get; }
}
