using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Infrastructure;
using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.IntegrationTests;

/// <summary>
/// Sondeo de las versiones del servidor que siempre declara al cliente compatible.
///
/// <para>
/// El destino de estas pruebas es un socket TCP en crudo, no el host HTTP del evento, así
/// que no hay servidor que pronuncie su veredicto: el sondeo no puede afirmar la
/// incompatibilidad y el cliente sigue su curso (AD-6, AC3).
/// </para>
/// </summary>
internal sealed class AlwaysCompatibleVersionProbe : IServerVersionProbe
{
    public Task<ServerVersionProbeResult> ProbeAsync(
        ServerEndpoint endpoint,
        TimeSpan timeout,
        CancellationToken cancellationToken) =>
        Task.FromResult(new ServerVersionProbeResult(
            Compatible: true,
            ServerVersion: "1.0.0",
            MinimumProtocolVersion: VersionInfo.CurrentProtocolVersion));
}

/// <summary>
/// Versiones que declara el cliente: la del binario real y la del contrato compartido
/// (AD-6).
/// </summary>
internal sealed class RealClientVersionProvider : IClientVersionProvider
{
    public string ClientVersion => ClientBuildVersion.Current;

    public int ProtocolVersion => VersionInfo.CurrentProtocolVersion;
}
