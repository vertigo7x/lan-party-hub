using LANPartyHub.Server.Application.Clients;
using LANPartyHub.Shared.Contracts;
using Microsoft.AspNetCore.SignalR;

namespace LANPartyHub.Server.Host;

/// <summary>
/// Hub of the state of the client (AD-5, AD-26, AD-29).
///
/// <para>
/// The hub lives in the route <c>/hubs/client-state</c> and it is the ONLY channel of push
/// from the server to the client (AD-5). The only event that travels server to client
/// through it in the story of the catalog is <see cref="CatalogChanged"/>, the trigger of the
/// resynchronization: it carries the version of the catalog and nothing else (AD-5,
/// AD-26).
/// </para>
/// <para>
/// The hub does not authenticate and does not authorize anybody: the LAN is trusted by
/// design and the only gating of the client is the compatibility of the version (AD-6).
/// The event carries no data of other clients, so a subscriber never learns anything about
/// the rest of the event (AD-29).
/// </para>
/// <para>
/// AD-29: el hub NO publica ningún evento de servidor → cliente, y el
/// <c>ClientsSnapshotChanged</c> NO existe. El método que este hub declara va en la
/// dirección CONTRARIA —cliente → servidor— y recibe el estado que el propio cliente
/// declara, que es el canal sancionado por AD-18 y AD-29 para el
/// <c>connectionState</c> de un cliente (AD-22).
/// </para>
/// </summary>
public sealed class CatalogHub : Hub
{
    /// <summary>Route of the hub of the state of the client.</summary>
    public const string Route = "/hubs/client-state";

    /// <summary>
    /// Name of the event that triggers the resynchronization of the catalog (AD-5).
    /// </summary>
    public const string CatalogChangedEventName = nameof(CatalogChanged);

    /// <summary>
    /// Name of the method that recibe el estado que declara el cliente (AD-18, AD-29).
    /// </summary>
    public const string ClientStateChangedMethodName = nameof(ClientStateChanged);

    /// <summary>
    /// Receives the state that the client declares (AD-18, AD-22, AD-29).
    ///
    /// <para>
    /// La transición del <c>connectionState</c> de un <c>ClientRegistration</c> es un
    /// hecho que el servidor PERSISTE, para que la tabla de clientes refleje la
    /// conexión y la desconexión de cada equipo (AD-29, FR-10).
    /// </para>
    /// <para>
    /// El método NO lleva ningún dato de OTRO cliente: el <c>clientId</c> del payload
    /// es el del propio cliente que publica, y el servidor no acepta una publicación
    /// que nombre a otro (AD-29).
    /// </para>
    /// </summary>
    /// <param name="state">State that the client declares.</param>
    /// <param name="reportClientState">Use case that persists the state of the client.</param>
    /// <returns>A task that completes when the state has been persisted.</returns>
    public async Task ReportClientStateAsync(
        ClientStateChanged state,
        ReportClientState reportClientState)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(reportClientState);

        await reportClientState
            .ExecuteAsync(state, Context.ConnectionAborted)
            .ConfigureAwait(false);
    }
}
