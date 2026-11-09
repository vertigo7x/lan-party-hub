using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LANPartyHub.Client.Application.CatalogSync;
using LANPartyHub.Client.Application.Clients;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Client.Infrastructure.Networking;

/// <summary>
/// Adapter of the registration of the client against the event server (AD-30, AD-32).
///
/// <para>
/// El adaptador habla HTTP con el servidor del evento y llama a la ruta
/// <c>POST /api/v1/clients</c>, que es el ÚNICO punto de entrada del cliente al
/// servidor: el alta, la reconexión y el renombrado ocurren en ella (AD-32).
/// </para>
/// <para>
/// El cuerpo lleva la forma exacta <c>{ clientId, machineName, clientVersion,
/// protocolVersion }</c> y NO lleva <c>schemaVersion</c>: el registro es la única
/// mutación exenta de la versión del esquema (AD-5, AD-32).
/// </para>
/// <para>
/// Un rechazo por nombre mal formado llega como la forma única <c>ApiError</c> del
/// catálogo de errores, con su código, su causa y su siguiente paso: el cliente lo
/// presenta tal cual y NO reintenta con el mismo nombre, porque el rechazo significa
/// que el cliente queda sin registrar y no puede operar (AD-30, AD-32).
/// </para>
/// </summary>
public sealed class ClientRegistrationHttpClient : IClientRegistrationClient
{
    /// <summary>Route of the registration of the clients of the event.</summary>
    public const string Route = "/api/v1/clients";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;

    /// <summary>Creates the adapter of the registration against the server.</summary>
    /// <param name="httpClient">
    /// Client of the server of the event, with its base address already set.
    /// </param>
    public ClientRegistrationHttpClient(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task<ClientRegistrationOutcome> RegisterAsync(
        string clientId,
        string machineName,
        string clientVersion,
        int protocolVersion,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(machineName);

        using var response = await _httpClient
            .PostAsJsonAsync(
                Route,
                new ClientRegistrationBody(
                    clientId,
                    machineName,
                    clientVersion,
                    protocolVersion),
                SerializerOptions,
                cancellationToken)
            .ConfigureAwait(false);

        // AD-30, AD-32: el rechazo por nombre mal formado llega con la forma única
        // ApiError, y el cliente lo propaga con su código, su causa y su siguiente
        // paso. El cliente NO reintenta con el mismo nombre: el rechazo significa que
        // queda sin registrar y no puede operar.
        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var error = await response.Content
                .ReadFromJsonAsync<ApiError>(SerializerOptions, cancellationToken)
                .ConfigureAwait(false);

            throw new ClientRegistrationRejectedException(
                error ?? ErrorCatalog.Resolve(ErrorCatalog.MachineNameInvalidCode));
        }

        // AD-6, FR-4: el servidor rechazó la versión del cliente. El cliente NO
        // descarga, NO instala y NO opera: la incompatibilidad es su propia falla.
        if (response.StatusCode == HttpStatusCode.UpgradeRequired)
        {
            throw new ClientVersionIncompatibleException(
                clientVersion,
                ReadServerVersion(response));
        }

        response.EnsureSuccessStatusCode();

        var answer = await response.Content
            .ReadFromJsonAsync<ClientRegistrationAnswer>(SerializerOptions, cancellationToken)
            .ConfigureAwait(false);

        if (answer is null)
        {
            throw new InvalidOperationException(
                "El servidor no devolvió la respuesta del registro del cliente.");
        }

        return new ClientRegistrationOutcome(
            answer.ClientId,
            answer.MachineName,
            answer.ConnectionState,
            answer.LastSeenAt,
            answer.Renamed);
    }

    /// <summary>
    /// Reads the version of the server that published in its response, or <c>null</c>
    /// when the header no llega.
    /// </summary>
    /// <param name="response">Response of the server.</param>
    /// <returns>The version of the server, or <c>null</c>.</returns>
    private static string? ReadServerVersion(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("X-Server-Version", out var values))
        {
            return null;
        }

        var list = values.ToArray();

        if (list.Length != 1)
        {
            return null;
        }

        var value = list[0]?.Trim();

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}

/// <summary>
/// Body of the registration of the client, with the EXACT shape that the contract
/// fixes (AD-32).
///
/// <para>
/// El cuerpo NO lleva <c>schemaVersion</c>: el registro es la única mutación exenta
/// de la versión del esquema (AD-5, AD-32).
/// </para>
/// <para>
/// El cuerpo NO lleva tampoco el estado de conexión: la forma del cuerpo es EXACTA y
/// el estado lo publica el cliente en el evento <c>ClientStateChanged</c> del hub
/// (AD-18, AD-29, AD-32).
/// </para>
/// </summary>
/// <param name="ClientId">Canonical identifier of the client.</param>
/// <param name="MachineName">Name proposed for the team.</param>
/// <param name="ClientVersion">Version of the binary of the client.</param>
/// <param name="ProtocolVersion">Protocol version that the client speaks.</param>
internal sealed record ClientRegistrationBody(
    string ClientId,
    string MachineName,
    string ClientVersion,
    int ProtocolVersion);

/// <summary>
/// Answer of the registration of the client, with the EXACT shape that the contract
/// fixes (AD-32).
/// </summary>
/// <param name="ClientId">Canonical identifier of the client.</param>
/// <param name="MachineName">Effective name of the team.</param>
/// <param name="ConnectionState">Connection state that the row holds.</param>
/// <param name="LastSeenAt">Instant of the contact, in UTC.</param>
/// <param name="Renamed">Whether the server resolved a collision with a derived name.</param>
internal sealed record ClientRegistrationAnswer(
    string ClientId,
    string MachineName,
    ConnectionState ConnectionState,
    DateTimeOffset LastSeenAt,
    bool Renamed);
