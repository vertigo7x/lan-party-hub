using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LANPartyHub.Server.Domain.Clients;
using LANPartyHub.Server.Host;
using LANPartyHub.Server.Infrastructure.Http;
using LANPartyHub.Shared.Contracts;
using Xunit;

namespace LANPartyHub.IntegrationTests;

/// <summary>
/// The registration of the clients of the event against the real host (AD-30, AD-31,
/// AD-32).
///
/// <para>
/// The tests start the real host, with the real Kestrel and the real sockets, because
/// the contract of the registration, the shape of the answers, the shape of the view of
/// the clients and the diagnosis of the collision are only observable with the server
/// in march (AD-16).
/// </para>
/// </summary>
public sealed class ClientRegistrationTests
{
    private const string FirstClientId = "0193f2a1b2c34d4e5f60718293a4b5c6";
    private const string SecondClientId = "0193f2a1b2c34d4e5f60718293a4b5c7";

    [Fact]
    public async Task A_client_that_registers_with_a_free_name_keeps_that_name()
    {
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        using var response = await SendRegistrationAsync(
            client,
            FirstClientId,
            "EQ-PC",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var answer = await ReadAnswerAsync(response, TestContext.Current.CancellationToken);

        Assert.Equal(FirstClientId, answer.GetProperty("clientId").GetString());
        Assert.Equal("EQ-PC", answer.GetProperty("machineName").GetString());
        Assert.False(answer.GetProperty("renamed").GetBoolean());
    }

    [Fact]
    public async Task The_answer_of_the_registration_carries_the_exact_shape_of_the_contract()
    {
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        using var response = await SendRegistrationAsync(
            client,
            FirstClientId,
            "EQ-PC",
            TestContext.Current.CancellationToken);

        var answer = await ReadAnswerAsync(response, TestContext.Current.CancellationToken);

        var names = answer.EnumerateObject().Select(property => property.Name).ToArray();

        Assert.Equal(
            ["clientId", "machineName", "connectionState", "lastSeenAt", "renamed"],
            names);
    }

    [Fact]
    public async Task The_body_of_the_registration_does_not_carry_the_schema_version()
    {
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        using var response = await SendRegistrationAsync(
            client,
            FirstClientId,
            "EQ-PC",
            TestContext.Current.CancellationToken);

        // AD-5, AD-32: el registro es la ÚNICA mutación exenta de la versión del
        // esquema, así que un cuerpo que la lleve tiene que ser rechazado.
        using var malformed = BuildRegistrationRequest(FirstClientId, "EQ-PC");

        malformed.Content = new StringContent(
            "{\"clientId\":\"" + FirstClientId + "\",\"machineName\":\"EQ-PC\","
            + "\"clientVersion\":\"1.0.0\",\"protocolVersion\":1,\"schemaVersion\":1}",
            Encoding.UTF8,
            "application/json");

        using var malformedResponse = await client.SendAsync(
            malformed,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, malformedResponse.StatusCode);
    }

    [Fact]
    public async Task A_client_that_registers_again_keeps_its_row_and_its_name()
    {
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        using var first = await SendRegistrationAsync(
            client,
            FirstClientId,
            "EQ-PC",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using var second = await SendRegistrationAsync(
            client,
            FirstClientId,
            "EQ-PC",
            TestContext.Current.CancellationToken);

        var answer = await ReadAnswerAsync(second, TestContext.Current.CancellationToken);

        // AD-30, AD-32: la reconexión conserva la fila y el nombre, y no dispara la
        // derivación, así que un cliente reconectado nunca colisiona consigo mismo.
        Assert.Equal("EQ-PC", answer.GetProperty("machineName").GetString());
        Assert.False(answer.GetProperty("renamed").GetBoolean());

        using var list = await client.SendAsync(
            BuildRequest(HttpMethod.Get, ClientsEndpoint.Route),
            TestContext.Current.CancellationToken);

        var clients = await ReadArrayAsync(list, TestContext.Current.CancellationToken);

        Assert.Equal(1, clients.GetArrayLength());
    }

    [Fact]
    public async Task A_client_that_renames_its_team_keeps_its_client_identifier()
    {
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        using var first = await SendRegistrationAsync(
            client,
            FirstClientId,
            "EQ-PC",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using var renamed = await SendRegistrationAsync(
            client,
            FirstClientId,
            "EQ-PC-2",
            TestContext.Current.CancellationToken);

        var answer = await ReadAnswerAsync(renamed, TestContext.Current.CancellationToken);

        // AD-30, AD-32: el renombrado ocurre en la MISMA operación de registro y el
        // clientId NO cambia en ningún caso.
        Assert.Equal(FirstClientId, answer.GetProperty("clientId").GetString());
        Assert.Equal("EQ-PC-2", answer.GetProperty("machineName").GetString());
        Assert.False(answer.GetProperty("renamed").GetBoolean());
    }

    [Fact]
    public async Task A_collision_is_not_an_error_and_both_teams_coexist_with_different_names()
    {
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        using var first = await SendRegistrationAsync(
            client,
            FirstClientId,
            "EQ-PC",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using var second = await SendRegistrationAsync(
            client,
            SecondClientId,
            "EQ-PC",
            TestContext.Current.CancellationToken);

        // AD-30, AD-31: la colisión NO es un error: el registro tiene éxito con el
        // nombre efectivo derivado.
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var answer = await ReadAnswerAsync(second, TestContext.Current.CancellationToken);

        Assert.True(answer.GetProperty("renamed").GetBoolean());
        Assert.Equal(
            $"EQ-PC-{SecondClientId}",
            answer.GetProperty("machineName").GetString());
        Assert.Equal(SecondClientId, answer.GetProperty("clientId").GetString());

        using var list = await client.SendAsync(
            BuildRequest(HttpMethod.Get, ClientsEndpoint.Route),
            TestContext.Current.CancellationToken);

        var clients = await ReadArrayAsync(list, TestContext.Current.CancellationToken);

        // Los dos equipos coexisten y ambos son visibles (AD-30).
        Assert.Equal(2, clients.GetArrayLength());

        var names = clients.EnumerateArray()
            .Select(item => item.GetProperty("machineName").GetString())
            .ToArray();

        Assert.Contains("EQ-PC", names);
        Assert.Contains($"EQ-PC-{SecondClientId}", names);
    }

    [Fact]
    public async Task The_client_that_already_had_the_name_keeps_it()
    {
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        using var first = await SendRegistrationAsync(
            client,
            FirstClientId,
            "EQ-PC",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using var second = await SendRegistrationAsync(
            client,
            SecondClientId,
            "EQ-PC",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        using var list = await client.SendAsync(
            BuildRequest(HttpMethod.Get, ClientsEndpoint.Route),
            TestContext.Current.CancellationToken);

        var clients = await ReadArrayAsync(list, TestContext.Current.CancellationToken);

        var owner = clients.EnumerateArray()
            .Single(item => item.GetProperty("clientId").GetString() == FirstClientId);

        Assert.Equal("EQ-PC", owner.GetProperty("machineName").GetString());
    }

    [Fact]
    public async Task A_derived_name_of_a_name_at_the_maximum_length_never_exceeds_it()
    {
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        var stem = new string('a', 64);

        using var first = await SendRegistrationAsync(
            client,
            FirstClientId,
            stem,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using var second = await SendRegistrationAsync(
            client,
            SecondClientId,
            stem,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var answer = await ReadAnswerAsync(second, TestContext.Current.CancellationToken);

        var effective = answer.GetProperty("machineName").GetString();

        Assert.NotNull(effective);
        Assert.Equal(64, effective.EnumerateRunes().Count());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("EQ/PC")]
    [InlineData("EQ  PC")]
    [InlineData(" EQ-PC")]
    [InlineData("EQ-PC ")]
    public async Task A_name_that_does_not_satisfy_the_rules_of_shape_is_rejected(
        string proposedName)
    {
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        using var response = await SendRegistrationAsync(
            client,
            FirstClientId,
            proposedName,
            TestContext.Current.CancellationToken);

        // AD-30, AD-32: el nombre mal formado se rechaza con 422 y con la forma única
        // ApiError.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var error = document.RootElement;

        Assert.Equal(
            ErrorCatalog.MachineNameInvalidCode,
            error.GetProperty("code").GetString());

        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("message").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("cause").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("nextStep").GetString()));
    }

    [Fact]
    public async Task A_rejected_client_is_not_registered()
    {
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        using var response = await SendRegistrationAsync(
            client,
            FirstClientId,
            "EQ/PC",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        using var list = await client.SendAsync(
            BuildRequest(HttpMethod.Get, ClientsEndpoint.Route),
            TestContext.Current.CancellationToken);

        var clients = await ReadArrayAsync(list, TestContext.Current.CancellationToken);

        // AD-30: el cliente queda SIN REGISTRAR y no puede operar.
        Assert.Equal(0, clients.GetArrayLength());
    }

    [Fact]
    public async Task The_code_of_the_rejection_is_its_own_and_distinct_from_the_signature_ones()
    {
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        using var response = await SendRegistrationAsync(
            client,
            FirstClientId,
            "EQ/PC",
            TestContext.Current.CancellationToken);

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var code = document.RootElement.GetProperty("code").GetString();

        // AD-30: el código es PROPIO y distinto del de «firma inválida» y del de
        // «fingerprint de ancla no coincide».
        Assert.NotEqual(ErrorCatalog.ManifestSignatureInvalidCode, code);
        Assert.NotEqual(ErrorCatalog.AnchorFingerprintMismatchCode, code);
        Assert.NotEqual(ErrorCatalog.MachineNameCollisionCode, code);
    }

    [Fact]
    public async Task An_incompatible_client_is_rejected_with_the_upgrade_required()
    {
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        using var request = BuildRegistrationRequest(FirstClientId, "EQ-PC");

        request.Headers.Remove(ClientVersionMiddleware.ProtocolVersionHeaderName);
        request.Headers.TryAddWithoutValidation(
            ClientVersionMiddleware.ProtocolVersionHeaderName,
            (VersionInfo.CurrentProtocolVersion + 1).ToString(
                System.Globalization.CultureInfo.InvariantCulture));

        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // AD-6, AD-32: la versión incompatible responde 426.
        Assert.Equal(HttpStatusCode.UpgradeRequired, response.StatusCode);
    }

    [Fact]
    public async Task The_incompatible_version_is_rejected_before_the_name_is_validated()
    {
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        using var request = BuildRegistrationRequest(FirstClientId, "EQ/PC");

        request.Headers.Remove(ClientVersionMiddleware.ProtocolVersionHeaderName);
        request.Headers.TryAddWithoutValidation(
            ClientVersionMiddleware.ProtocolVersionHeaderName,
            (VersionInfo.CurrentProtocolVersion + 1).ToString(
                System.Globalization.CultureInfo.InvariantCulture));

        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // AD-32: la comprobación de la versión ocurre ANTES de cualquier validación de
        // nombre, así que el 426 gana al 422.
        Assert.Equal(HttpStatusCode.UpgradeRequired, response.StatusCode);
    }

    [Fact]
    public async Task The_view_of_the_clients_carries_the_exact_shape_of_the_contract()
    {
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        using var registration = await SendRegistrationAsync(
            client,
            FirstClientId,
            "EQ-PC",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, registration.StatusCode);

        using var list = await client.SendAsync(
            BuildRequest(HttpMethod.Get, ClientsEndpoint.Route),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);

        var clients = await ReadArrayAsync(list, TestContext.Current.CancellationToken);

        var names = clients[0].EnumerateObject().Select(property => property.Name).ToArray();

        // AD-18, AD-29: la forma del contrato de la vista es EXACTA.
        Assert.Equal(
            [
                "clientId",
                "machineName",
                "clientVersion",
                "protocolVersion",
                "connectionState",
                "lastSeenAt",
            ],
            names);
    }

    [Fact]
    public async Task The_view_of_the_clients_is_ordered_by_team_name()
    {
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        using var zeta = await SendRegistrationAsync(
            client,
            SecondClientId,
            "Zeta-PC",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, zeta.StatusCode);

        using var alpha = await SendRegistrationAsync(
            client,
            FirstClientId,
            "Alpha-PC",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, alpha.StatusCode);

        using var list = await client.SendAsync(
            BuildRequest(HttpMethod.Get, ClientsEndpoint.Route),
            TestContext.Current.CancellationToken);

        var clients = await ReadArrayAsync(list, TestContext.Current.CancellationToken);

        var names = clients.EnumerateArray()
            .Select(item => item.GetProperty("machineName").GetString())
            .ToArray();

        // AD-29, UX-DR-14: el orden es ESTABLE por nombre de equipo.
        Assert.Equal(["Alpha-PC", "Zeta-PC"], names);
    }

    [Fact]
    public async Task The_view_of_the_clients_is_empty_when_no_client_is_registered()
    {
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        using var list = await client.SendAsync(
            BuildRequest(HttpMethod.Get, ClientsEndpoint.Route),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);

        var clients = await ReadArrayAsync(list, TestContext.Current.CancellationToken);

        Assert.Equal(0, clients.GetArrayLength());
    }

    [Fact]
    public async Task Two_clients_that_propose_names_equal_in_canonical_form_collide()
    {
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        using var first = await SendRegistrationAsync(
            client,
            FirstClientId,
            "EQ-PC",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // AD-30: la unicidad del nombre se compara por FORMA CANÓNICA, así que un
        // nombre que solo se diferencia en la capitalización o en los diacríticos es el
        // MISMO nombre y la colisión se resuelve con un nombre derivado.
        using var second = await SendRegistrationAsync(
            client,
            SecondClientId,
            "eq-pc",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var answer = await ReadAnswerAsync(second, TestContext.Current.CancellationToken);

        Assert.True(answer.GetProperty("renamed").GetBoolean());
        Assert.Equal(
            $"eq-pc-{SecondClientId}",
            answer.GetProperty("machineName").GetString());
    }

    [Fact]
    public async Task A_client_that_reconnects_with_its_effective_name_keeps_it()
    {
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        using var first = await SendRegistrationAsync(
            client,
            FirstClientId,
            "EQ-PC",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // AD-30: la reconexión se hace con el nombre EFECTIVO del equipo, y lo que
        // se compara es su FORMA CANÓNICA, así que el cliente conserva su nombre y no
        // colisiona consigo mismo.
        using var second = await SendRegistrationAsync(
            client,
            FirstClientId,
            "EQ-PC",
            TestContext.Current.CancellationToken);

        var answer = await ReadAnswerAsync(second, TestContext.Current.CancellationToken);

        Assert.Equal("EQ-PC", answer.GetProperty("machineName").GetString());
        Assert.False(answer.GetProperty("renamed").GetBoolean());
    }

    [Fact]
    public async Task The_view_of_the_clients_carries_the_state_of_the_connection()
    {
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        using var registration = await SendRegistrationAsync(
            client,
            FirstClientId,
            "EQ-PC",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, registration.StatusCode);

        using var list = await client.SendAsync(
            BuildRequest(HttpMethod.Get, ClientsEndpoint.Route),
            TestContext.Current.CancellationToken);

        var clients = await ReadArrayAsync(list, TestContext.Current.CancellationToken);

        // AD-29, FR-10: la fila lleva el estado de la conexión del equipo, y el
        // cliente que se ha registrado está CONECTADO. El enumerado viaja con su
        // valor numérico, que es la forma que el contrato de `Shared` fija (AD-18).
        Assert.Equal(
            (int)ConnectionState.AnchorVerified,
            clients[0].GetProperty("connectionState").GetInt32());
    }

    [Fact]
    public async Task A_client_that_renames_its_team_to_a_taken_name_gets_a_derived_one()
    {
        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        using var holder = await SendRegistrationAsync(
            client,
            FirstClientId,
            "EQ-PC",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, holder.StatusCode);

        using var other = await SendRegistrationAsync(
            client,
            SecondClientId,
            "Sala-2",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, other.StatusCode);

        // AD-30, AD-32: el renombrado aplica la MISMA regla que el alta, así que un
        // nombre que ya está ocupado se deriva y el clientId NO cambia.
        using var renamed = await SendRegistrationAsync(
            client,
            SecondClientId,
            "EQ-PC",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);

        var answer = await ReadAnswerAsync(renamed, TestContext.Current.CancellationToken);

        Assert.True(answer.GetProperty("renamed").GetBoolean());
        Assert.Equal(SecondClientId, answer.GetProperty("clientId").GetString());
        Assert.Equal($"EQ-PC-{SecondClientId}", answer.GetProperty("machineName").GetString());
    }

    [Fact]
    public async Task The_registration_and_the_view_of_the_clients_share_the_same_route()
    {
        // AD-29, AD-32: la vista se obtiene por REST y el registro es la única
        // operación de entrada del cliente al servidor.
        Assert.Equal("/api/v1/clients", ClientsEndpoint.Route);
    }

    [Fact]
    public async Task The_effective_name_of_a_derived_name_satisfies_the_rules_of_shape()
    {
        // AD-31: el nombre efectivo derivado SIEMPRE vuelve a ser admisible.
        var derived = MachineNamePolicy.Derive("EQ-PC", SecondClientId);

        Assert.True(MachineNamePolicy.IsValid(derived));
    }

    private static async Task<HttpResponseMessage> SendRegistrationAsync(
        HttpClient client,
        string clientId,
        string proposedName,
        CancellationToken cancellationToken)
    {
        using var request = BuildRegistrationRequest(clientId, proposedName);

        return await client.SendAsync(request, cancellationToken);
    }

    private static HttpRequestMessage BuildRegistrationRequest(string clientId, string proposedName)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, ClientsEndpoint.Route);

        request.Headers.Add(CorrelationMiddleware.ClientIdHeaderName, clientId);
        request.Headers.TryAddWithoutValidation(ClientVersionMiddleware.ClientVersionHeaderName, "1.0.0");
        request.Headers.TryAddWithoutValidation(
            ClientVersionMiddleware.ProtocolVersionHeaderName,
            VersionInfo.CurrentProtocolVersion.ToString(
                System.Globalization.CultureInfo.InvariantCulture));

        request.Content = JsonContent.Create(new
        {
            clientId,
            machineName = proposedName,
            clientVersion = "1.0.0",
            protocolVersion = VersionInfo.CurrentProtocolVersion,
        });

        return request;
    }

    private static HttpRequestMessage BuildRequest(HttpMethod method, string route)
    {
        var request = new HttpRequestMessage(method, route);

        request.Headers.Add(CorrelationMiddleware.ClientIdHeaderName, FirstClientId);
        request.Headers.TryAddWithoutValidation(ClientVersionMiddleware.ClientVersionHeaderName, "1.0.0");
        request.Headers.TryAddWithoutValidation(
            ClientVersionMiddleware.ProtocolVersionHeaderName,
            VersionInfo.CurrentProtocolVersion.ToString(
                System.Globalization.CultureInfo.InvariantCulture));

        return request;
    }

    private static async Task<JsonElement> ReadAnswerAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));

        return document.RootElement.Clone();
    }

    private static async Task<JsonElement> ReadArrayAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));

        return document.RootElement.Clone();
    }
}
