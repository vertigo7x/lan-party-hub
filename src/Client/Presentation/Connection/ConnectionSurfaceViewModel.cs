using LANPartyHub.Client.Application.Connection;
using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Client.Presentation.Connection;

/// <summary>
/// View model of the surface of the connection of the player (AC1, AC2, AC3, AC4, FR-3,
/// UX-DR-30, UX-DR-36).
///
/// <para>
/// The view model holds the servers that the discovery found and the microcopy of the
/// state in which the network did not return any (AC3). The rule of the discovery lives in
/// the Application layer and this view model only holds the state of the surface and its
/// texts (AD-1).
/// </para>
/// <para>
/// The manual address is a PERMANENT and equivalent way to reach the server: the field of
/// the address is offered in EVERY state, it confirms with «Enter» and its result is the
/// state of the connection with the three part message of the error when it fails (AC1,
/// AC2, AC3, AD-3).
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

    /// <summary>
    /// Text of the label of the field of the address (AC1, UX-DR-9).
    /// </summary>
    public const string AddressLabelText = "Dirección del servidor";

    /// <summary>
    /// Text of the placeholder of the field of the address (AC1, UX-DR-9).
    /// </summary>
    public const string AddressPlaceholderText = "host:puerto";

    /// <summary>
    /// Text of the title of the notice of the error (AC3, UX-DR-36).
    /// </summary>
    public const string ErrorNoticeTitleText = "No se pudo conectar con el servidor";

    /// <summary>
    /// Text of the label of the cause inside the notice of the error (AC3, UX-DR-36).
    /// </summary>
    public const string ErrorCauseLabelText = "Por qué: ";

    /// <summary>
    /// Text of the label of the next step inside the notice of the error (AC3, UX-DR-36).
    /// </summary>
    public const string ErrorNextStepLabelText = "Qué hacer ahora: ";

    /// <summary>
    /// Text of the label of the address that is in use (AC1).
    /// </summary>
    public const string AddressInUseLabelText = "Servidor en uso: ";

    private readonly List<ServerEndpoint> _servers = [];

    /// <summary>
    /// Raised when the surface has to be rendered again, because the outcome of the
    /// discovery or the result of the manual connection changed.
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
    /// Address that the player typed in the field of the connection (AC1).
    /// </summary>
    public string Address { get; private set; } = string.Empty;

    /// <summary>
    /// State of the connection with the server. It starts DISCONNECTED, because the player
    /// has not chosen any server yet (AC1, AD-27).
    /// </summary>
    public ConnectionState ConnectionState { get; private set; } = ConnectionState.Disconnected;

    /// <summary>
    /// Text of the state of the connection, taken from the single table of microcopy of the
    /// system (AD-25, AD-27).
    /// </summary>
    public string ConnectionStateText => StateDisplayMap.GetText(ConnectionState);

    /// <summary>
    /// Endpoint of the server that the client is connected to, or <c>null</c> when the
    /// client is not connected to any (AC1).
    /// </summary>
    public ServerEndpoint? EndpointInUse { get; private set; }

    /// <summary>
    /// Text of the address that is in use, or an empty text when the client is not
    /// connected to any server (AC1).
    /// </summary>
    public string AddressInUseText =>
        EndpointInUse is null
            ? string.Empty
            : $"{AddressInUseLabelText}{EndpointInUse.Address}:{EndpointInUse.Port}";

    /// <summary>
    /// Last error of the manual connection, or <c>null</c> when the last attempt succeeded
    /// (AC3).
    /// </summary>
    public ApiError? LastError { get; private set; }

    /// <summary>
    /// Whether the surface has to show the notice of the error (AC3, UX-DR-36).
    /// </summary>
    public bool ShowsError => LastError is not null;

    /// <summary>
    /// Text of the notice of the error with its three parts: what failed, why and what to
    /// do now. The internal code is NEVER the primary text (AC3, UX-DR-36, AR-4).
    /// </summary>
    public string ErrorNoticeText =>
        LastError is null
            ? string.Empty
            : $"{LastError.Message}{Environment.NewLine}" +
              $"{ErrorCauseLabelText}{LastError.Cause}{Environment.NewLine}" +
              $"{ErrorNextStepLabelText}{LastError.NextStep}";

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

    /// <summary>
    /// Records the address that the player typed (AC1).
    ///
    /// <param name="address">Text of the address, in the form <c>host:puerto</c>.</param>
    public void SetAddress(string? address)
    {
        Address = address ?? string.Empty;
    }

    /// <summary>
    /// Applies the result of the manual connection to the surface (AC1, AC3).
    ///
    /// <para>
    /// A connection that opened leaves the surface connected to that endpoint and without
    /// error. A connection that failed leaves the surface disconnected and with the error
    /// of the catalog, whose code NEVER becomes the primary text (AC3, UX-DR-36).
    /// </para>
    /// </summary>
    /// <param name="attempt">Result of the attempt of the connection.</param>
    public void ApplyConnection(ConnectionAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        ConnectionState = attempt.State;
        EndpointInUse = attempt.IsConnected ? attempt.Endpoint : null;
        LastError = attempt.Error;

        Changed?.Invoke();
    }
}
