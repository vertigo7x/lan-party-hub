using System.Net;
using LANPartyHub.Server.Infrastructure.Networking;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// Classification of the addresses the server is allowed to listen on (AD-12).
///
/// The predicate is checked on its own, with the boundary addresses of every range, so a
/// regression that starts accepting a public address cannot hide behind the resolver.
/// </summary>
public sealed class PrivateAddressResolverTests
{
    [Theory]
    [InlineData("10.0.0.1", true)]
    [InlineData("10.255.255.255", true)]
    [InlineData("172.15.0.1", false)]
    [InlineData("172.16.0.1", true)]
    [InlineData("172.31.255.255", true)]
    [InlineData("172.32.0.1", false)]
    [InlineData("192.168.0.1", true)]
    [InlineData("192.169.0.1", false)]
    [InlineData("169.254.1.1", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("127.0.0.1", false)]
    [InlineData("8.8.8.8", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("::1", false)]
    [InlineData("fc00::1", true)]
    [InlineData("fd12:3456::1", true)]
    [InlineData("fe80::1", false)]
    [InlineData("ff02::1", false)]
    [InlineData("2001:4860:4860::8888", false)]
    public void An_address_is_private_only_when_it_belongs_to_a_private_range(string address, bool expected)
    {
        Assert.Equal(expected, PrivateAddressResolver.IsPrivate(IPAddress.Parse(address)));
    }

    [Fact]
    public void The_resolver_returns_only_private_addresses_and_never_repeats_one()
    {
        var addresses = new PrivateAddressResolver().GetPrivateAddresses();

        Assert.Equal(addresses.Distinct().Count(), addresses.Count);
        Assert.All(addresses, address => Assert.True(
            PrivateAddressResolver.IsPrivate(address),
            $"The resolver returned an address that is not private: {address}."));
    }
}
