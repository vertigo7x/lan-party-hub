using LANPartyHub.Client.Application.Connection;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Domain.Trust;
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

    [Fact]
    public async Task A_client_that_the_server_rejects_ends_as_disconnected()
    {
        // AD-6, FR-4: un cliente incompatible NO opera: el intento termina como
        // desconectado y no hay conexión con la que sincronizar, descargar o instalar.
        var connector = new FakeServerConnector();

        var service = CreateService(connector, new FakeServerVersionProbe { Incompatible = true });

        var attempt = await service.ConnectAsync(
            "192.168.1.20:5177",
            TestContext.Current.CancellationToken);

        Assert.Equal(ConnectionState.Disconnected, attempt.State);
        Assert.False(attempt.IsConnected);
        Assert.True(attempt.IsVersionIncompatible);
    }

    [Fact]
    public async Task A_client_that_the_server_rejects_carries_the_version_of_the_client_and_the_server()
    {
        // AD-6: el diagnóstico lleva la versión del cliente y la del servidor.
        var connector = new FakeServerConnector();

        var service = CreateService(connector, new FakeServerVersionProbe { Incompatible = true });

        var attempt = await service.ConnectAsync(
            "192.168.1.20:5177",
            TestContext.Current.CancellationToken);

        Assert.Equal("1.2.0", attempt.IncompatibleClientVersion);
        Assert.Equal("1.4.0", attempt.IncompatibleServerVersion);
    }

    [Fact]
    public async Task A_client_that_the_server_rejects_carries_the_error_of_the_catalog()
    {
        var connector = new FakeServerConnector();

        var service = CreateService(connector, new FakeServerVersionProbe { Incompatible = true });

        var attempt = await service.ConnectAsync(
            "192.168.1.20:5177",
            TestContext.Current.CancellationToken);

        Assert.NotNull(attempt.Error);
        Assert.Equal(ErrorCatalog.ClientVersionIncompatibleCode, attempt.Error!.Code);
        Assert.False(string.IsNullOrWhiteSpace(attempt.Error.Message));
        Assert.False(string.IsNullOrWhiteSpace(attempt.Error.Cause));
        Assert.False(string.IsNullOrWhiteSpace(attempt.Error.NextStep));
    }

    [Fact]
    public async Task A_compatible_client_probes_the_versions_of_the_server()
    {
        var connector = new FakeServerConnector();
        var probe = new FakeServerVersionProbe();

        var service = CreateService(connector, probe);

        await service.ConnectAsync(
            "192.168.1.20:5177",
            TestContext.Current.CancellationToken);

        Assert.NotNull(probe.LastEndpoint);
        Assert.Equal("192.168.1.20", probe.LastEndpoint!.Address);
        Assert.Equal(5177, probe.LastEndpoint.Port);
    }

    [Fact]
    public async Task A_client_without_a_fixed_anchor_leaves_the_anchor_unverified()
    {
        // AC7, AD-27: sin ancla fijada, el estado es «Ancla sin verificar» y el cliente no
        // opera. La conexión NO se degrada a «conectado» por el hecho de abrir el socket.
        var connector = new FakeServerConnector();

        var service = CreateService(
            connector,
            new FakeServerVersionProbe(),
            new FakeTrustAnchorReader { Fixed = null },
            new FakeServerTrustAnchorProbe { Presented = GoldenAnchor() });

        var attempt = await service.ConnectAsync(
            "192.168.1.20:5177",
            TestContext.Current.CancellationToken);

        Assert.Equal(ConnectionState.AnchorUnverified, attempt.State);
        Assert.Null(attempt.Error);
    }

    [Fact]
    public async Task A_fixed_anchor_that_matches_the_server_keeps_the_connection()
    {
        // AD-27: con el ancla fijada y validada contra el valor persistido, la conexión no
        // se rechaza.
        var connector = new FakeServerConnector();

        var service = CreateService(
            connector,
            new FakeServerVersionProbe(),
            new FakeTrustAnchorReader { Fixed = GoldenPinnedAnchor() },
            new FakeServerTrustAnchorProbe { Presented = GoldenAnchor() });

        var attempt = await service.ConnectAsync(
            "192.168.1.20:5177",
            TestContext.Current.CancellationToken);

        Assert.Null(attempt.Error);
        Assert.False(attempt.IsAnchorMismatch);
    }

    [Fact]
    public async Task A_fixed_anchor_that_does_not_match_the_server_is_rejected()
    {
        // AC6, AD-27: un ancla fijada es INMUTABLE. Una clave distinta se RECHAZA.
        var connector = new FakeServerConnector();

        var service = CreateService(
            connector,
            new FakeServerVersionProbe(),
            new FakeTrustAnchorReader { Fixed = GoldenPinnedAnchor() },
            new FakeServerTrustAnchorProbe { Presented = OtherAnchor() });

        var attempt = await service.ConnectAsync(
            "192.168.1.20:5177",
            TestContext.Current.CancellationToken);

        Assert.Equal(ConnectionState.Disconnected, attempt.State);
        Assert.True(attempt.IsAnchorMismatch);
        Assert.NotNull(attempt.Error);
        Assert.NotNull(attempt.Endpoint);
    }

    [Fact]
    public async Task A_rejected_anchor_carries_the_anchor_mismatch_code()
    {
        // AC2, AD-27: el `code` del diagnóstico es el de «fingerprint de ancla no
        // coincide», DISTINTO del de «firma inválida».
        var connector = new FakeServerConnector();

        var service = CreateService(
            connector,
            new FakeServerVersionProbe(),
            new FakeTrustAnchorReader { Fixed = GoldenPinnedAnchor() },
            new FakeServerTrustAnchorProbe { Presented = OtherAnchor() });

        var attempt = await service.ConnectAsync(
            "192.168.1.20:5177",
            TestContext.Current.CancellationToken);

        Assert.NotNull(attempt.Error);
        Assert.Equal(ErrorCatalog.AnchorFingerprintMismatchCode, attempt.Error!.Code);
        Assert.NotEqual(ErrorCatalog.ManifestSignatureInvalidCode, attempt.Error.Code);
    }

    [Fact]
    public async Task A_rejected_anchor_carries_both_fingerprints()
    {
        // AC6: la superficie muestra el fingerprint que presentó el servidor y el que ya
        // está fijado.
        var connector = new FakeServerConnector();

        var service = CreateService(
            connector,
            new FakeServerVersionProbe(),
            new FakeTrustAnchorReader { Fixed = GoldenPinnedAnchor() },
            new FakeServerTrustAnchorProbe { Presented = OtherAnchor() });

        var attempt = await service.ConnectAsync(
            "192.168.1.20:5177",
            TestContext.Current.CancellationToken);

        Assert.NotNull(attempt.PresentedAnchorFingerprint);
        Assert.NotNull(attempt.FixedAnchorFingerprint);
        Assert.NotEqual(
            attempt.PresentedAnchorFingerprint,
            attempt.FixedAnchorFingerprint);
    }

    private static TrustAnchor GoldenAnchor() =>
        new(
            AnchorFingerprintTests.GoldenPublicKeyBase64,
            AnchorFingerprintTests.GoldenFingerprint);

    private static TrustAnchor OtherAnchor() =>
        new(
            Convert.ToBase64String(new byte[32]),
            new string('a', 64));

    private static PinnedTrustAnchor GoldenPinnedAnchor() =>
        new(
            AnchorFingerprintTests.GoldenPublicKeyBase64,
            AnchorFingerprintTests.GoldenFingerprint,
            "2026-11-03T09:00:00.0000000Z");

    private static ServerConnectionService CreateService(FakeServerConnector connector) =>
        CreateService(connector, new FakeServerVersionProbe());

    private static ServerConnectionService CreateService(
        FakeServerConnector connector,
        FakeServerVersionProbe probe) =>
        new(
            connector,
            probe,
            new FakeClientVersionProvider(),
            anchorReader: null,
            anchorProbe: null,
            NullLogger<ServerConnectionService>.Instance);

    private static ServerConnectionService CreateService(
        FakeServerConnector connector,
        FakeServerVersionProbe probe,
        FakeTrustAnchorReader anchorReader,
        FakeServerTrustAnchorProbe anchorProbe) =>
        new(
            connector,
            probe,
            new FakeClientVersionProvider(),
            anchorReader,
            anchorProbe,
            NullLogger<ServerConnectionService>.Instance);

    /// <summary>
    /// Ancla de confianza que el cliente ya tiene fijada (AD-27).
    /// </summary>
    private sealed class FakeTrustAnchorReader : ITrustAnchorReader
    {
        /// <summary>The ancla that the client has fixed, when it has one.</summary>
        public PinnedTrustAnchor? Fixed { get; init; }

        public Task<PinnedTrustAnchor?> LoadTrustAnchorAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Fixed);
    }

    /// <summary>
    /// Sonda del ancla que el servidor presenta (AD-27).
    /// </summary>
    private sealed class FakeServerTrustAnchorProbe : IServerTrustAnchorProbe
    {
        /// <summary>The ancla that the server presents.</summary>
        public TrustAnchor? Presented { get; init; }

        public Task<ServerTrustAnchorProbeResult> ProbeAsync(
            ServerEndpoint endpoint,
            TimeSpan timeout,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ServerTrustAnchorProbeResult(Presented, Reachable: true));
    }

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

    /// <summary>
    /// Sondeo de las versiones del servidor, con el comportamiento que los tests
    /// necesitan (AD-6).
    /// </summary>
    private sealed class FakeServerVersionProbe : IServerVersionProbe
    {
        /// <summary>Whether the server rejects the client as incompatible.</summary>
        public bool Incompatible { get; init; }

        /// <summary>The endpoint that the use case passed to the port.</summary>
        public ServerEndpoint? LastEndpoint { get; private set; }

        public Task<ServerVersionProbeResult> ProbeAsync(
            ServerEndpoint endpoint,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            LastEndpoint = endpoint;

            return Task.FromResult(new ServerVersionProbeResult(
                Compatible: !Incompatible,
                ServerVersion: "1.4.0",
                MinimumProtocolVersion: 1));
        }
    }

    /// <summary>
    /// Versiones que declara el cliente (AD-6).
    /// </summary>
    private sealed class FakeClientVersionProvider : IClientVersionProvider
    {
        public string ClientVersion => "1.2.0";

        public int ProtocolVersion => VersionInfo.CurrentProtocolVersion;
    }
}
