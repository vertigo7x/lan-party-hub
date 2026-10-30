using LANPartyHub.Shared.Contracts;
using Microsoft.AspNetCore.SignalR;

namespace LANPartyHub.Server.Host;

/// <summary>
/// Hub of the state of the client (AD-5, AD-26).
///
/// <para>
/// The hub lives in the route <c>/hubs/client-state</c> and it is the ONLY channel of push
/// from the server to the client (AD-5). The only event that travels server to client
/// through it in the story of the catalog is <see cref="CatalogChanged"/>, the trigger of
/// the resynchronization: it carries the version of the catalog and nothing else (AD-5,
/// AD-26).
/// </para>
/// <para>
/// The hub does not authenticate and does not authorize anybody: the LAN is trusted by
/// design and the only gating of the client is the compatibility of the version (AD-6).
/// The event carries no data of other clients, so a subscriber never learns anything about
/// the rest of the event (AD-29).
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
}
