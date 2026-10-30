using LANPartyHub.Client.Application.Connection;
using LANPartyHub.Client.Presentation.Connection;
using LANPartyHub.Shared.Contracts;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The surface of the connection of the player (AC1, AC3, AC4, UX-DR-30).
///
/// <para>
/// The tests fix the microcopy que la historia exige: el texto «No se encontró ningún
/// servidor en esta red.» y la acción primaria «Introducir dirección manualmente». La
/// acción manual se conserva también cuando la red bloquea el descubrimiento, porque la
/// vía manual es permanente y equivalente (AC3, AC4, AD-3).
/// </para>
/// </summary>
public sealed class ConnectionSurfaceViewModelTests
{
    [Fact]
    public void A_network_without_a_server_shows_the_empty_text()
    {
        var viewModel = new ConnectionSurfaceViewModel();

        viewModel.ApplyDiscovery(DiscoveryResult.Empty());

        Assert.Equal(
            "No se encontró ningún servidor en esta red.",
            viewModel.EmptyMessage);
    }

    [Fact]
    public void A_network_without_a_server_offers_the_manual_address_as_the_primary_action()
    {
        var viewModel = new ConnectionSurfaceViewModel();

        viewModel.ApplyDiscovery(DiscoveryResult.Empty());

        Assert.Equal("Introducir dirección manualmente", viewModel.PrimaryActionText);
    }

    [Fact]
    public void A_blocked_discovery_keeps_the_manual_address()
    {
        // La red bloquea el mDNS: el cliente NO falla de forma fatal y la vía manual sigue
        // operativa, así que la acción primaria se conserva (AC4, AD-3).
        var viewModel = new ConnectionSurfaceViewModel();

        viewModel.ApplyDiscovery(DiscoveryResult.Blocked());

        Assert.True(viewModel.IsDiscoveryBlocked);
        Assert.Equal("Introducir dirección manualmente", viewModel.PrimaryActionText);
        Assert.True(viewModel.ShowsEmptyState);
    }

    [Fact]
    public void A_discovery_with_a_server_shows_the_server_and_no_empty_state()
    {
        var viewModel = new ConnectionSurfaceViewModel();

        viewModel.ApplyDiscovery(DiscoveryResult.Found(
        [
            new ServerEndpoint("192.168.1.20", 5177),
        ]));

        Assert.True(viewModel.HasServers);
        Assert.False(viewModel.ShowsEmptyState);
        Assert.Equal("192.168.1.20", Assert.Single(viewModel.Servers).Address);
        Assert.Equal(5177, viewModel.Servers[0].Port);
    }

    [Fact]
    public void A_discovery_with_a_server_does_not_show_the_empty_text()
    {
        var viewModel = new ConnectionSurfaceViewModel();

        viewModel.ApplyDiscovery(DiscoveryResult.Found(
        [
            new ServerEndpoint("192.168.1.20", 5177),
        ]));

        Assert.NotEqual("No se encontró ningún servidor en esta red.", viewModel.EmptyMessage);
    }

    [Fact]
    public void A_search_that_finds_nothing_after_finding_servers_empties_the_list()
    {
        var viewModel = new ConnectionSurfaceViewModel();

        viewModel.ApplyDiscovery(DiscoveryResult.Found(
        [
            new ServerEndpoint("192.168.1.20", 5177),
        ]));

        viewModel.ApplyDiscovery(DiscoveryResult.Empty());

        Assert.False(viewModel.HasServers);
        Assert.True(viewModel.ShowsEmptyState);
    }

    [Fact]
    public void The_surface_raises_the_change_when_the_outcome_arrives()
    {
        var viewModel = new ConnectionSurfaceViewModel();
        var changes = 0;

        viewModel.Changed += () => changes++;

        viewModel.ApplyDiscovery(DiscoveryResult.Empty());

        Assert.Equal(1, changes);
    }
}
