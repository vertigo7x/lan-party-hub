using LANPartyHub.Client.Infrastructure.Networking;
using LANPartyHub.Server.Infrastructure.Networking;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LANPartyHub.IntegrationTests;

/// <summary>
/// SPIKE S-1 · descubrimiento por mDNS (NO BLOQUEANTE, AD-21, R-MVP-5).
///
/// <para>
/// El criterio S-1a solo es observable en una RED REAL: por eso esta prueba usa el
/// anuncio y el navegador de verdad, sobre la red de la maquina (S-1a).
/// </para>
/// <para>
/// El spike S-1 es NO BLOQUEANTE: su resultado es una decision de fase posterior y no
/// cambia ningun artefacto del MVP, porque la via manual basta (AD-3, AD-21).
/// </para>
/// </summary>
public sealed class MdnsDiscoveryTests
{
    /// <summary>
    /// The client DISCOVERS the server without the manual address (S-1a).
    /// </summary>
    [Fact]
    public async Task The_client_discovers_the_server_without_the_manual_address()
    {
        const int puerto = 47891;

        await using var publisher = new HaukcodeMdnsPublisher(
            "spike-s1-" + Environment.MachineName,
            MdnsServiceAdvertiser.ServiceType,
            puerto,
            MdnsServiceAdvertiser.TxtProperties);

        await using var advertiser = new MdnsServiceAdvertiser(
            NullLogger<MdnsServiceAdvertiser>.Instance,
            publisher);

        advertiser.Start();

        var discovery = new MdnsServerDiscovery(NullLogger<MdnsServerDiscovery>.Instance);

        var encontrados = await discovery.DiscoverAsync(
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken);

        // El servidor se descubre por mDNS, SIN que el jugador escriba la direccion
        // (S-1a).
        Assert.Contains(encontrados, endpoint => endpoint.Port == puerto);
    }

    /// <summary>
    /// The discovery of a service that is NOT published returns nothing, para que el
    /// criterio S-1a no se pueda cumplir por accidente (S-1a, AD-21).
    /// </summary>
    [Fact]
    public async Task The_discovery_of_a_service_that_is_not_published_returns_nothing()
    {
        var discovery = new MdnsServerDiscovery(NullLogger<MdnsServerDiscovery>.Instance);

        var encontrados = await discovery.DiscoverAsync(
            TimeSpan.FromSeconds(3),
            TestContext.Current.CancellationToken);

        // Sin anuncio, el descubrimiento NO inventa servidores (AD-27: lo que no se
        // anuncia no se descubre).
        Assert.DoesNotContain(encontrados, endpoint => endpoint.Port == 47892);
    }
}
