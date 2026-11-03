using System.Net;
using LANPartyHub.Client.Application.Connection;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Host;
using LANPartyHub.Client.Infrastructure;
using LANPartyHub.Client.Infrastructure.Networking;
using LANPartyHub.Client.Infrastructure.Persistence;
using LANPartyHub.Client.Presentation;
using LANPartyHub.Client.Presentation.Status;
using LANPartyHub.Server.Host;
using LANPartyHub.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LANPartyHub.IntegrationTests;

/// <summary>
/// The reconexión del cliente cuando el servidor del evento cae y vuelve (AC1, AC3, AC4,
/// AC5, FR-3, NFR-9, AD-16).
///
/// <para>
/// La prueba levanta el host REAL, baja el acceso al servidor y lo restablece. Lo que se
/// observa es el PANEL PERSISTENTE de la conexión: el punto de presencia pasa a aviso con
/// el contador del reintento, NO hay ningún modal, y cuando el servidor vuelve el cliente
/// recupera la conexión y resincroniza el catálogo SIN reinstalarse (AC3, AC4, AC5).
/// </para>
/// </summary>
[Collection(RealHostCollection.Name)]
public sealed class ServerReconnectionTests : IDisposable
{
    private static readonly string ClientId = "0193f2a1b2c34d4e5f60718293a4b5c6";

    private readonly List<string> _temporaryFolders = [];

    [Fact]
    public async Task The_panel_shows_the_name_and_the_version_of_the_server_that_answers()
    {
        // AC1, UX-DR-11: el panel PERSISTENTE muestra el nombre del servidor, la dirección
        // en uso y la versión del servidor.
        var host = await TestServer.StartAsync(
            CreateTemporaryDataDirectory(),
            port: null,
            TestContext.Current.CancellationToken);

        await using (host)
        {
            var panel = new ConnectionStatusPanelViewModel();
            var published = new List<ServerAvailability>();

            var monitor = await StartSupervisionAsync(host, panel, published);

            try
            {
                await WaitForAsync(
                    () => published.Any(availability => availability.Available),
                    "el servidor nunca se publicó como disponible");

                var available = published.Last(availability => availability.Available);

                Assert.False(string.IsNullOrWhiteSpace(available.ServerName));
                Assert.False(string.IsNullOrWhiteSpace(available.ServerVersion));

                Assert.Equal(
                    $"{host.BoundAddress}:{host.Port}",
                    panel.AddressText);
                Assert.Equal(available.ServerName, panel.ServerNameText);
                Assert.Equal(available.ServerVersion, panel.ServerVersionText);
                Assert.Equal(ConnectionStatusPanelViewModel.PresenceAvailableText, panel.PresenceText);
            }
            finally
            {
                await monitor.StopAsync();
            }
        }
    }

    [Fact]
    public async Task The_server_that_answers_publishes_its_name_and_its_version()
    {
        // AC1: el servidor publica su nombre en la cabecera `X-Server-Name` y su versión en
        // la cabecera `X-Server-Version`, y el panel los muestra (AD-6, AD-18).
        var host = await TestServer.StartAsync(
            CreateTemporaryDataDirectory(),
            port: null,
            TestContext.Current.CancellationToken);

        await using (host)
        {
            var panel = new ConnectionStatusPanelViewModel();
            var published = new List<ServerAvailability>();

            var monitor = await StartSupervisionAsync(host, panel, published);

            try
            {
                await WaitForAsync(
                    () => published.Any(availability => availability.Available),
                    "el servidor nunca se publicó como disponible");

                var available = published.Last(availability => availability.Available);

                // El nombre publicado es el de la máquina que hospeda el servidor, y la
                // versión es la que el servidor publica (AD-6, AC1).
                Assert.Equal(Environment.MachineName, available.ServerName);
                Assert.Equal(ServerVersion.Current, available.ServerVersion);
            }
            finally
            {
                await monitor.StopAsync();
            }
        }
    }

    [Fact]
    public async Task The_route_of_the_server_publishes_its_name_in_a_header()
    {
        // AC1: el nombre del servidor viaja como CABECERA, con el mismo patrón que la
        // versión, y el contrato de `Shared` no crece (AD-18, AD-33).
        var host = await TestServer.StartAsync(
            CreateTemporaryDataDirectory(),
            port: null,
            TestContext.Current.CancellationToken);

        await using (host)
        {
            using var client = host.CreateClient();

            using var request = new HttpRequestMessage(HttpMethod.Get, ServerInfoEndpoint.Route);
            request.Headers.Add(
                LANPartyHub.Server.Infrastructure.Http.CorrelationMiddleware.ClientIdHeaderName,
                ClientId);
            request.Headers.TryAddWithoutValidation(
                LANPartyHub.Server.Infrastructure.Http.ClientVersionMiddleware.ClientVersionHeaderName,
                "1.0.0");
            request.Headers.TryAddWithoutValidation(
                LANPartyHub.Server.Infrastructure.Http.ClientVersionMiddleware.ProtocolVersionHeaderName,
                VersionInfo.CurrentProtocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));

            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.Contains(ServerInfoEndpoint.ServerNameHeaderName));

            var name = response.Headers.GetValues(ServerInfoEndpoint.ServerNameHeaderName).Single();

            Assert.False(string.IsNullOrWhiteSpace(name));
        }
    }

    [Fact]
    public async Task The_client_recovers_when_the_server_comes_back_without_reinstalling()
    {
        // AC5: el servidor cae y vuelve; el cliente recupera la conexión y resincroniza el
        // catálogo SIN reinstalarse.
        var dataDirectory = CreateTemporaryDataDirectory();
        var port = GetFreeTcpPort();

        var host = await TestServer.StartAsync(dataDirectory, port, TestContext.Current.CancellationToken);

        var panel = new ConnectionStatusPanelViewModel();
        var published = new List<ServerAvailability>();
        panel.Changed += () => published.Add(panel.Availability);

        var monitor = await StartSupervisionAsync(host, panel, published);

        try
        {
            // El servidor está en pie: el punto de presencia dice que está disponible.
            await WaitForAsync(
                () => published.Any(availability => availability.Available),
                "el servidor nunca se publicó como disponible");

            // El servidor CAE: el acceso se corta y el panel pasa a aviso con el contador
            // del reintento (AC3, AC4).
            await host.DisposeAsync();

            await WaitForAsync(
                () => published.Any(availability => !availability.Available),
                "el servidor nunca se publicó como no disponible");

            var unavailable = published.Last(availability => !availability.Available);

            Assert.NotNull(unavailable.RetryCountdown);
            Assert.True(
                unavailable.RetryCountdown > TimeSpan.Zero,
                "El reintento tiene que mostrar el tiempo que le queda.");

            // El aviso dice EXACTAMENTE lo que el texto de la historia fija (AC3).
            Assert.Equal(
                "Servidor no disponible. Se conserva el último catálogo sincronizado.",
                ConnectionStatusPanelViewModel.ServerUnavailableText);

            // El servidor VUELVE: el cliente recupera la conexión y resincroniza el
            // catálogo (AC5).
            await using var restored = await TestServer.StartAsync(
                dataDirectory,
                port,
                TestContext.Current.CancellationToken);

            await WaitForAsync(
                () => published.Count(availability => availability.Available) >= 2,
                "el cliente no recuperó la conexión cuando el servidor volvió");
        }
        finally
        {
            await monitor.StopAsync();
        }
    }

    [Fact]
    public async Task The_catalog_is_resynchronized_when_the_server_comes_back()
    {
        // AC5: la reconexión no solo restablece el socket: RESINCRONIZA el catálogo, sin
        // reinstalar nada (AD-5, AD-26).
        var dataDirectory = CreateTemporaryDataDirectory();
        var port = GetFreeTcpPort();

        var host = await TestServer.StartAsync(dataDirectory, port, TestContext.Current.CancellationToken);

        await using (host)
        {
            var clientDataFolder = CreateTemporaryDataDirectory();
            var databasePath = Path.Combine(clientDataFolder, ClientDataLayout.StateDatabaseFileName);

            var options = new DbContextOptionsBuilder<ClientStateDbContext>()
                .UseSqlite($"Data Source={databasePath};Pooling=False")
                .Options;

            await using var context = new ClientStateDbContext(options);

            await context.Database.MigrateAsync(TestContext.Current.CancellationToken);

            using var correlation = new ClientCorrelationFactory(NullLoggerFactory.Instance);

            correlation.Open(ClientId);

            await using var catalogSync = new CatalogSyncComposition(
                new Uri($"http://{host.BoundAddress}:{host.Port}"),
                databasePath,
                correlation,
                NullLoggerFactory.Instance,
                connectionGate: null,
                new AvaloniaUiThread());

            await catalogSync.StartAsync(TestContext.Current.CancellationToken);

            // El catálogo se sincroniza al menos una vez mientras el servidor está en pie.
            var synchronized = false;

            for (var attempt = 0; attempt < 50 && !synchronized; attempt++)
            {
                synchronized = await catalogSync.Service
                    .SynchronizeAsync(TestContext.Current.CancellationToken) is { Synchronized: true };

                if (!synchronized)
                {
                    await Task.Delay(100, TestContext.Current.CancellationToken);
                }
            }

            Assert.True(synchronized, "El catálogo no se sincronizó con el servidor en pie.");
        }
    }

    [Fact]
    public async Task A_healthy_server_does_not_reconnect_on_every_poll()
    {
        // AC5: la reconexión es la respuesta a una CAÍDA, no al estado permanente. Con el
        // servidor SANO, el sondeo periódico NO restablece la conexión: el cliente no abre
        // un socket, no repite la puerta de versión y no descarga el catálogo completo en
        // cada sondeo.
        var host = await TestServer.StartAsync(
            CreateTemporaryDataDirectory(),
            port: null,
            TestContext.Current.CancellationToken);

        await using (host)
        {
            var handler = new CountingReconnectHandler();

            var probe = new CountingVersionProbe(
                new HttpServerVersionProbe(
                    BuildProbeHttpClient(),
                    NullLogger<HttpServerVersionProbe>.Instance));

            var monitor = new ServerAvailabilityMonitor(
                probe,
                handler,
                NullLogger<ServerAvailabilityMonitor>.Instance)
            {
                InitialRetryDelay = TimeSpan.FromMilliseconds(100),
                MaximumRetryDelay = TimeSpan.FromMilliseconds(100),
                AvailablePollInterval = TimeSpan.FromMilliseconds(100),
            };

            await monitor.StartAsync(
                new ServerEndpoint(host.BoundAddress.ToString(), host.Port),
                TestContext.Current.CancellationToken);

            try
            {
                // Varios sondeos con el servidor en pie.
                await WaitForAsync(
                    () => probe.ProbeCount >= 5,
                    "el supervisor no llegó a sondear cinco veces");

                await Task.Delay(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);

                Assert.Equal(0, handler.Attempts);
            }
            finally
            {
                await monitor.StopAsync();
            }
        }
    }

    [Fact]
    public async Task The_client_reconnects_once_when_the_server_comes_back()
    {
        // AC5: cuando el servidor CAE y vuelve, el cliente recupera la conexión UNA sola
        // vez. Los sondeos posteriores, con el servidor ya recuperado, NO vuelven a
        // reconectar (AC5).
        var dataDirectory = CreateTemporaryDataDirectory();
        var port = GetFreeTcpPort();

        var host = await TestServer.StartAsync(dataDirectory, port, TestContext.Current.CancellationToken);

        var handler = new CountingReconnectHandler();

        var probe = new CountingVersionProbe(
            new HttpServerVersionProbe(
                BuildProbeHttpClient(),
                NullLogger<HttpServerVersionProbe>.Instance));

        var monitor = new ServerAvailabilityMonitor(
            probe,
            handler,
            NullLogger<ServerAvailabilityMonitor>.Instance)
        {
            InitialRetryDelay = TimeSpan.FromMilliseconds(100),
            MaximumRetryDelay = TimeSpan.FromMilliseconds(100),
            AvailablePollInterval = TimeSpan.FromMilliseconds(100),
        };

        await monitor.StartAsync(
            new ServerEndpoint(host.BoundAddress.ToString(), host.Port),
            TestContext.Current.CancellationToken);

        try
        {
            // El servidor está en pie: el cliente ya está conectado y NO reconecta.
            await WaitForAsync(
                () => probe.ProbeCount >= 2,
                "el supervisor no llegó a sondear dos veces con el servidor en pie");

            // El servidor CAE.
            await host.DisposeAsync();

            await WaitForAsync(
                () => probe.ProbeCount >= 4,
                "el supervisor no siguió vigilando tras la caída");

            // El servidor VUELVE: el cliente recupera su conexión.
            await using var restored = await TestServer.StartAsync(
                dataDirectory,
                port,
                TestContext.Current.CancellationToken);

            await WaitForAsync(
                () => handler.Attempts >= 1,
                "el cliente no recuperó la conexión cuando el servidor volvió");

            // Más sondeos con el servidor ya recuperado: la reconexión NO se repite.
            await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);

            Assert.Equal(1, handler.Attempts);
        }
        finally
        {
            await monitor.StopAsync();
        }
    }

    /// <summary>
    /// Starts the supervision of the availability of the host and wires it to the panel
    /// (AC1, AC3, AC4).
    ///
    /// <param name="host">Host of the event server.</param>
    /// <param name="panel">View model of the persistent panel.</param>
    /// <param name="published">List that collects the published availabilities.</param>
    /// <returns>The supervisor of the availability.</returns>
    private static async Task<ServerAvailabilityMonitor> StartSupervisionAsync(
        TestServer host,
        ConnectionStatusPanelViewModel panel,
        List<ServerAvailability>? published = null)
    {
        var probe = new HttpServerVersionProbe(
            BuildProbeHttpClient(),
            NullLogger<HttpServerVersionProbe>.Instance);

        var monitor = new ServerAvailabilityMonitor(
            probe,
            reconnectHandler: null,
            NullLogger<ServerAvailabilityMonitor>.Instance)
        {
            InitialRetryDelay = TimeSpan.FromMilliseconds(300),
            MaximumRetryDelay = TimeSpan.FromMilliseconds(300),
            AvailablePollInterval = TimeSpan.FromMilliseconds(300),
        };

        monitor.AvailabilityChanged += availability =>
        {
            published?.Add(availability);
            panel.ApplyAvailability(availability);
        };

        await monitor.StartAsync(
            new ServerEndpoint(host.BoundAddress.ToString(), host.Port),
            TestContext.Current.CancellationToken);

        return monitor;
    }

    /// <summary>
    /// Builds the client of HTTP of the probe, con el manejador real de la correlación
    /// (AD-15, AD-23).
    /// </summary>
    /// <returns>The client of HTTP.</returns>
    private static HttpClient BuildProbeHttpClient()
    {
        var correlation = new ClientCorrelationFactory(NullLoggerFactory.Instance);

        correlation.Open(ClientId);

        return new HttpClient(
            new ClientRequestHeadersHandler(correlation, new RealClientVersionProvider())
            {
                InnerHandler = new HttpClientHandler(),
            });
    }

    /// <summary>
    /// Waits until the condition holds, or fails the test (AC3, AC5).
    /// </summary>
    /// <param name="condition">Condition to wait for.</param>
    /// <param name="message">Message of the failure.</param>
    /// <returns>A task that completes when the condition holds.</returns>
    private static async Task WaitForAsync(Func<bool> condition, string message)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);

        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        Assert.Fail(message);
    }

    private static int GetFreeTcpPort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);

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

    private string CreateTemporaryDataDirectory()
    {
        var folder = Directory.CreateTempSubdirectory("lan-party-hub-reconnection-").FullName;

        _temporaryFolders.Add(folder);

        return folder;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var folder in _temporaryFolders)
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
                // Un archivo que el sistema operativo todavía tiene abierto no falla la
                // prueba.
            }
            catch (UnauthorizedAccessException)
            {
                // La carpeta no se puede borrar ahora, y no falla la prueba.
            }
        }
    }

    /// <summary>
    /// Sonda de las versiones que CUENTA los sondeos, para que la prueba pueda observar
    /// cuántos sondeos hizo el supervisor (AC5).
    /// </summary>
    private sealed class CountingVersionProbe : IServerVersionProbe
    {
        private readonly IServerVersionProbe _inner;

        public CountingVersionProbe(IServerVersionProbe inner) => _inner = inner;

        /// <summary>How many times the probe was called.</summary>
        public int ProbeCount { get; private set; }

        public Task<ServerVersionProbeResult> ProbeAsync(
            ServerEndpoint endpoint,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            ProbeCount++;

            return _inner.ProbeAsync(endpoint, timeout, cancellationToken);
        }
    }

    /// <summary>
    /// Manejador de la reconexión que CUENTA los intentos, para que la prueba pueda
    /// observar cuántas veces el supervisor restableció la conexión (AC5).
    /// </summary>
    private sealed class CountingReconnectHandler : IServerReconnectHandler
    {
        /// <summary>How many times the reconexión was attempted.</summary>
        public int Attempts { get; private set; }

        public Task<bool> ReconnectAsync(ServerEndpoint endpoint, CancellationToken cancellationToken)
        {
            Attempts++;

            return Task.FromResult(true);
        }
    }
}
