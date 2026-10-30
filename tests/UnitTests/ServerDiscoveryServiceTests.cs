using LANPartyHub.Client.Application.Connection;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The automatic discovery of the event server (AC1, AC3, AC4, FR-3, NFR-9, AD-3).
///
/// <para>
/// The tests fix the OUTCOME of the search and its two ways of ending without a server: a
/// network that does not answer ends the search as «vacía» y una red que bloquea el
/// descubrimiento la termina como «bloqueada». Ninguna de las dos es un fallo fatal: la
/// dirección manual es una vía permanente y equivalente (AC3, AC4, AD-3).
/// </para>
/// </summary>
public sealed class ServerDiscoveryServiceTests
{
    [Fact]
    public async Task A_search_that_answers_with_a_server_is_found()
    {
        // El servidor contesta por mDNS y la dirección encontrada resuelve al MISMO
        // contrato ServerEndpoint que usa la vía manual (AC1, AD-3).
        var discovery = new FakeServerDiscovery(
        [
            new ServerEndpoint("192.168.1.20", 5177),
        ]);

        var service = CreateService(discovery);

        var result = await service.DiscoverAsync(TestContext.Current.CancellationToken);

        Assert.Equal(DiscoveryOutcome.Found, result.Outcome);
        Assert.Equal("192.168.1.20", Assert.Single(result.Servers).Address);
        Assert.Equal(5177, result.Servers[0].Port);
    }

    [Fact]
    public async Task A_search_that_answers_with_several_servers_returns_all_of_them()
    {
        var discovery = new FakeServerDiscovery(
        [
            new ServerEndpoint("192.168.1.20", 5177),
            new ServerEndpoint("192.168.1.21", 5178),
        ]);

        var service = CreateService(discovery);

        var result = await service.DiscoverAsync(TestContext.Current.CancellationToken);

        Assert.Equal(DiscoveryOutcome.Found, result.Outcome);
        Assert.Equal(2, result.Servers.Count);
    }

    [Fact]
    public async Task A_search_that_answers_with_nothing_ends_as_empty()
    {
        // Una red que no contesta NO es un fallo: la búsqueda termina como «vacía» y la
        // superficie ofrece la vía manual (AC3).
        var discovery = new FakeServerDiscovery([]);

        var service = CreateService(discovery);

        var result = await service.DiscoverAsync(TestContext.Current.CancellationToken);

        Assert.Equal(DiscoveryOutcome.Empty, result.Outcome);
        Assert.Empty(result.Servers);
    }

    [Fact]
    public async Task A_search_that_answers_with_nothing_after_the_time_limit_ends_as_empty()
    {
        // El caso del tiempo límite agotado: la búsqueda termina como «vacía» y NO como
        // bloqueada, porque la red no falló: simplemente no hay servidor (AC3).
        var discovery = new FakeServerDiscovery([]) { AnswersAfterTheTimeout = true };

        var service = CreateService(discovery);

        var result = await service.DiscoverAsync(
            TimeSpan.FromMilliseconds(50),
            TestContext.Current.CancellationToken);

        Assert.Equal(DiscoveryOutcome.Empty, result.Outcome);
        Assert.Empty(result.Servers);
    }

    [Fact]
    public async Task A_discovery_that_fails_ends_as_blocked_and_is_not_fatal()
    {
        // La política de la red puede bloquear el mDNS: el cliente NO falla de forma fatal
        // y la vía manual sigue operativa (AC4, AD-3).
        var discovery = new FakeServerDiscovery([]) { Fails = true };

        var service = CreateService(discovery);

        var result = await service.DiscoverAsync(TestContext.Current.CancellationToken);

        Assert.Equal(DiscoveryOutcome.Blocked, result.Outcome);
        Assert.Empty(result.Servers);
    }

    [Fact]
    public async Task The_search_passes_the_time_limit_to_the_port()
    {
        var discovery = new FakeServerDiscovery([]);

        var service = CreateService(discovery);

        await service.DiscoverAsync(
            TimeSpan.FromSeconds(7),
            TestContext.Current.CancellationToken);

        Assert.Equal(TimeSpan.FromSeconds(7), discovery.LastTimeout);
    }

    [Fact]
    public async Task The_default_time_limit_is_three_seconds()
    {
        var discovery = new FakeServerDiscovery([]);

        var service = CreateService(discovery);

        await service.DiscoverAsync(TestContext.Current.CancellationToken);

        Assert.Equal(TimeSpan.FromSeconds(3), discovery.LastTimeout);
    }

    private static ServerDiscoveryService CreateService(FakeServerDiscovery discovery) =>
        new(discovery, NullLogger<ServerDiscoveryService>.Instance);

    /// <summary>
    /// Port of the search of the servers of the event, with the behaviour that the tests
    /// need.
    /// </summary>
    private sealed class FakeServerDiscovery : IServerDiscovery
    {
        private readonly IReadOnlyList<ServerEndpoint> _servers;

        public FakeServerDiscovery(IReadOnlyList<ServerEndpoint> servers)
        {
            _servers = servers;
        }

        /// <summary>Whether the port throws, as a network that blocks the discovery does.</summary>
        public bool Fails { get; init; }

        /// <summary>
        /// Whether the port answers AFTER the time limit, as a network that does not
        /// answer does.
        /// </summary>
        public bool AnswersAfterTheTimeout { get; init; }

        /// <summary>The time limit that the use case passed to the port.</summary>
        public TimeSpan? LastTimeout { get; private set; }

        public async Task<IReadOnlyList<ServerEndpoint>> DiscoverAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            LastTimeout = timeout;

            if (Fails)
            {
                throw new InvalidOperationException("The network blocked the multicast.");
            }

            if (AnswersAfterTheTimeout)
            {
                await Task.Delay(timeout + TimeSpan.FromSeconds(30), cancellationToken);

                return _servers;
            }

            return _servers;
        }
    }
}
