using LANPartyHub.Client.Application.Connection;
using LANPartyHub.Shared.Contracts;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The rule that turns the text of an address into the contract of the endpoint
/// (AC1, AC3, AD-3).
///
/// <para>
/// The tests fix the format <c>host:puerto</c> and the three cases that the player can
/// write wrong: an empty address, an address without a port and a port out of range. A
/// malformed address NEVER throws: it resolves into the error of the catalog with its
/// three parts and the internal code is never the primary text (AC3, UX-DR-36, AR-4).
/// </para>
/// </summary>
public sealed class ServerEndpointParserTests
{
    [Theory]
    [InlineData("192.168.1.20:5177", "192.168.1.20", 5177)]
    [InlineData("servidor:1", "servidor", 1)]
    [InlineData("servidor:65535", "servidor", 65535)]
    [InlineData("[::1]:8080", "[::1]", 8080)]
    public void An_address_that_is_a_host_and_a_port_is_the_endpoint(string value, string host, int port)
    {
        var parsed = ServerEndpointParser.TryParse(value, out var endpoint, out var error);

        Assert.True(parsed);
        Assert.NotNull(endpoint);
        Assert.Equal(host, endpoint!.Address);
        Assert.Equal(port, endpoint.Port);
        Assert.Null(error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_address_is_rejected_with_the_three_parts(string? value)
    {
        var parsed = ServerEndpointParser.TryParse(value, out var endpoint, out var error);

        Assert.False(parsed);
        Assert.Null(endpoint);
        Assert.NotNull(error);
        Assert.Equal(ErrorCatalog.ServerAddressInvalidCode, error!.Code);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
        Assert.False(string.IsNullOrWhiteSpace(error.Cause));
        Assert.False(string.IsNullOrWhiteSpace(error.NextStep));
    }

    [Theory]
    [InlineData("192.168.1.20")]
    [InlineData("192.168.1.20:")]
    [InlineData(":5177")]
    public void An_address_without_a_port_is_rejected(string value)
    {
        var parsed = ServerEndpointParser.TryParse(value, out var endpoint, out var error);

        Assert.False(parsed);
        Assert.Null(endpoint);
        Assert.NotNull(error);
        Assert.Equal(ErrorCatalog.ServerAddressInvalidCode, error!.Code);
    }

    [Theory]
    [InlineData("192.168.1.20:0")]
    [InlineData("192.168.1.20:65536")]
    [InlineData("192.168.1.20:70000")]
    [InlineData("192.168.1.20:abc")]
    public void A_port_out_of_range_is_rejected(string value)
    {
        var parsed = ServerEndpointParser.TryParse(value, out var endpoint, out var error);

        Assert.False(parsed);
        Assert.Null(endpoint);
        Assert.NotNull(error);
        Assert.Equal(ErrorCatalog.ServerAddressInvalidCode, error!.Code);
    }

    [Fact]
    public void The_code_of_the_error_is_never_the_primary_text()
    {
        var parsed = ServerEndpointParser.TryParse("no-es-una-direccion", out _, out var error);

        Assert.False(parsed);
        Assert.NotNull(error);
        Assert.NotEqual(error!.Code, error.Message);
        Assert.False(error.Message.StartsWith(error.Code, StringComparison.OrdinalIgnoreCase));
        Assert.False(error.Cause.Contains(error.Code, StringComparison.OrdinalIgnoreCase));
        Assert.False(error.NextStep.Contains(error.Code, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_code_of_the_error_belongs_to_the_catalog()
    {
        ServerEndpointParser.TryParse("no-es-una-direccion", out _, out var error);

        Assert.NotNull(error);
        Assert.Contains(error!.Code, ErrorCatalog.Codes);
    }

    [Fact]
    public void The_format_of_the_address_is_the_one_that_the_command_line_declares()
    {
        // La vía manual y los flags del arranque comparten UNA sola regla: el texto que
        // el jugador escribe y el valor de «--server-address» significan lo mismo (AC1,
        // AD-3).
        Assert.Equal("host:puerto", ServerEndpointParser.ServerAddressFormat);
        Assert.Equal(ServerEndpointParser.ServerAddressFormat, LANPartyHub.Client.Host.ClientCommandLine.ServerAddressFormat);
    }
}
