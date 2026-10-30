using LANPartyHub.Client.Application.Connection;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The connection with the event server by the address that the player typed
/// (AC1, AC3, FR-3, AD-3, AD-27).
///
/// <para>
/// The tests fix the RESULT of the attempt: a connection that opened leaves the client
/// connected with the anchor still pending, and a failure NEVER travels as an exception:
/// it ends the attempt as disconnected with the three part message of the error (AC1,
/// AC3, UX-DR-36).
/// </para>
/// </summary>
public sealed class ServerConnectionServiceTests
{
    [Fact]
    public async Task A_connection_that_opens_leaves_the_anchor_pending()
    {
        // Conectar NO fija el ancla de confianza: el estado es «Ancla sin verificar» y
        // nunca «Ancla verificada» (AD-6, AD-27).
        var connector = new FakeServerConnector();

        var service = CreateService(connector);

        var attempt = await service.ConnectAsync(
            "192.168.1.20:5177",
            TestContext.Current.CancellationToken);

        Assert.Equal(ConnectionState.AnchorUnverified, attempt.State);
        Assert.True(attempt.IsConnected);
        Assert.Null(attempt.Error);
    }

    [Fact]
    public async Task A_connection_that_opens_carries_the_endpoint_that_the_address_described()
    {
        var connector = new FakeServerConnector();

        var service = CreateService(connector);

        var attempt = await service.ConnectAsync(
            "192.168.1.20:5177",
            TestContext.Current.CancellationToken);

        Assert.NotNull(attempt.Endpoint);
        Assert.Equal("192.168.1.20", attempt.Endpoint!.Address);
        Assert.Equal(5177, attempt.Endpoint.Port);
    }

    [Fact]
    public async Task A_connection_that_opens_reaches_the_port_with_the_endpoint()
    {
        var connector = new FakeServerConnector();

        var service = CreateService(connector);

        await service.ConnectAsync(
            "192.168.1.20:5177",
            TestContext.Current.CancellationToken);

        Assert.NotNull(connector.LastEndpoint);
        Assert.Equal("192.168.1.20", connector.LastEndpoint!.Address);
        Assert.Equal(5177, connector.LastEndpoint.Port);
    }

    [Fact]
    public async Task A_destination_that_does_not_answer_ends_as_disconnected_and_is_not_fatal()
    {
        // El destino no responde: el intento termina como desconectado y la excepción NO
        // viaja al jugador (AC3).
        var connector = new FakeServerConnector() { Fails = true };

        var service = CreateService(connector);

        var attempt = await service.ConnectAsync(
            "192.168.1.20:5177",
            TestContext.Current.CancellationToken);

        Assert.Equal(ConnectionState.Disconnected, attempt.State);
        Assert.False(attempt.IsConnected);
        Assert.NotNull(attempt.Error);
        Assert.NotNull(attempt.Endpoint);
        Assert.Equal(5177, attempt.Endpoint!.Port);
    }

    [Fact]
    public async Task A_destination_that_does_not_answer_shows_the_three_parts()
    {
        var connector = new FakeServerConnector() { Fails = true };

        var service = CreateService(connector);

        var attempt = await service.ConnectAsync(
            "192.168.1.20:5177",
            TestContext.Current.CancellationToken);

        Assert.NotNull(attempt.Error);
        Assert.False(string.IsNullOrWhiteSpace(attempt.Error!.Message));
        Assert.False(string.IsNullOrWhiteSpace(attempt.Error.Cause));
        Assert.False(string.IsNullOrWhiteSpace(attempt.Error.NextStep));
        Assert.NotEqual(attempt.Error.Code, attempt.Error.Message);
    }

    [Fact]
    public async Task A_malformed_address_never_reaches_the_network()
    {
        // Una dirección mal formada NO abre ningún socket: el intento termina como
        // desconectado con el error del catálogo (AC3, AR-4).
        var connector = new FakeServerConnector();

        var service = CreateService(connector);

        var attempt = await service.ConnectAsync(
            "no-es-una-direccion",
            TestContext.Current.CancellationToken);

        Assert.Equal(ConnectionState.Disconnected, attempt.State);
        Assert.Null(attempt.Endpoint);
        Assert.NotNull(attempt.Error);
        Assert.Equal(ErrorCatalog.ServerAddressInvalidCode, attempt.Error!.Code);
        Assert.Null(connector.LastEndpoint);
    }

    [Fact]
    public async Task A_malformed_address_shows_the_three_parts_and_not_the_code()
    {
        var connector = new FakeServerConnector();

        var service = CreateService(connector);

        var attempt = await service.ConnectAsync(
            "192.168.1.20",
            TestContext.Current.CancellationToken);

        Assert.NotNull(attempt.Error);
        Assert.False(string.IsNullOrWhiteSpace(attempt.Error!.Message));
        Assert.False(string.IsNullOrWhiteSpace(attempt.Error.Cause));
        Assert.False(string.IsNullOrWhiteSpace(attempt.Error.NextStep));
        Assert.NotEqual(attempt.Error.Code, attempt.Error.Message);
        Assert.False(attempt.Error.Message.Contains(attempt.Error.Code, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task The_connection_passes_the_time_limit_to_the_port()
    {
        var connector = new FakeServerConnector();

        var service = CreateService(connector);

        await service.ConnectAsync(
            "192.168.1.20:5177",
            TimeSpan.FromSeconds(7),
            TestContext.Current.CancellationToken);

        Assert.Equal(TimeSpan.FromSeconds(7), connector.LastTimeout);
    }

    [Fact]
    public async Task The_default_time_limit_is_three_seconds()
    {
        var connector = new FakeServerConnector();

        var service = CreateService(connector);

        await service.ConnectAsync(
            "192.168.1.20:5177",
            TestContext.Current.CancellationToken);

        Assert.Equal(TimeSpan.FromSeconds(3), connector.LastTimeout);
    }

    private static ServerConnectionService CreateService(FakeServerConnector connector) =>
        new(connector, NullLogger<ServerConnectionService>.Instance);

    /// <summary>
    /// Port of the opening of the connection with the server, with the behaviour that the
    /// tests need.
    /// </summary>
    private sealed class FakeServerConnector : IServerConnector
    {
        /// <summary>Whether the port throws, as a destination that does not answer does.</summary>
        public bool Fails { get; init; }

        /// <summary>The endpoint that the use case passed to the port.</summary>
        public ServerEndpoint? LastEndpoint { get; private set; }

        /// <summary>The time limit that the use case passed to the port.</summary>
        public TimeSpan? LastTimeout { get; private set; }

        public Task ConnectAsync(
            ServerEndpoint endpoint,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            LastEndpoint = endpoint;
            LastTimeout = timeout;

            if (Fails)
            {
                throw new InvalidOperationException("The destination did not answer.");
            }

            return Task.CompletedTask;
        }
    }
}
