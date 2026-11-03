using System.Net;
using System.Net.Sockets;
using LANPartyHub.Client.Application.Connection;
using LANPartyHub.Client.Infrastructure.Networking;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LANPartyHub.IntegrationTests;

/// <summary>
/// The connection with the event server by the address that the player typed
/// (AC1, AC2, AC3, FR-3, AD-3).
///
/// <para>
/// The test opens a REAL socket against a REAL server and it drives the REAL adapter of
/// the client, because the opening with a time limit and the mapping between the text of
/// the address and the contract <see cref="ServerEndpoint"/> are only observable with the
/// two ends on the wire, so they cannot be exercised with a fake (AD-16).
/// </para>
/// </summary>
[Collection(RealHostCollection.Name)]
public sealed class ManualConnectionTests
{
    [Fact]
    public async Task The_client_connects_with_the_address_that_the_player_types()
    {
        await using var listener = RealListener.Start();

        var connector = new TcpServerConnector(NullLogger<TcpServerConnector>.Instance);
        var service = new ServerConnectionService(
            connector,
            NullLogger<ServerConnectionService>.Instance);

        var attempt = await service.ConnectAsync(
            $"127.0.0.1:{listener.Port}",
            TestContext.Current.CancellationToken);

        await listener.AcceptAsync(TestContext.Current.CancellationToken);

        // La vía manual es PERMANENTE y equivalente: el cliente se conecta a la dirección
        // escrita y el estado pasa a conectado (AC1, AD-3).
        Assert.Equal(ConnectionState.AnchorUnverified, attempt.State);
        Assert.True(attempt.IsConnected);
        Assert.Null(attempt.Error);
        Assert.Equal(listener.Port, attempt.Endpoint!.Port);
    }

    [Fact]
    public async Task The_address_that_the_player_types_is_the_one_that_the_client_uses()
    {
        await using var listener = RealListener.Start();

        var connector = new TcpServerConnector(NullLogger<TcpServerConnector>.Instance);
        var service = new ServerConnectionService(
            connector,
            NullLogger<ServerConnectionService>.Instance);

        var attempt = await service.ConnectAsync(
            $"127.0.0.1:{listener.Port}",
            TestContext.Current.CancellationToken);

        await listener.AcceptAsync(TestContext.Current.CancellationToken);

        Assert.Equal("127.0.0.1", attempt.Endpoint!.Address);
    }

    [Fact]
    public async Task A_destination_that_does_not_answer_ends_as_disconnected_and_is_not_fatal()
    {
        // Un puerto que nadie escucha NO deja al jugador colgado: el tiempo límite vence,
        // el intento termina como desconectado y NO hay excepción (AC3).
        var port = GetUnusedPort();

        var connector = new TcpServerConnector(NullLogger<TcpServerConnector>.Instance);
        var service = new ServerConnectionService(
            connector,
            NullLogger<ServerConnectionService>.Instance);

        var attempt = await service.ConnectAsync(
            $"127.0.0.1:{port}",
            TimeSpan.FromMilliseconds(300),
            TestContext.Current.CancellationToken);

        Assert.Equal(ConnectionState.Disconnected, attempt.State);
        Assert.NotNull(attempt.Error);
        Assert.False(string.IsNullOrWhiteSpace(attempt.Error!.Message));
        Assert.False(string.IsNullOrWhiteSpace(attempt.Error.Cause));
        Assert.False(string.IsNullOrWhiteSpace(attempt.Error.NextStep));
    }

    [Fact]
    public async Task A_malformed_address_shows_the_three_parts_and_never_the_code()
    {
        var connector = new TcpServerConnector(NullLogger<TcpServerConnector>.Instance);
        var service = new ServerConnectionService(
            connector,
            NullLogger<ServerConnectionService>.Instance);

        var attempt = await service.ConnectAsync(
            "esto-no-es-una-direccion",
            TestContext.Current.CancellationToken);

        // La dirección mal formada se presenta con las tres partes obligatorias y el
        // código interno NO es el texto principal (AC3, UX-DR-36, AR-4).
        Assert.Equal(ConnectionState.Disconnected, attempt.State);
        Assert.NotNull(attempt.Error);
        Assert.Equal(ErrorCatalog.ServerAddressInvalidCode, attempt.Error!.Code);
        Assert.NotEqual(attempt.Error.Code, attempt.Error.Message);
        Assert.False(attempt.Error.Message.Contains(attempt.Error.Code, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task The_client_does_not_hang_when_the_destination_does_not_answer()
    {
        // El tiempo límite del conector se respeta: la conexión no se queda esperando
        // sin fin (AC3).
        var port = GetUnusedPort();

        var connector = new TcpServerConnector(NullLogger<TcpServerConnector>.Instance);
        var service = new ServerConnectionService(
            connector,
            NullLogger<ServerConnectionService>.Instance);

        var started = DateTimeOffset.UtcNow;

        var attempt = await service.ConnectAsync(
            $"127.0.0.1:{port}",
            TimeSpan.FromMilliseconds(300),
            TestContext.Current.CancellationToken);

        var elapsed = DateTimeOffset.UtcNow - started;

        Assert.Equal(ConnectionState.Disconnected, attempt.State);
        Assert.True(
            elapsed < TimeSpan.FromSeconds(10),
            $"La conexión sin destino tardó {elapsed} y no respetó el tiempo límite.");
    }

    private static int GetUnusedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);

        listener.Start();

        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>
    /// A real TCP listener that accepts the connection of the client and holds it open
    /// while the test runs.
    /// </summary>
    private sealed class RealListener : IAsyncDisposable
    {
        private readonly System.Net.Sockets.TcpListener _listener;
        private System.Net.Sockets.TcpClient? _accepted;

        private RealListener(System.Net.Sockets.TcpListener listener, int port)
        {
            _listener = listener;

            Port = port;
        }

        /// <summary>Port that the listener listens on.</summary>
        public int Port { get; }

        /// <summary>
        /// Starts a listener on the loopback, ready to accept the first connection.
        /// </summary>
        /// <returns>The listener, ready to accept.</returns>
        public static RealListener Start()
        {
            var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);

            listener.Start();

            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            return new RealListener(listener, port);
        }

        /// <summary>Accepts the connection after the client has started it.</summary>
        /// <param name="cancellationToken">Token that cancels the operation.</param>
        public async Task AcceptAsync(CancellationToken cancellationToken)
        {
            _accepted = await _listener.AcceptTcpClientAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            _accepted?.Dispose();
            _listener.Stop();

            return ValueTask.CompletedTask;
        }
    }
}
