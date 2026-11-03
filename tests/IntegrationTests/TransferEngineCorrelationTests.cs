using System.Net;
using System.Text.Json;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Host;
using LANPartyHub.Server.Host;
using Xunit;

namespace LANPartyHub.IntegrationTests;

/// <summary>
/// The headers that the engine of the transfer of bytes sends against the real host
/// (AD-6, AD-15, AD-20, AD-23).
///
/// <para>
/// La ruta del contenido del paquete es de la superficie del cliente, así que la puerta
/// de versión y la de correlación la cubren: el servidor rechaza con 426 la petición que
/// no declara la versión del protocolo y con 400 la que no lleva el identificador del
/// equipo (AD-6, AD-23). Un motor que viajara sin esas cabeceras NO PODRÍA pasar la puerta
/// de versión, así que la ruta de contenido que sirvió la 5.2 nunca habría recibido una
/// petición.
///
/// <para>
/// La observación es la del SERVIDOR: el contador de errores por código del host cuenta
/// los rechazos de la correlación, así que un motor que viaja sin las cabeceras se ve
/// aquí como un rechazo (AD-15, AD-25).
/// </para>
/// </summary>
[Collection(RealHostCollection.Name)]
public sealed class TransferEngineCorrelationTests
{
    /// <summary>
    /// Canonical identifier of a team: 32 lowercase hexadecimal digits (AD-23, AD-31).
    /// </summary>
    private const string ClientId = "0193f2a1b2c34d4e5f60718293a4b5c6";

    [Fact]
    public async Task The_engine_of_the_transfer_passes_the_version_gate_of_the_server()
    {
        // AD-6: la petición del motor declara la versión del cliente y la del protocolo, así
        // que el servidor NO la rechaza con 426. La ruta del catálogo es de la superficie
        // del cliente y la 5.2 servirá el contenido del paquete por la misma superficie.
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        using var composition = new TransferComposition(new StubRequestHeaders(ClientId));

        var outcome = await TransferAsync(composition, host);

        // La puerta de versión y la de correlación NO rechazaron la petición: la
        // transferencia llegó al cuerpo de la respuesta.
        Assert.True(outcome.BytesTransferred > 0);
    }

    [Fact]
    public async Task The_server_counts_no_rejection_of_the_engine_of_the_transfer()
    {
        // La observación del servidor: los rechazos se cuentan por código, así que un motor
        // que viaja sin cabeceras aparece aquí como un rechazo CLIENT_ID_MISSING o
        // PROTOCOL_VERSION_MISSING (AD-15, AD-25).
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        using var composition = new TransferComposition(new StubRequestHeaders(ClientId));

        await TransferAsync(composition, host);

        using var client = host.CreateClient();

        using var document = JsonDocument.Parse(
            await client.GetStringAsync(MetricsEndpoint.Route, TestContext.Current.CancellationToken));

        var rejections = document.RootElement
            .GetProperty("errorsByCode")
            .EnumerateObject()
            .Where(property => property.Value.TryGetInt32(out var count) && count > 0)
            .ToArray();

        Assert.Empty(rejections);
    }

    /// <summary>
    /// Asks the engine of the composition for a range of the catalog of the real host.
    ///
    /// <para>
    /// La ruta del contenido del paquete todavía NO existe (la 5.2 la sirve), así que el
    /// motor pide un rango de la ruta del catálogo: es la misma superficie del cliente y la
    /// misma puerta, así que lo que el motor declara es lo que el servidor comprueba
    /// (AD-6, AD-20).
    /// </para>
    /// </summary>
    /// <param name="composition">Composition of the transfer of bytes.</param>
    /// <param name="host">The real host of the event server.</param>
    /// <returns>The outcome of the transfer.</returns>
    private static async Task<TransferOutcome> TransferAsync(
        TransferComposition composition,
        TestServer host)
    {
        using var destination = new MemoryStream();

        return await composition.Engine.TransferAsync(
            new Uri($"http://{host.BoundAddress}:{host.Port}{CatalogEndpoint.Route}"),
            startInclusive: 0,
            endInclusive: 7,
            destination,
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Headers of the correlation that the composition stamps in every request (AD-15,
    /// AD-23).
    /// </summary>
    private sealed class StubRequestHeaders : IClientRequestHeaders
    {
        public StubRequestHeaders(string? clientId, string? operationId = null)
        {
            ClientId = clientId;
            OperationId = operationId;
        }

        public string? ClientId { get; }

        public string? OperationId { get; }
    }
}
