using LANPartyHub.Client.Application.Connection;
using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Client.Presentation.Connection;

/// <summary>
/// View model of the surface of the connection of the player (AC1, AC3, AC4, FR-3,
/// UX-DR-30).
///
/// <para>
/// The view model holds the servers that the discovery found and the microcopy of the
/// state in which the network did not return any (AC3). The rule of the discovery lives in
/// the Application layer and this view model only holds the state of the surface and its
/// texts (AD-1).
/// </para>
/// <para>
/// The manual address is a PERMANENT and equivalent way to reach the server, so the action
/// «Introducir dirección manualmente» is offered in EVERY state in which there is no
/// server to choose, and it is never hidden behind an error (AC3, AC4, AD-3).
/// </para>
/// </summary>
public sealed class ConnectionSurfaceViewModel
{
    /// <summary>
    /// Text that the surface shows when the network did not return any server (AC3,
    /// UX-DR-30).
    /// </summary>
    public const string NoServersFoundText = "No se encontró ningún servidor en esta red.";

    /// <summary>
    /// Text of the primary action that offers the manual address when there is no server
    /// to choose (AC3, UX-DR-30).
    /// </summary>
    public const string ManualAddressText = "Introducir dirección manualmente";

    /// <summary>
    /// Text that the surface shows when the policy of the network blocked the discovery
    /// (AC4).
    /// </summary>
    public const string DiscoveryBlockedText =
        "El descubrimiento automático está bloqueado en esta red.";

    private readonly List<ServerEndpoint> _servers = [];

    /// <summary>
    /// Raised when the surface has to be rendered again, because the outcome of the
    /// discovery changed.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// Servers that the discovery found and that the player can choose (AC1).
    /// </summary>
    public IReadOnlyList<ServerEndpoint> Servers => _servers;

    /// <summary>
    /// Whether the surface holds at least one server to choose (AC1).
    /// </summary>
    public bool HasServers => _servers.Count > 0;

    /// <summary>
    /// Whether the discovery was blocked by the policy of the network (AC4).
    /// </summary>
    public bool IsDiscoveryBlocked { get; private set; }

    /// <summary>
    /// Text that the surface shows when there is no server to choose (AC3, AC4).
    ///
    /// <para>
    /// It is EMPTY when there is a server to choose: the text belongs al estado vacío y no
    /// se muestra junto a una lista de servidores (AC1, AC3).
    /// </para>
    /// </summary>
    public string EmptyMessage =>
        _servers.Count > 0
            ? string.Empty
            : IsDiscoveryBlocked
                ? DiscoveryBlockedText
                : NoServersFoundText;

    /// <summary>
    /// Text of the primary action of the surface: the manual address is offered in EVERY
    /// state in which there is no server to choose (AC3, AC4).
    /// </summary>
    public string PrimaryActionText => ManualAddressText;

    /// <summary>
    /// Whether the surface has to show its state in which there is no server to choose
    /// (AC3, AC4).
    /// </summary>
    public bool ShowsEmptyState => _servers.Count == 0;

    /// <summary>
    /// Applies the outcome of the discovery to the surface (AC1, AC3, AC4).
    ///
    /// <para>
    /// A search that found servers fills the list of the surface, and a search that found
    /// nothing OR that the network blocked empties it. The blocked search is NOT an error:
    /// the manual address keeps being offered (AC4).
    /// </para>
    /// </summary>
    /// <param name="result">Outcome of the discovery and the servers that answered it.</param>
    public void ApplyDiscovery(DiscoveryResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        _servers.Clear();
        _servers.AddRange(result.Servers);

        IsDiscoveryBlocked = result.Outcome == DiscoveryOutcome.Blocked;

        Changed?.Invoke();
    }
}
