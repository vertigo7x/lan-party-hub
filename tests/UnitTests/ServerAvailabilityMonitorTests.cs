using LANPartyHub.Client.Application.Connection;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Shared.Contracts;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The supervisor of the availability of the event server (AC3, AC4, AC5, FR-3, NFR-9).
///
/// <para>
/// The tests fix the OBSERVABLE behaviour of the supervisor: when the server does not
/// answer, the availability is published as unavailable with a retry and a VISIBLE
/// countdown, and NO modal interrupts the player (AC3, AC4, UX-DR-29). When the server
/// comes back, the reconexión is attempted and el catálogo se resincroniza (AC5).
/// </para>
/// </summary>
public sealed class ServerAvailabilityMonitorTests
{
    private static readonly ServerEndpoint Endpoint = new("192.168.1.20", 5177);

    /// <summary>
    /// Retry delay that the tests use: it has to be SHORT so that the test does not wait,
    /// y lo bastante largo para que la cuenta atrás tenga varios pasos (AC4).
    /// </summary>
    private static readonly TimeSpan TestRetryDelay = TimeSpan.FromMilliseconds(300);

    [Fact]
    public async Task A_server_that_does_not_answer_publishes_the_unavailable_state()
    {
        // AC3: el servidor caído se publica como NO disponible, y el panel lo muestra.
        var probe = new ScriptedVersionProbe { Reachable = false };
        var monitor = CreateMonitor(probe);

        var published = new List<ServerAvailability>();
        monitor.AvailabilityChanged += published.Add;

        await monitor.StartAsync(Endpoint, TestContext.Current.CancellationToken);

        await probe.WaitForProbesAsync(1, TestContext.Current.CancellationToken);

        Assert.NotEmpty(published);
        Assert.False(published[0].Available);
    }

    [Fact]
    public async Task A_server_that_does_not_answer_publishes_a_retry_with_a_visible_countdown()
    {
        // AC4, UX-DR-29: el reintento tiene un CONTADOR visible: «Reintentando · 3 s».
        var probe = new ScriptedVersionProbe { Reachable = false };
        var monitor = CreateMonitor(probe);

        var published = new List<ServerAvailability>();
        monitor.AvailabilityChanged += published.Add;

        await monitor.StartAsync(Endpoint, TestContext.Current.CancellationToken);

        await probe.WaitForProbesAsync(1, TestContext.Current.CancellationToken);

        Assert.Contains(published, availability => availability.RetryCountdown is not null);
        Assert.Contains(published, availability => availability.RetryCountdown > TimeSpan.Zero);
    }

    [Fact]
    public async Task The_countdown_of_the_retry_decreases()
    {
        // AC4: el contador del reintento BAJA. El panel nunca muestra una espera
        // congelada.
        var probe = new ScriptedVersionProbe { Reachable = false };
        var monitor = CreateMonitor(probe);

        var published = new List<ServerAvailability>();
        monitor.AvailabilityChanged += published.Add;

        await monitor.StartAsync(Endpoint, TestContext.Current.CancellationToken);

        await probe.WaitForProbesAsync(1, TestContext.Current.CancellationToken);

        // La cuenta atrás se publica en pasos: el test espera a que el supervisor haya
        // publicado al menos dos valores DISTINTOS del contador.
        var deadline = DateTime.UtcNow.AddSeconds(5);

        while (DateTime.UtcNow < deadline)
        {
            var countdowns = published
                .Where(availability => availability.RetryCountdown is not null)
                .Select(availability => availability.RetryCountdown!.Value)
                .Distinct()
                .ToArray();

            if (countdowns.Length >= 2)
            {
                Assert.True(
                    countdowns.Zip(countdowns.Skip(1), (first, second) => second < first)
                        .Any(decreased => decreased),
                    "El contador del reintento tiene que decrecer.");

                return;
            }

            await Task.Delay(10, TestContext.Current.CancellationToken).ConfigureAwait(false);
        }

        Assert.Fail("El contador del reintento no llegó a publicar dos valores distintos.");
    }

    [Fact]
    public async Task A_server_that_answers_publishes_the_available_state_with_its_name_and_version()
    {
        // AC1: el panel muestra el nombre del servidor y su versión.
        var probe = new ScriptedVersionProbe
        {
            Reachable = true,
            ServerName = "MESA-1",
            ServerVersion = "1.4.0",
        };

        var monitor = CreateMonitor(probe);

        var published = new List<ServerAvailability>();
        monitor.AvailabilityChanged += published.Add;

        await monitor.StartAsync(Endpoint, TestContext.Current.CancellationToken);

        await probe.WaitForProbesAsync(1, TestContext.Current.CancellationToken);

        var available = await WaitForAvailableAsync(published, TestContext.Current.CancellationToken);

        Assert.Equal("MESA-1", available.ServerName);
        Assert.Equal("1.4.0", available.ServerVersion);
        Assert.Equal(Endpoint, available.Endpoint);
    }

    /// <summary>
    /// Waits until the supervisor publishes the server as AVAILABLE (AC1).
    /// </summary>
    /// <param name="published">Published availabilities.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The availability that says that the server is available.</returns>
    private static async Task<ServerAvailability> WaitForAvailableAsync(
        List<ServerAvailability> published,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);

        while (DateTime.UtcNow < deadline)
        {
            var available = published.FirstOrDefault(availability => availability.Available);

            if (available is not null)
            {
                return available;
            }

            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        }

        Assert.Fail("El supervisor no publicó el servidor como disponible.");

        throw new InvalidOperationException("The supervisor never published the server as available.");
    }

    [Fact]
    public async Task The_unavailable_state_conserves_the_name_and_the_version_of_the_server()
    {
        // AC3: la caída del servidor NO borra lo que el panel ya sabía de él.
        var probe = new ScriptedVersionProbe
        {
            Reachable = true,
            ServerName = "MESA-1",
            ServerVersion = "1.4.0",
        };

        var monitor = CreateMonitor(probe);

        var published = new List<ServerAvailability>();
        monitor.AvailabilityChanged += published.Add;

        await monitor.StartAsync(Endpoint, TestContext.Current.CancellationToken);

        var available = await WaitForAvailableAsync(published, TestContext.Current.CancellationToken);

        Assert.Equal("MESA-1", available.ServerName);

        probe.Reachable = false;

        var deadline = DateTime.UtcNow.AddSeconds(5);
        ServerAvailability? unavailable = null;

        while (DateTime.UtcNow < deadline)
        {
            unavailable = published.LastOrDefault(availability => !availability.Available);

            if (unavailable is not null)
            {
                break;
            }

            await Task.Delay(10, TestContext.Current.CancellationToken).ConfigureAwait(false);
        }

        Assert.NotNull(unavailable);
        Assert.Equal("MESA-1", unavailable!.ServerName);
        Assert.Equal("1.4.0", unavailable.ServerVersion);
    }

    [Fact]
    public async Task A_server_that_comes_back_triggers_the_reconnection()
    {
        // AC5: el servidor vuelve, el cliente recupera la conexión SIN reinstalarse.
        var probe = new ScriptedVersionProbe { Reachable = false };
        var handler = new RecordingReconnectHandler();
        var monitor = CreateMonitor(probe, handler);

        await monitor.StartAsync(Endpoint, TestContext.Current.CancellationToken);

        await probe.WaitForProbesAsync(1, TestContext.Current.CancellationToken);

        probe.Reachable = true;

        await handler.WaitForReconnectionsAsync(1, TestContext.Current.CancellationToken);

        Assert.Equal(Endpoint, handler.LastEndpoint);
    }

    [Fact]
    public async Task A_reconnection_that_fails_keeps_the_supervisor_retrying()
    {
        // AC4, AC5: una reconexión que NO pasa la puerta no deja de reintentar: el
        // supervisor sigue vigilando y lo intenta otra vez.
        var probe = new ScriptedVersionProbe { Reachable = false };
        var handler = new RecordingReconnectHandler { Succeeds = false };
        var monitor = CreateMonitor(probe, handler);

        await monitor.StartAsync(Endpoint, TestContext.Current.CancellationToken);

        await probe.WaitForProbesAsync(1, TestContext.Current.CancellationToken);

        // El servidor vuelve: el cliente intenta recuperar su conexión, pero la
        // reconexión NO pasa la puerta, así que el supervisor la reintenta (AC4, AC5).
        probe.Reachable = true;

        await handler.WaitForReconnectionsAsync(2, TestContext.Current.CancellationToken);

        Assert.True(handler.Attempts >= 2);
    }

    [Fact]
    public async Task A_healthy_server_does_not_trigger_the_reconnection_on_every_poll()
    {
        // AC5: la reconexión es la respuesta a una CAÍDA, no al estado permanente. Con el
        // servidor SANO, el sondeo periódico NO restablece la conexión: el cliente no
        // abre un socket, no repite la puerta de versión y no descarga el catálogo
        // completo en cada sondeo.
        var probe = new ScriptedVersionProbe { Reachable = true };
        var handler = new RecordingReconnectHandler();
        var monitor = CreateMonitor(probe, handler);

        await monitor.StartAsync(Endpoint, TestContext.Current.CancellationToken);

        // Varios sondeos con el servidor en pie.
        await probe.WaitForProbesAsync(3, TestContext.Current.CancellationToken);

        await Task.Delay(TestRetryDelay, TestContext.Current.CancellationToken);

        Assert.Equal(0, handler.Attempts);
    }

    [Fact]
    public async Task The_reconnection_happens_only_once_per_outage()
    {
        // AC5: una sola caída produce UNA sola reconexión. Los sondeos posteriores, con el
        // servidor ya recuperado, NO vuelven a restablecerse.
        var probe = new ScriptedVersionProbe { Reachable = false };
        var handler = new RecordingReconnectHandler();
        var monitor = CreateMonitor(probe, handler);

        await monitor.StartAsync(Endpoint, TestContext.Current.CancellationToken);

        await probe.WaitForProbesAsync(1, TestContext.Current.CancellationToken);

        probe.Reachable = true;

        await handler.WaitForReconnectionsAsync(1, TestContext.Current.CancellationToken);

        // Más sondeos con el servidor en pie: la reconexión NO se repite.
        await probe.WaitForProbesAsync(4, TestContext.Current.CancellationToken);

        await Task.Delay(TestRetryDelay, TestContext.Current.CancellationToken);

        Assert.Equal(1, handler.Attempts);
    }

    [Fact]
    public async Task A_second_outage_triggers_a_second_reconnection()
    {
        // AC5: cada CAÍDA produce su propia reconexión: la bandera de recuperación se
        // levanta otra vez y el cliente restablece su conexión cuando el servidor vuelve.
        var probe = new ScriptedVersionProbe { Reachable = false };
        var handler = new RecordingReconnectHandler();
        var monitor = CreateMonitor(probe, handler);

        await monitor.StartAsync(Endpoint, TestContext.Current.CancellationToken);

        await probe.WaitForProbesAsync(1, TestContext.Current.CancellationToken);

        probe.Reachable = true;

        await handler.WaitForReconnectionsAsync(1, TestContext.Current.CancellationToken);

        // El servidor CAE otra vez y vuelve.
        probe.Reachable = false;

        await probe.WaitForProbesAsync(3, TestContext.Current.CancellationToken);

        probe.Reachable = true;

        await handler.WaitForReconnectionsAsync(2, TestContext.Current.CancellationToken);

        Assert.True(handler.Attempts >= 2);
    }

    [Fact]
    public async Task The_supervisor_can_be_stopped()
    {
        var probe = new ScriptedVersionProbe { Reachable = false };
        var monitor = CreateMonitor(probe);

        await monitor.StartAsync(Endpoint, TestContext.Current.CancellationToken);

        await probe.WaitForProbesAsync(1, TestContext.Current.CancellationToken);

        await monitor.StopAsync();

        // El supervisor parado NO vuelve a sondear: el cliente no vigila un servidor que
        // ya no le importa.
        var probesAfterStop = probe.ProbeCount;

        await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);

        Assert.Equal(probesAfterStop, probe.ProbeCount);
    }

    [Fact]
    public void The_backoff_of_the_retry_grows_and_has_a_cap()
    {
        // AC4: el backoff CRECE y tiene TOPE, así que un servidor que sigue caído no
        // convierte la espera en un intervalo enorme.
        var monitor = new ServerAvailabilityMonitor(new ScriptedVersionProbe())
        {
            InitialRetryDelay = TimeSpan.FromSeconds(1),
            MaximumRetryDelay = TimeSpan.FromSeconds(8),
        };

        var first = monitor.NextRetryDelay(monitor.InitialRetryDelay);
        var second = monitor.NextRetryDelay(first);

        Assert.True(second > first);

        var capped = monitor.NextRetryDelay(monitor.MaximumRetryDelay);

        Assert.Equal(monitor.MaximumRetryDelay, capped);
    }

    [Fact]
    public void The_first_retry_is_soon()
    {
        // AC5: el primer reintento es PRONTO: el servidor puede que se haya reiniciado.
        Assert.Equal(TimeSpan.FromSeconds(1), ServerAvailabilityMonitor.DefaultInitialRetryDelay);
    }

    /// <summary>
    /// Builds the supervisor with the waits that los tests necesitan (AC4).
    /// </summary>
    /// <param name="probe">Sonda de las versiones del servidor.</param>
    /// <param name="handler">Manejador de la reconexión.</param>
    /// <returns>The supervisor.</returns>
    private static ServerAvailabilityMonitor CreateMonitor(
        ScriptedVersionProbe probe,
        IServerReconnectHandler? handler = null) =>
        new(probe, handler)
        {
            InitialRetryDelay = TestRetryDelay,
            MaximumRetryDelay = TestRetryDelay,
            AvailablePollInterval = TestRetryDelay,
        };

    /// <summary>
    /// Sonda de las versiones con el comportamiento que los tests necesitan (AD-6).
    /// </summary>
    private sealed class ScriptedVersionProbe : IServerVersionProbe
    {
        /// <summary>Whether the server answers.</summary>
        public bool Reachable { get; set; }

        /// <summary>Name of the server that the probe reads.</summary>
        public string? ServerName { get; set; }

        /// <summary>Version of the server that the probe reads.</summary>
        public string? ServerVersion { get; set; }

        /// <summary>How many times the probe was called.</summary>
        public int ProbeCount { get; private set; }

        public Task<ServerVersionProbeResult> ProbeAsync(
            ServerEndpoint endpoint,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            ProbeCount++;

            return Task.FromResult(new ServerVersionProbeResult(
                Compatible: true,
                ServerVersion,
                MinimumProtocolVersion: 1,
                ServerName,
                Reachable));
        }

        /// <summary>
        /// Waits until the probe was called the given number of times.
        /// </summary>
        /// <param name="count">Number of probes to wait for.</param>
        /// <param name="cancellationToken">Token that cancels the operation.</param>
        /// <returns>A task that completes when the probe was called enough times.</returns>
        public async Task WaitForProbesAsync(int count, CancellationToken cancellationToken)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);

            while (ProbeCount < count && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            }

            Assert.True(
                ProbeCount >= count,
                $"La sonda se llamó {ProbeCount} veces y se esperaban {count}.");
        }
    }

    /// <summary>
    /// Manejador de la reconexión que REGISTRA lo que el supervisor le pide (AC5).
    /// </summary>
    private sealed class RecordingReconnectHandler : IServerReconnectHandler
    {
        /// <summary>Whether the reconexión succeeds.</summary>
        public bool Succeeds { get; init; } = true;

        /// <summary>The endpoint that the supervisor passed.</summary>
        public ServerEndpoint? LastEndpoint { get; private set; }

        /// <summary>How many times the reconexión was attempted.</summary>
        public int Attempts { get; private set; }

        public Task<bool> ReconnectAsync(ServerEndpoint endpoint, CancellationToken cancellationToken)
        {
            LastEndpoint = endpoint;
            Attempts++;

            return Task.FromResult(Succeeds);
        }

        /// <summary>
        /// Waits until the reconexión was attempted the given number of times.
        /// </summary>
        /// <param name="count">Number of reconnections to wait for.</param>
        /// <param name="cancellationToken">Token that cancels the operation.</param>
        /// <returns>A task that completes when the reconexión was attempted enough times.</returns>
        public async Task WaitForReconnectionsAsync(int count, CancellationToken cancellationToken)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);

            while (Attempts < count && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            }

            Assert.True(
                Attempts >= count,
                $"La reconexión se intentó {Attempts} veces y se esperaban {count}.");
        }
    }
}
