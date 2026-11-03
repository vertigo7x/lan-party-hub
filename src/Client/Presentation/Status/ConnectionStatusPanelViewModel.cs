using LANPartyHub.Client.Application.Connection;

namespace LANPartyHub.Client.Presentation.Status;

/// <summary>
/// View model of the PERSISTENT panel of the connection (AC1, AC2, AC3, AC4, UX-DR-11,
/// UX-DR-23, UX-DR-29).
///
/// <para>
/// El panel es PERSISTENTE: no se colapsa, no se esconde en un menú ni en un modal, y
/// vive en una columna fija de la ventana (AC1).
/// </para>
/// <para>
/// El panel muestra el PUNTO DE PRESENCIA, el nombre del servidor, la dirección en uso, la
/// versión del servidor y la última operación terminada (AC1, AC2, UX-DR-11, UX-DR-23).
/// </para>
/// <para>
/// Cuando el servidor no está disponible, el punto de presencia pasa a
/// <c>state-warning</c>, el panel muestra «Servidor no disponible. Se conserva el último
/// catálogo sincronizado.» y el reintento con su contador «Reintentando · 3 s», y NO se
/// abre ningún modal que interrumpa (AC3, AC4, UX-DR-29).
/// </para>
/// <para>
/// La microcopia del panel es del CLIENTE y no de <c>Shared</c>: la tabla de microcopia de
/// <c>Shared</c> no crece, porque el estado de la disponibilidad del servidor es un estado
/// DEL CLIENTE (AR-3, AD-18, AD-33).
/// </para>
/// </summary>
public sealed class ConnectionStatusPanelViewModel
{
    /// <summary>
    /// Text of the notice that the server is not available (AC3, UX-DR-29).
    ///
    /// <para>
    /// El texto dice lo que el cliente CONSERVA: el último catálogo sincronizado. El
    /// jugador no pierde nada por la caída (AC3).
    /// </para>
    /// </summary>
    public const string ServerUnavailableText =
        "Servidor no disponible. Se conserva el último catálogo sincronizado.";

    /// <summary>
    /// Text of the point of presence when the server IS available (AC1).
    /// </summary>
    public const string PresenceAvailableText = "Conectado";

    /// <summary>
    /// Text of the point of presence when the server is NOT available (AC3, AC4).
    /// </summary>
    public const string PresenceUnavailableText = "Sin conexión";

    /// <summary>
    /// Text of the label of the point of presence (AC1, UX-DR-11).
    /// </summary>
    public const string PresenceLabelText = "Presencia";

    /// <summary>
    /// Text of the label of the name of the server (AC1, UX-DR-11).
    /// </summary>
    public const string ServerNameLabelText = "Servidor";

    /// <summary>
    /// Text of the label of the address that is in use (AC1, UX-DR-11).
    /// </summary>
    public const string AddressLabelText = "Dirección";

    /// <summary>
    /// Text of the label of the version of the server (AC1, UX-DR-11).
    /// </summary>
    public const string ServerVersionLabelText = "Versión del servidor";

    /// <summary>
    /// Text of the label of the last operation that finished (AC2, UX-DR-11).
    /// </summary>
    public const string LastOperationLabelText = "Última operación";

    /// <summary>
    /// Text that the panel shows when the client has no finished operation yet
    /// (AC2, UX-DR-11).
    ///
    /// <para>
    /// El panel NUNCA muestra un espacio vacío: sin operaciones todavía dice exactamente
    /// eso (AC2).
    /// </para>
    /// </summary>
    public const string NoOperationsYetText = "Sin operaciones todavía";

    /// <summary>
    /// Text of the prefix of the countdown of the retry (AC4, UX-DR-29).
    /// </summary>
    public const string RetryingPrefixText = "Reintentando";

    /// <summary>
    /// Text that the panel shows when the server name is not known yet (AC1).
    /// </summary>
    public const string UnknownServerNameText = "(desconocido)";

    /// <summary>
    /// Text that the panel shows when the version of the server is not known yet (AC1).
    /// </summary>
    public const string UnknownServerVersionText = "(desconocida)";

    private ServerAvailability _availability = new(
        Endpoint: null,
        ServerName: null,
        ServerVersion: null,
        Available: false);

    /// <summary>
    /// Raised when the panel has to be rendered again, because the availability of the
    /// server changed (AC3, AC4).
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// The last availability that the panel shows (AC1).
    /// </summary>
    public ServerAvailability Availability => _availability;

    /// <summary>
    /// Whether the server is available (AC1, AC3).
    /// </summary>
    public bool IsAvailable => _availability.Available;

    /// <summary>
    /// Text of the point of presence: «Conectado» o «Sin conexión» (AC1, AC3).
    /// </summary>
    public string PresenceText => IsAvailable ? PresenceAvailableText : PresenceUnavailableText;

    /// <summary>
    /// Text of the notice of the unavailable server, or an empty text when the server IS
    /// available (AC3, UX-DR-29).
    /// </summary>
    public string UnavailableNoticeText => IsAvailable ? string.Empty : ServerUnavailableText;

    /// <summary>
    /// Whether the panel has to show the notice of the unavailable server (AC3).
    /// </summary>
    public bool ShowsUnavailableNotice => !IsAvailable;

    /// <summary>
    /// Text of the name of the server, or the unknown text when the server did not publish
    /// it (AC1).
    /// </summary>
    public string ServerNameText =>
        string.IsNullOrWhiteSpace(_availability.ServerName)
            ? UnknownServerNameText
            : _availability.ServerName;

    /// <summary>
    /// Text of the address that is in use, or an empty text when the client is not
    /// connected to any server (AC1).
    /// </summary>
    public string AddressText =>
        _availability.Endpoint is null
            ? string.Empty
            : $"{_availability.Endpoint.Address}:{_availability.Endpoint.Port}";

    /// <summary>
    /// Text of the version of the server, or the unknown text when the server did not
    /// publish it (AC1).
    /// </summary>
    public string ServerVersionText =>
        string.IsNullOrWhiteSpace(_availability.ServerVersion)
            ? UnknownServerVersionText
            : _availability.ServerVersion;

    /// <summary>
    /// Text of the last operation that finished, or the text of «no operations yet» when
    /// the client has none (AC2, UX-DR-11).
    ///
    /// <para>
    /// El panel NUNCA deja un espacio vacío: sin operaciones todavía dice exactamente eso
    /// (AC2).
    /// </para>
    /// </summary>
    public string LastOperationText =>
        string.IsNullOrWhiteSpace(_availability.LastOperation)
            ? NoOperationsYetText
            : _availability.LastOperation;

    /// <summary>
    /// Text of the countdown of the retry, «Reintentando · 3 s», or an empty text when the
    /// server is available (AC4, UX-DR-29).
    /// </summary>
    public string RetryCountdownText
    {
        get
        {
            if (IsAvailable || _availability.RetryCountdown is null)
            {
                return string.Empty;
            }

            var seconds = (int)Math.Ceiling(_availability.RetryCountdown.Value.TotalSeconds);

            return $"{RetryingPrefixText} · {seconds} s";
        }
    }

    /// <summary>
    /// Whether the panel has to show the countdown of the retry (AC4).
    /// </summary>
    public bool ShowsRetryCountdown => !IsAvailable && _availability.RetryCountdown is not null;

/// <summary>
    /// Applies the availability of the server to the panel (AC1, AC3, AC4).
    /// </summary>
    /// <param name="availability">Availability of the server of the event.</param>
    public void ApplyAvailability(ServerAvailability availability)
    {
        ArgumentNullException.ThrowIfNull(availability);

        _availability = availability;

        Changed?.Invoke();
    }

    /// <summary>
    /// Records the last operation that finished, so the panel shows su resultado en vez de
    /// un espacio vacío (AC2, UX-DR-11).
    ///
    /// <param name="description">Description of the last operation that finished.</param>
    public void ApplyLastOperation(string? description)
    {
        _availability = _availability with { LastOperation = description };

        Changed?.Invoke();
    }
}
