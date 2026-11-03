using System.Reflection;
using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Server.Host;

/// <summary>
/// Version of the binary of the event server (AD-6, AD-18).
///
/// <para>
/// The version of the server travels in the <c>X-Server-Version</c> header of every
/// response and en la ruta <c>GET /api/v1/server/info</c>, so a client that no es
/// compatible puede leer la versión que necesita sin tener que interpretar texto
/// (AD-6).
/// </para>
/// <para>
/// The version comes from the assembly of the server, which is the single origin of the
/// version of the binario: el <c>&lt;Version&gt;</c> del proyecto la fija y la
/// ensamblación la lleva, así que el valor que se publica NO se puede desincronizar del
/// binario que se distribuye (AD-17).
/// </para>
/// </summary>
public static class ServerVersion
{
    /// <summary>
    /// Version of the binary of the server, as it is published.
    /// </summary>
    public static string Current =>
        typeof(ServerVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(ServerVersion).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";

    /// <summary>
    /// Protocol version that the server speaks (AD-6). It is the constant of the shared
    /// contract, so the client and the server can NEVER disagree about it (AD-6, AD-18).
    /// </summary>
    public static int ProtocolVersion => VersionInfo.CurrentProtocolVersion;
}
