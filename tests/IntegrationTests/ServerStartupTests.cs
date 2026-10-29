using System.Net;
using System.Net.Sockets;
using LANPartyHub.Server.Host;
using LANPartyHub.Server.Infrastructure.Networking;
using LANPartyHub.Server.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LANPartyHub.IntegrationTests;

/// <summary>
/// Real startup of the event server (AD-11, AD-12, AD-15, AD-17).
///
/// The tests start the real host, with the real Kestrel, the real sockets and a real SQLite
/// file, because that is the only way to observe the network surface and the schema of the
/// database of the server.
/// </summary>
public sealed class ServerStartupTests
{
    private const int MdnsPort = 5353;

    private const string UrlsVariable = "ASPNETCORE_URLS";

    private const string NoPrivateInterfaceReason =
        "This host has no operational private interface (RFC 1918 or ULA), so the real startup cannot be exercised here.";

    private static readonly string[] ExpectedTables =
    [
        "CATALOG_VERSION",
        "CLIENT_ERROR",
        "CLIENT_INSTALLATION_VIEW",
        "CLIENT_REGISTRATION",
        "DOWNLOAD_INTENT",
        "GAME",
        "INSTALLATION_MANIFEST",
        "PACKAGE_DESCRIPTOR",
    ];

    [Fact]
    public async Task The_startup_creates_the_data_folder_the_database_and_the_package_repository()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        Assert.True(File.Exists(Path.Combine(dataDirectory, DataDirectory.CatalogDatabaseFileName)));
        Assert.True(Directory.Exists(Path.Combine(dataDirectory, DataDirectory.PackagesFolderName)));
    }

    [Fact]
    public async Task The_startup_creates_the_eight_tables_of_the_erd_and_the_history_of_the_migrations()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var connection = new SqliteConnection($"Data Source={Path.Combine(dataDirectory, DataDirectory.CatalogDatabaseFileName)}");
        connection.Open();

        var tables = ReadNames(connection, "SELECT name FROM sqlite_master WHERE type = 'table'");

        foreach (var table in ExpectedTables)
        {
            Assert.Contains(table, tables);
        }

        var appliedMigrations = ReadNames(connection, "SELECT MigrationId FROM __EFMigrationsHistory");

        Assert.NotEmpty(appliedMigrations);
        Assert.Contains(appliedMigrations, migration => migration.Contains("InitialCatalogSchema", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(
        "GAME",
        "gameId,name,description,categories,tags,coverImage,images,version,sizeBytes,minPlayers,maxPlayers,"
        + "operatingSystem,distributionType,installationType,launchConfiguration,dependencies,license,distributionRestrictions")]
    [InlineData(
        "PACKAGE_DESCRIPTOR",
        "packageId,gameId,version,sizeBytes,chunkSizeBytes,sha256,manifestRef,withdrawnAt")]
    [InlineData(
        "CATALOG_VERSION",
        "id,catalogVersion")]
    [InlineData(
        "INSTALLATION_MANIFEST",
        "manifestId,packageId,schemaVersion,signature,keyId,canonicalManifest")]
    [InlineData(
        "CLIENT_REGISTRATION",
        "clientId,machineName,clientVersion,protocolVersion,connectionState,lastSeenAt")]
    [InlineData(
        "CLIENT_INSTALLATION_VIEW",
        "clientId,gameId,installedVersion,validationStateText")]
    [InlineData(
        "DOWNLOAD_INTENT",
        "operationId,clientId,packageId,state,outcome")]
    [InlineData(
        "CLIENT_ERROR",
        "errorId,clientId,operationId,code,message,cause,nextStep,occurredAt")]
    public async Task The_columns_of_the_table_keep_the_name_and_the_order_of_the_erd(string table, string expectedColumns)
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var connection = new SqliteConnection($"Data Source={Path.Combine(dataDirectory, DataDirectory.CatalogDatabaseFileName)}");
        connection.Open();

        var columns = ReadNames(connection, $"SELECT name FROM pragma_table_info('{table}')");

        Assert.Equal(expectedColumns.Split(','), columns);
    }

    [Fact]
    public async Task The_startup_is_idempotent_over_a_database_that_is_already_migrated()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using (var first = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken))
        {
            Assert.NotNull(first);
        }

        await using var second = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var connection = new SqliteConnection($"Data Source={Path.Combine(dataDirectory, DataDirectory.CatalogDatabaseFileName)}");
        connection.Open();

        var appliedMigrations = ReadNames(connection, "SELECT MigrationId FROM __EFMigrationsHistory");

        // La migración inicial y la de la versión del catálogo y la retirada: el arranque
        // es idempotente y no aplica ninguna de las dos dos veces (AD-11).
        Assert.Equal(2, appliedMigrations.Count);
        Assert.Contains(appliedMigrations, migration => migration.Contains("InitialCatalogSchema", StringComparison.Ordinal));
        Assert.Contains(appliedMigrations, migration => migration.Contains("CatalogVersionAndWithdrawal", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_metrics_endpoint_answers_on_the_http_port()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = new HttpClient { BaseAddress = new Uri($"http://{host.BoundAddress}:{host.Port}") };

        using var response = await client.GetAsync(MetricsEndpoint.Route, TestContext.Current.CancellationToken);
        var payload = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.True(response.IsSuccessStatusCode, payload);
        Assert.Contains("transferRateBytesPerSecond", payload);
        Assert.Contains("activeOperations", payload);
        Assert.Contains("errorsByCode", payload);
    }

    [Fact]
    public async Task The_only_tcp_port_in_listening_state_is_the_http_port_and_its_address_is_private()
    {
        // The sockets of the machine cannot be used to observe the surface of the server: the
        // machine runs background processes that open sockets of their own, and a diff of the
        // whole machine reports them as if the server had opened them. Only the sockets of the
        // process that hosts the server tell them apart.
        var listenersBefore = ReadListeningEndpoints(Environment.ProcessId);
        var udpBefore = ReadUdpEndpoints(Environment.ProcessId);

        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        var opened = ReadListeningEndpoints(Environment.ProcessId).Except(listenersBefore).ToArray();

        Assert.NotEmpty(opened);
        Assert.All(opened, endpoint => Assert.Equal(host.Port, endpoint.Port));
        Assert.All(opened, endpoint => Assert.True(
            endpoint.Address is not null && IsPrivateAddress(endpoint.Address),
            $"The server opened a listener in an address that is not private: {endpoint}."));

        // The only new UDP socket is the one of the mDNS announcement (AD-12).
        var openedUdp = ReadUdpEndpoints(Environment.ProcessId).Except(udpBefore).ToArray();

        Assert.All(openedUdp, endpoint => Assert.Equal(MdnsPort, endpoint.Port));
    }

    [Fact]
    public async Task The_server_opens_no_outbound_tcp_connection()
    {
        var connectionsBefore = ProcessTcpConnections.Get(Environment.ProcessId)
            .Select(Describe)
            .ToHashSet(StringComparer.Ordinal);

        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        var newConnections = ProcessTcpConnections.GetConnected(Environment.ProcessId)
            .Select(Describe)
            .Where(connection => !connectionsBefore.Contains(connection))
            .ToArray();

        // The server only opens listeners: it never reaches out to any service (NFR-10). The
        // listeners live in the listener table and the connections in this one, so an empty
        // diff means that the server established no connection at all.
        Assert.Empty(newConnections);
    }

    [Fact]
    public async Task The_mdns_announcement_uses_the_udp_port_5353()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        var advertiser = host.Application.Services.GetRequiredService<IMdnsServiceAdvertiser>();

        if (!advertiser.IsAdvertising)
        {
            Assert.Skip(
                "The network of this host blocks the multicast, so the mDNS announcement cannot be emitted. "
                + "The server started anyway, which is what AD-3 requires, but the UDP socket cannot be observed here.");

            return;
        }

        // The socket is looked up in the table of the process that hosts the server, and not
        // in the table of the machine: another program of the machine could be listening on
        // the same port and the assertion would pass without the announcement.
        var udpSockets = ProcessUdpSockets.Get(Environment.ProcessId);

        Assert.Contains(udpSockets, socket => socket.LocalEndPoint.Port == MdnsPort);
    }

    [Fact]
    public async Task The_server_starts_even_when_the_mdns_announcement_cannot_be_published()
    {
        SkipWithoutPrivateInterface();

        var dataDirectory = CreateTemporaryDataDirectory();
        var port = GetFreeTcpPort();

        var application = await Program.BuildServerAsync(
            ["--data-dir", dataDirectory, "--http-port", port.ToString()],
            privateAddressProvider: null,
            mdnsPublisher: new FailingMdnsPublisher());

        await using (application)
        {
            await application.StartAsync(TestContext.Current.CancellationToken);

            var advertiser = application.Services.GetRequiredService<IMdnsServiceAdvertiser>();

            Assert.False(advertiser.IsAdvertising);

            // The manual address is an equivalent way to reach the server, so the HTTP
            // surface keeps serving with the announcement out of service (AD-3).
            var boundAddress = GetPrivateAddresses().First();

            using var client = new HttpClient { BaseAddress = new Uri($"http://{boundAddress}:{port}") };

            using var response = await client.GetAsync(MetricsEndpoint.Route, TestContext.Current.CancellationToken);

            Assert.True(response.IsSuccessStatusCode);

            await application.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task The_server_refuses_to_start_when_the_host_has_no_private_address()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        var exception = await Assert.ThrowsAsync<ServerStartupException>(
            () => Program.BuildServerAsync(
                ["--data-dir", dataDirectory, "--http-port", "8080"],
                new NoPrivateAddressProvider()));

        Assert.Contains("0.0.0.0", exception.Message);
    }

    [Fact]
    public async Task The_urls_variable_of_the_environment_does_not_move_the_bind_out_of_the_private_interfaces()
    {
        SkipWithoutPrivateInterface();

        var previous = Environment.GetEnvironmentVariable(UrlsVariable);

        try
        {
            Environment.SetEnvironmentVariable(UrlsVariable, "http://+:5000");

            var listenersBefore = ReadListeningEndpoints(Environment.ProcessId);

            var dataDirectory = CreateTemporaryDataDirectory();
            var port = GetFreeTcpPort();

            var application = await Program.BuildServerAsync(["--data-dir", dataDirectory, "--http-port", port.ToString()]);

            await using (application)
            {
                await application.StartAsync(TestContext.Current.CancellationToken);

                var opened = ReadListeningEndpoints(Environment.ProcessId).Except(listenersBefore).ToArray();

                Assert.NotEmpty(opened);
                Assert.All(opened, endpoint => Assert.Equal(port, endpoint.Port));
                Assert.All(opened, endpoint => Assert.True(
                    endpoint.Address is not null && IsPrivateAddress(endpoint.Address),
                    $"The server opened a listener in an address that is not private: {endpoint}."));

                await application.StopAsync(TestContext.Current.CancellationToken);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(UrlsVariable, previous);
        }
    }

    [Fact]
    public async Task The_configuration_file_of_the_deployment_is_the_lowest_priority_layer()
    {
        SkipWithoutPrivateInterface();

        var deployment = Directory.CreateTempSubdirectory("lan-party-hub-deployment-");
        var port = GetFreeTcpPort();

        await File.WriteAllTextAsync(
            Path.Combine(deployment.FullName, ServerConfiguration.FileName),
            "{\"Server\":{\"httpPort\":" + port + ",\"dataDirectory\":\"del-archivo\"}}",
            TestContext.Current.CancellationToken);

        var dataDirectory = CreateTemporaryDataDirectory();

        // The port is not given as a flag, so it can only come from the file of the
        // deployment, and the file is the layer of the lowest priority.
        var application = await Program.BuildServerAsync(
            ["--data-dir", dataDirectory],
            baseDirectory: deployment.FullName);

        await using (application)
        {
            await application.StartAsync(TestContext.Current.CancellationToken);

            // The listener is looked up in the table of the process that hosts the server, and
            // not in the table of the machine: another program of the machine could be
            // listening on the same port and the assertion would pass without the server.
            var listeners = ReadListeningEndpoints(Environment.ProcessId);

            Assert.Contains(listeners, endpoint => endpoint.Port == port);

            await application.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task The_startup_aborts_when_the_http_port_is_already_in_use()
    {
        SkipWithoutPrivateInterface();

        var privateAddresses = GetPrivateAddresses();

        var occupant = new TcpListener(privateAddresses[0], 0);
        occupant.Start();

        try
        {
            var port = ((IPEndPoint)occupant.LocalEndpoint).Port;
            var dataDirectory = CreateTemporaryDataDirectory();

            var application = await Program.BuildServerAsync(["--data-dir", dataDirectory, "--http-port", port.ToString()]);

            await using (application)
            {
                await Assert.ThrowsAnyAsync<IOException>(() => application.StartAsync(TestContext.Current.CancellationToken));
            }
        }
        finally
        {
            occupant.Stop();
        }
    }

    [Fact]
    public async Task The_server_refuses_to_start_with_an_unknown_option()
    {
        var exception = await Assert.ThrowsAsync<ServerStartupException>(
            () => Program.BuildServerAsync(["--tracker-port", "6881"]));

        Assert.Contains("--tracker-port", exception.Message);
        Assert.Contains("--data-dir", exception.Message);
        Assert.Contains("--http-port", exception.Message);
    }

    /// <summary>
    /// Tells whether an address belongs to a private range. It is written here, and not taken
    /// from the resolver of the server, so the assertion is independent of the code it
    /// checks.
    /// </summary>
    private static bool IsPrivateAddress(IPAddress address)
    {
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var octets = address.GetAddressBytes();

            return octets[0] == 10
                || (octets[0] == 172 && octets[1] >= 16 && octets[1] <= 31)
                || (octets[0] == 192 && octets[1] == 168);
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return !IPAddress.IsLoopback(address)
                && !address.IsIPv6LinkLocal
                && address.IsIPv6UniqueLocal;
        }

        return false;
    }

    private static string Describe(ProcessTcpConnection connection) =>
        $"{connection.LocalEndPoint}|{connection.RemoteEndPoint}|{connection.State}";

    /// <summary>
    /// Endpoints of the sockets that the process has in listening state.
    /// </summary>
    private static IReadOnlyList<IPEndPoint> ReadListeningEndpoints(int processId) =>
        ProcessTcpConnections.GetListening(processId)
            .Select(connection => connection.LocalEndPoint)
            .ToArray();

    /// <summary>
    /// Endpoints of the UDP sockets that the process has.
    /// </summary>
    private static IReadOnlyList<IPEndPoint> ReadUdpEndpoints(int processId) =>
        ProcessUdpSockets.Get(processId)
            .Select(socket => socket.LocalEndPoint)
            .ToArray();

    private static IReadOnlyList<IPAddress> GetPrivateAddresses() =>
        new PrivateAddressResolver().GetPrivateAddresses();

    private static void SkipWithoutPrivateInterface()
    {
        if (GetPrivateAddresses().Count == 0)
        {
            Assert.Skip(NoPrivateInterfaceReason);
        }
    }

    private static IReadOnlyList<string> ReadNames(SqliteConnection connection, string command)
    {
        using var reader = connection.CreateCommand();
        reader.CommandText = command;

        var names = new List<string>();

        using var results = reader.ExecuteReader();

        while (results.Read())
        {
            names.Add(results.GetString(0));
        }

        return names;
    }

    private static string CreateTemporaryDataDirectory()
    {
        var folder = Directory.CreateTempSubdirectory("lan-party-hub-server-");

        return folder.FullName;
    }

    private static int GetFreeTcpPort()
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

    private sealed class NoPrivateAddressProvider : IPrivateAddressProvider
    {
        public IReadOnlyList<IPAddress> GetPrivateAddresses() => [];
    }

    private sealed class FailingMdnsPublisher : IMdnsPublisher
    {
        public string InstanceName => "test";

        public int Port => 5353;

        public void Publish() =>
            throw new InvalidOperationException("The multicast of the network is blocked by its policy.");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
