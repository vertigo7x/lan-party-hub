using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LANPartyHub.Client.Infrastructure.Networking;
using LANPartyHub.Server.Host;
using LANPartyHub.Server.Infrastructure.Http;
using LANPartyHub.Shared.Contracts;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace LANPartyHub.IntegrationTests;

/// <summary>
/// The state of the connection that a client declares reaches the server through the
/// hub (AD-18, AD-22, AD-29, FR-10).
///
/// <para>
/// El test arranca el host REAL, con el Kestrel real y los sockets reales, y se conecta
/// al hub REAL con un cliente de SignalR real, porque el fallo que esta historia
/// repara NO es observable con una prueba estructural: el servidor registra el método
/// del hub con su nombre de .NET y NO recorta el sufijo <c>Async</c>, así que un
/// nombre distinto hace que el servidor responda «Method does not exist», el cliente
/// se lo come y la fila del cliente se queda en el estado que escribió el registro
/// (AD-16).
/// </para>
/// </summary>
public sealed class ClientStateHubTests
{
    private const string ClientId = "0193f2a1b2c34d4e5f60718293a4b5c6";

    [Fact]
    public async Task The_state_that_the_client_declares_reaches_the_server()
    {
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        using var http = host.CreateClient();

        using var registration = await http.SendAsync(
            BuildRegistrationRequest(),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, registration.StatusCode);

        await using var connection = new HubConnectionBuilder()
            .WithUrl(BuildHubUri(host))
            .Build();

        await connection.StartAsync(TestContext.Current.CancellationToken);

        // AD-18, AD-29: el nombre del método es el que declara el hub, y es el que el
        // cliente invoca. La invocación NO falla: un «Method does not exist» del
        // servidor significa que el estado se ha perdido.
        await connection.InvokeAsync(
            ClientStateHubPublisher.ClientStateChangedMethodName,
            new ClientStateChanged(ClientId, ConnectionState.Disconnected, [], []),
            TestContext.Current.CancellationToken);

        using var list = await http.SendAsync(
            BuildViewRequest(),
            TestContext.Current.CancellationToken);

        var clients = await ReadClientsAsync(list, TestContext.Current.CancellationToken);

        // AD-29, FR-10: la tabla del organizador refleja la desconexión que el cliente
        // declaró, y no el estado que escribió el registro.
        Assert.Equal(
            (int)ConnectionState.Disconnected,
            clients[0].GetProperty("connectionState").GetInt32());
    }

    [Fact]
    public async Task The_state_that_the_client_declares_does_not_change_the_name_of_the_team()
    {
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        using var http = host.CreateClient();

        using var registration = await http.SendAsync(
            BuildRegistrationRequest(),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, registration.StatusCode);

        await using var connection = new HubConnectionBuilder()
            .WithUrl(BuildHubUri(host))
            .Build();

        await connection.StartAsync(TestContext.Current.CancellationToken);

        await connection.InvokeAsync(
            ClientStateHubPublisher.ClientStateChangedMethodName,
            new ClientStateChanged(ClientId, ConnectionState.Disconnected, [], []),
            TestContext.Current.CancellationToken);

        using var list = await http.SendAsync(
            BuildViewRequest(),
            TestContext.Current.CancellationToken);

        var clients = await ReadClientsAsync(list, TestContext.Current.CancellationToken);

        // AD-30: el nombre efectivo es el único bajo el que la fila está registrada, y
        // el estado de la conexión no lo toca.
        Assert.Equal("EQ-PC", clients[0].GetProperty("machineName").GetString());
    }

    [Fact]
    public async Task The_state_of_a_client_that_is_not_registered_creates_no_row()
    {
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        using var http = host.CreateClient();

        await using var connection = new HubConnectionBuilder()
            .WithUrl(BuildHubUri(host))
            .Build();

        await connection.StartAsync(TestContext.Current.CancellationToken);

        await connection.InvokeAsync(
            ClientStateHubPublisher.ClientStateChangedMethodName,
            new ClientStateChanged(ClientId, ConnectionState.Disconnected, [], []),
            TestContext.Current.CancellationToken);

        using var list = await http.SendAsync(
            BuildViewRequest(),
            TestContext.Current.CancellationToken);

        var clients = await ReadClientsAsync(list, TestContext.Current.CancellationToken);

        // AD-30, AD-32: el alta del equipo es la operación de REGISTRO, así que un
        // estado de un cliente que no está registrado no crea ninguna fila.
        Assert.Equal(0, clients.GetArrayLength());
    }

    private static Uri BuildHubUri(TestServer host) =>
        new($"http://{host.BoundAddress}:{host.Port}{CatalogHub.Route}");

    private static HttpRequestMessage BuildRegistrationRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, ClientsEndpoint.Route);

        AddVersionHeaders(request);

        request.Content = JsonContent.Create(new
        {
            clientId = ClientId,
            machineName = "EQ-PC",
            clientVersion = "1.0.0",
            protocolVersion = VersionInfo.CurrentProtocolVersion,
        });

        return request;
    }

    private static HttpRequestMessage BuildViewRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, ClientsEndpoint.Route);

        AddVersionHeaders(request);

        return request;
    }

    private static void AddVersionHeaders(HttpRequestMessage request)
    {
        request.Headers.Add(CorrelationMiddleware.ClientIdHeaderName, ClientId);
        request.Headers.TryAddWithoutValidation(
            ClientVersionMiddleware.ClientVersionHeaderName,
            "1.0.0");
        request.Headers.TryAddWithoutValidation(
            ClientVersionMiddleware.ProtocolVersionHeaderName,
            VersionInfo.CurrentProtocolVersion.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
    }

    private static async Task<JsonElement> ReadClientsAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));

        return document.RootElement.Clone();
    }
}
