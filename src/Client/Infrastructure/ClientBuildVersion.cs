using System.Reflection;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Client.Infrastructure;

/// <summary>
/// Version of the binary of the client (AD-6, AD-18).
///
/// <para>
/// La versión del cliente es la del ensamblado que se está ejecutando, y no un valor
/// escrito a mano: el <c>&lt;Version&gt;</c> del proyecto la fija y el ensamblado la
/// lleva, así que la versión que el cliente declara NUNCA se desincroniza del binario que
/// la organización distribuye (AD-17).
/// </para>
/// <para>
/// La versión del protocolo es la constante del contrato compartido, de modo que el
/// cliente y el servidor no pueden discrepar sobre ella (AD-6, AD-18).
/// </para>
/// </summary>
public sealed class ClientBuildVersion : IClientVersionProvider
{
    /// <summary>Version of the binary of the client, as it is published.</summary>
    public static string Current =>
        typeof(ClientBuildVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(ClientBuildVersion).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";

    /// <inheritdoc />
    public string ClientVersion => Current;

    /// <inheritdoc />
    public int ProtocolVersion => VersionInfo.CurrentProtocolVersion;
}
