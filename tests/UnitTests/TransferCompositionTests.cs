using System.Net;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Host;
using LANPartyHub.Client.Infrastructure;
using LANPartyHub.Client.Infrastructure.Networking;
using LANPartyHub.Shared.Contracts;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// Headers of every request that the engine of the transfer of bytes sends
/// (AD-6, AD-15, AD-20, AD-23).
///
/// <para>
/// La ruta del contenido es de la superficie del cliente del servidor del evento, así que
/// el servidor rechaza con 426 la petición que no declara la versión del protocolo y con
/// 400 la que no lleva el identificador del equipo. El motor delega esa declaración en el
/// manejador de cabeceras, y la composición es la que lo instala (AD-6).
/// </para>
/// <para>
/// Los tests observan la petición REAL que sale: un escuchador local recibe los bytes y
/// lee las cabeceras, así que lo que se comprueba es lo que el servidor vería (AD-16).
/// </para>
/// </summary>
public sealed class TransferCompositionTests
{
    private const string ClientId = "0193f2a1-b2c3-4d4e-5f60-718293a4b5c6";

    private const string OperationId = "0193f2a1-b2c3-4d4e-5f60-718293a4b5c7";

    [Fact]
    public async Task The_transfer_declares_the_identifier_of_the_client_in_every_request()
    {
        // AD-15, AD-23: la superficie del cliente EXIGE el identificador del equipo en
        // cada petición, así que el motor NUNCA puede viajar sin él.
        await using var listener = new CapturingListener();

        using var composition = new TransferComposition(new StubRequestHeaders(ClientId, OperationId));

        await TransferAsync(composition, listener.BaseAddress);

        var request = await listener.WaitForRequestAsync();

        Assert.Equal(
            ClientId,
            request.Headers[ClientRequestHeadersHandler.ClientIdHeaderName].Single());
    }

    [Fact]
    public async Task The_transfer_declares_the_identifier_of_the_operation_in_every_request()
    {
        await using var listener = new CapturingListener();

        using var composition = new TransferComposition(new StubRequestHeaders(ClientId, OperationId));

        await TransferAsync(composition, listener.BaseAddress);

        var request = await listener.WaitForRequestAsync();

        Assert.Equal(
            OperationId,
            request.Headers[ClientRequestHeadersHandler.OperationIdHeaderName].Single());
    }

    [Fact]
    public async Task The_transfer_declares_the_version_of_the_client_in_every_request()
    {
        // AD-6: TODA petición declara la versión del cliente, sin excepción: el servidor
        // rechaza con 426 la que no la lleva.
        await using var listener = new CapturingListener();

        using var composition = new TransferComposition(new StubRequestHeaders(ClientId, OperationId));

        await TransferAsync(composition, listener.BaseAddress);

        var request = await listener.WaitForRequestAsync();

        Assert.Equal(
            ClientBuildVersion.Current,
            request.Headers[ClientRequestHeadersHandler.ClientVersionHeaderName].Single());
    }

    [Fact]
    public async Task The_transfer_declares_the_version_of_the_protocol_in_every_request()
    {
        // AD-6: la versión del protocolo es la del contrato compartido, así que el
        // servidor puede decidir la compatibilidad de la petición (AD-6, AD-18).
        await using var listener = new CapturingListener();

        using var composition = new TransferComposition(new StubRequestHeaders(ClientId, OperationId));

        await TransferAsync(composition, listener.BaseAddress);

        var request = await listener.WaitForRequestAsync();

        Assert.Equal(
            VersionInfo.CurrentProtocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            request.Headers[ClientRequestHeadersHandler.ProtocolVersionHeaderName].Single());
    }

    [Fact]
    public async Task The_transfer_asks_for_the_range_with_the_range_header()
    {
        // AD-20: el motor pide rangos al servidor del evento con el encabezado `Range`,
        // y la composición no lo altera al estampar las cabeceras de la versión.
        await using var listener = new CapturingListener();

        using var composition = new TransferComposition(new StubRequestHeaders(ClientId, OperationId));

        await TransferAsync(composition, listener.BaseAddress);

        var request = await listener.WaitForRequestAsync();

        Assert.Equal("bytes=100-199", request.Headers["Range"].Single());
    }

    [Fact]
    public void The_composition_rejects_headers_that_are_absent()
    {
        Assert.Throws<ArgumentNullException>(() => new TransferComposition(null!));
    }

    /// <summary>
    /// Asks the engine of the composition for a range of the content that the local
    /// listener serves.
    /// </summary>
    /// <param name="composition">Composition of the transfer of bytes.</param>
    /// <param name="baseAddress">Address of the listener that receives the request.</param>
    /// <returns>A task that completes when the transfer has finished.</returns>
    private static async Task TransferAsync(TransferComposition composition, Uri baseAddress)
    {
        using var destination = new MemoryStream();

        await composition.Engine.TransferAsync(
            new Uri(baseAddress, "/api/v1/packages/pkg-1/content"),
            startInclusive: 100,
            endInclusive: 199,
            destination,
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Headers of the correlation that the composition stamps in every request (AD-15,
    /// AD-23).
    /// </summary>
    private sealed class StubRequestHeaders : IClientRequestHeaders
    {
        public StubRequestHeaders(string? clientId, string? operationId)
        {
            ClientId = clientId;
            OperationId = operationId;
        }

        public string? ClientId { get; }

        public string? OperationId { get; }
    }

    /// <summary>
    /// Listener that receives the request REAL del motor y guarda sus cabeceras.
    ///
    /// <para>
    /// La composición arma su propio cliente HTTP con un manejador interior de verdad, así
    /// que la única forma de observar lo que sale es poner un servidor delante. El
    /// escuchador contesta 200 con ocho bytes, así que el motor termina la transferencia.
    /// </para>
    /// </summary>
    private sealed class CapturingListener : IAsyncDisposable
    {
        private readonly HttpListener _listener = new();

        private readonly TaskCompletionSource<CapturedRequest> _received =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private Task? _loop;

        public CapturingListener()
        {
            var port = GetFreeTcpPort();

            BaseAddress = new Uri($"http://127.0.0.1:{port}/");

            _listener.Prefixes.Add(BaseAddress.ToString());

            _listener.Start();

            _loop = Task.Run(() => AcceptAsync());
        }

        /// <summary>Address that the listener serves.</summary>
        public Uri BaseAddress { get; }

        /// <summary>
        /// Waits for the request that the engine sent and reads its headers.
        /// </summary>
        /// <returns>The request that the engine built.</returns>
        public async Task<CapturedRequest> WaitForRequestAsync() =>
            await _received.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            _listener.Stop();
            _listener.Close();

            if (_loop is not null)
            {
                await _loop.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            }
        }

        /// <summary>
        /// Accepts the requests of the motor and keeps the first one.
        /// </summary>
        /// <returns>A task that completes when the listener stops.</returns>
        private async Task AcceptAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;

                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (Exception)
                {
                    return;
                }

                var headers = context.Request.Headers
                    .AllKeys
                    .Where(key => key is not null)
                    .ToDictionary(
                        key => key!,
                        key => context.Request.Headers.GetValues(key!)?.ToArray() ?? [],
                        StringComparer.OrdinalIgnoreCase);

                _received.TrySetResult(new CapturedRequest(headers));

                context.Response.StatusCode = (int)HttpStatusCode.OK;

                await context.Response
                    .OutputStream
                    .WriteAsync(new byte[8], TestContext.Current.CancellationToken);

                context.Response.Close();
            }
        }

        /// <summary>
        /// Picks a port that is free at the moment of the call.
        /// </summary>
        /// <returns>The port.</returns>
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
    }

    /// <summary>
    /// The request that the engine sent, with its headers.
    /// </summary>
    /// <param name="Headers">Headers of the request.</param>
    private sealed record CapturedRequest(Dictionary<string, string[]> Headers);
}
