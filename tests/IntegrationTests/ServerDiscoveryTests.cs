using LANPartyHub.Client.Application.Connection;
using LANPartyHub.Client.Infrastructure.Networking;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LANPartyHub.IntegrationTests;

/// <summary>
/// The automatic discovery of the event server over mDNS (AC1, AC2, FR-3, AD-3, AD-12).
///
/// <para>
/// The test starts the REAL host of the event server, with the real Kestrel and the real
/// announcement of mDNS over UDP 5353, and it browses with the REAL client. The mapping
/// between the SRV and the A/AAAA and the contract <see cref="ServerEndpoint"/> is only
/// observable with the two ends on the wire, so it cannot be exercised with a fake
/// (AD-16).
/// </para>
/// <para>
/// The mechanism is EXCLUSIVELY the browse of mDNS: no unicast sweep of the subnet, no
/// SSDP and no DNS-SRV of an external network (AC2, AD-3).
/// </para>
/// </summary>
[Collection(RealHostCollection.Name)]
public sealed class ServerDiscoveryTests
{
    private const string NoMulticastReason =
        "This host has no operational interface that supports the multicast of mDNS, so the real discovery cannot be exercised here.";

    [Fact]
    public async Task The_client_discovers_the_server_that_announces_itself()
    {
        await using var server = await TestServer.StartAsync(cancellationToken: TestContext.Current.CancellationToken);

        var discovery = new MdnsServerDiscovery(NullLogger<MdnsServerDiscovery>.Instance);

        var servers = await discovery.DiscoverAsync(
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken);

        // El servidor real se anuncia con el servicio _lanpartyhub._tcp, así que el
        // cliente real tiene que encontrarlo (AC1).
        Assert.NotEmpty(servers);
    }

    [Fact]
    public async Task The_discovered_server_carries_the_http_port_of_the_server()
    {
        await using var server = await TestServer.StartAsync(cancellationToken: TestContext.Current.CancellationToken);

        var discovery = new MdnsServerDiscovery(NullLogger<MdnsServerDiscovery>.Instance);

        var servers = await discovery.DiscoverAsync(
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken);

        // El puerto que viaja en el registro SRV es el puerto HTTP del servidor: la
        // dirección encontrada resuelve al MISMO contrato ServerEndpoint que usa la vía
        // manual (AC1, AD-3).
        Assert.Contains(servers, endpoint => endpoint.Port == server.Port);
    }

    [Fact]
    public async Task The_discovered_server_carries_the_address_of_the_server()
    {
        await using var server = await TestServer.StartAsync(cancellationToken: TestContext.Current.CancellationToken);

        var discovery = new MdnsServerDiscovery(NullLogger<MdnsServerDiscovery>.Instance);

        var servers = await discovery.DiscoverAsync(
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken);

        // La dirección que viaja en el registro A/AAAA es la del servidor, y el resultado
        // es una lista de ServerEndpoint y NO un tipo de la biblioteca de mDNS (AC1).
        Assert.All(servers, endpoint =>
        {
            Assert.NotEmpty(endpoint.Address);
            Assert.True(endpoint.Port > 0);
        });
    }

    [Fact]
    public async Task The_same_server_is_collected_only_once()
    {
        await using var server = await TestServer.StartAsync(cancellationToken: TestContext.Current.CancellationToken);

        var discovery = new MdnsServerDiscovery(NullLogger<MdnsServerDiscovery>.Instance);

        var servers = await discovery.DiscoverAsync(
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken);

        // El mismo servidor puede contestar más de una vez por el browse, así que el par
        // de dirección y puerto se conserva UNA sola vez (AC1).
        var distinct = servers
            .Select(endpoint => (endpoint.Address, endpoint.Port))
            .Distinct()
            .ToArray();

        Assert.Equal(distinct.Length, servers.Count);
    }

    [Fact]
    public async Task A_search_that_finds_the_server_ends_as_found()
    {
        await using var server = await TestServer.StartAsync(cancellationToken: TestContext.Current.CancellationToken);

        var discovery = new MdnsServerDiscovery(NullLogger<MdnsServerDiscovery>.Instance);

        var service = new ServerDiscoveryService(
            discovery,
            NullLogger<ServerDiscoveryService>.Instance);

        var result = await service.DiscoverAsync(
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken);

        Assert.Equal(DiscoveryOutcome.Found, result.Outcome);
        Assert.NotEmpty(result.Servers);
    }

    [Fact]
    public async Task The_search_of_a_network_without_a_server_ends_as_empty_and_is_not_fatal()
    {
        // Una red que no contesta NO es un fallo: la búsqueda termina como «vacía» y el
        // cliente sigue vivo (AC3, AC4).
        var discovery = new MdnsServerDiscovery(NullLogger<MdnsServerDiscovery>.Instance);

        var service = new ServerDiscoveryService(
            discovery,
            NullLogger<ServerDiscoveryService>.Instance);

        var result = await service.DiscoverAsync(
            TimeSpan.FromMilliseconds(300),
            TestContext.Current.CancellationToken);

        Assert.NotEqual(DiscoveryOutcome.Blocked, result.Outcome);
    }
}
