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

    /// <summary>
    /// Text that the surface shows when the server rejected the client because its version
    /// is not compatible with it (AD-6, FR-4, UX-DR-31).
    /// </summary>
    public const string IncompatibleVersionText =
        "Este cliente ({0}) no es compatible con el servidor ({1}). Actualiza el cliente.";

    /// <summary>
    /// Text of the title of the notice of the incompatibility (AD-6, UX-DR-31).
    /// </summary>
    public const string IncompatibleVersionTitleText = "El cliente no es compatible con el servidor";

    /// <summary>
    /// Text of the label of the field of the fingerprint esperado (AC2, AD-27).
    /// </summary>
    public const string ExpectedFingerprintLabelText = "Fingerprint del ancla de confianza";

    /// <summary>
    /// Text of the placeholder of the field of the fingerprint esperado (AC2, AD-27).
    /// </summary>
    public const string ExpectedFingerprintPlaceholderText =
        "El que te dio el organizador (QR o pantalla)";

    /// <summary>
    /// Text of the label of the fingerprint que el servidor presenta (AC2, AD-27).
    /// </summary>
    public const string PresentedFingerprintLabelText = "Fingerprint que presenta el servidor: ";

    /// <summary>
    /// Text of the label of the fingerprint que ya está fijado (AD-27).
    /// </summary>
    public const string FixedFingerprintLabelText = "Fingerprint ya fijado: ";

    /// <summary>
    /// Text of the title of the notice of the ancla sin verificar (AC7, AD-27).
    ///
    /// <para>
    /// AD-27: el estado «Ancla sin verificar» NO es un error de red: es un estado de
    /// SEGURIDAD, y la UI lo dice de forma explícita. El cliente no lo degrada a
    /// «conectado» por haber descargado antes el catálogo (AC7, AD-27).
    /// </para>
    /// </summary>
    public const string AnchorUnverifiedTitleText = "Ancla de confianza sin verificar";

    /// <summary>
    /// Text that the surface shows when the ancla de confianza NO está fijada (AC7,
    /// AD-27).
    ///
    /// <para>
    /// AD-27: en este estado el cliente NO descarga ni instala nada, y la UI lo dice de
    /// forma explícita, porque el jugador tiene que saber por qué no puede operar.
    /// </para>
    /// </summary>
    public const string AnchorUnverifiedText =
        "No has fijado todavía el ancla de confianza. Pide al organizador el fingerprint del ancla "
        + "e introdúcelo aquí: hasta entonces no se descarga ni se instala nada.";

    /// <summary>
    /// Text of the title of the notice of la ancla que no coincide (AC2, AD-27).
    /// </summary>
    public const string AnchorMismatchTitleText = "El ancla de confianza no coincide";

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
    /// Version of the client when the server rejected it as incompatible, or
    /// <c>null</c> when there is no incompatibility (AD-6).
    /// </summary>
    public string? IncompatibleClientVersion { get; private set; }

    /// <summary>
    /// Version of the server when it rejected the client as incompatible, or
    /// <c>null</c> when there is no incompatibility (AD-6).
    /// </summary>
    public string? IncompatibleServerVersion { get; private set; }

    /// <summary>
    /// Whether the surface has to show the diagnostic de la incompatibilidad (AD-6,
    /// UX-DR-31).
    /// </summary>
    public bool ShowsIncompatibleVersion => IncompatibleClientVersion is not null;

    /// <summary>
    /// The fingerprint esperado that the player typed or that the QR carried (AC2,
    /// AD-27).
    ///
    /// <para>
    /// AD-27: este valor llega por un canal FUERA de banda. El cliente nunca lo toma del
    /// servidor, y un valor que llega por la LAN no puede convertirse en ancla por sí
    /// mismo (AD-27).
    /// </para>
    /// </summary>
    public string ExpectedFingerprint { get; private set; } = string.Empty;

    /// <summary>
    /// The fingerprint that the server presented, when the client already fixed an ancla
    /// and the server presented a DIFFERENT one (AC6, AD-27).
    /// </summary>
    public string? PresentedAnchorFingerprint { get; private set; }

    /// <summary>
    /// The fingerprint that the client has fixed, when the server presented a different
    /// one (AC6, AD-27).
    /// </summary>
    public string? FixedAnchorFingerprint { get; private set; }

    /// <summary>
    /// Whether the surface has to show the notice de la ancla sin verificar (AC7, AD-27).
    ///
    /// <para>
    /// AD-27: el estado «Ancla sin verificar» es un estado de seguridad, no un error de
    /// red, y la UI lo dice de forma explícita (AC7, AD-27).
    /// </para>
    /// </summary>
    public bool ShowsAnchorUnverified => ConnectionState == ConnectionState.AnchorUnverified;

    /// <summary>
    /// Whether the surface has to show the notice de la ancla que no coincide (AC6,
    /// AD-27).
    /// </summary>
    public bool ShowsAnchorMismatch => PresentedAnchorFingerprint is not null;

    /// <summary>
    /// Text of la ancla que no coincide: el fingerprint que presentó el servidor y el que
    /// ya está fijado, para que el jugador vea la diferencia (AC6, AD-27).
    /// </summary>
    public string AnchorMismatchText =>
        PresentedAnchorFingerprint is null
            ? string.Empty
            : $"{PresentedFingerprintLabelText}{PresentedAnchorFingerprint}{Environment.NewLine}" +
              $"{FixedFingerprintLabelText}{FixedAnchorFingerprint}";


    /// <summary>
    /// Text of the diagnostic de la incompatibilidad: la versión del cliente, la del
    /// servidor y el siguiente paso (AD-6, UX-DR-31).
    ///
    /// <para>
    /// El texto es EXACTO: «Este cliente (1.2) no es compatible con el servidor (1.4).
    /// Actualiza el cliente.» El código interno NO aparece nunca como texto principal
    /// (AD-15, AR-4).
    /// </para>
    /// </summary>
    public string IncompatibleVersionMessage =>
        IncompatibleClientVersion is null
            ? string.Empty
            : string.Format(
                IncompatibleVersionText,
                IncompatibleClientVersion,
                IncompatibleServerVersion ?? "desconocida");

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
    /// </summary>
    /// <param name="address">Text of the address, in the form <c>host:puerto</c>.</param>
    public void SetAddress(string? address)
    {
        Address = address ?? string.Empty;
    }

    /// <summary>
    /// Records the fingerprint esperado that the player typed or that the QR carried
    /// (AC2, AD-27).
    ///
    /// <para>
    /// AD-27: este valor es el que llega FUERA de banda. La superficie solo lo guarda: la
    /// comparación y el rechazo los hace el caso de uso, que es el único que escribe
    /// (AC2, AD-27).
    /// </para>
    /// </summary>
    /// <param name="fingerprint">Fingerprint as the player transported it.</param>
    public void SetExpectedFingerprint(string? fingerprint)
    {
        ExpectedFingerprint = fingerprint ?? string.Empty;
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

        if (attempt.IsVersionIncompatible)
        {
            IncompatibleClientVersion = attempt.IncompatibleClientVersion;
            IncompatibleServerVersion = attempt.IncompatibleServerVersion;
        }

        if (attempt.IsAnchorMismatch)
        {
            // AC6, AD-27: el servidor presentó un ancla que NO es la fijada. La superficie
            // muestra los dos fingerprints para que el jugador vea la diferencia, y el
            // ancla fijada NO se sustituye.
            PresentedAnchorFingerprint = attempt.PresentedAnchorFingerprint;
            FixedAnchorFingerprint = attempt.FixedAnchorFingerprint;
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// Applies the fingerprint que el servidor presentó y el que ya está fijado, cuando no
    /// coinciden (AC6, AD-27).
    /// </summary>
    /// <param name="presentedFingerprint">Fingerprint that the server presented.</param>
    /// <param name="fixedFingerprint">Fingerprint that the client has fixed.</param>
    public void ApplyAnchorMismatch(string presentedFingerprint, string fixedFingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(presentedFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(fixedFingerprint);

        PresentedAnchorFingerprint = presentedFingerprint;
        FixedAnchorFingerprint = fixedFingerprint;

        // El cliente con un ancla que no coincide NO opera: el estado queda
        // DESCONECTADO (AC6, AD-27).
        ConnectionState = ConnectionState.Disconnected;
        EndpointInUse = null;

        Changed?.Invoke();
    }

    /// <summary>
    /// Applies the state of an ancla de confianza que ya está fijada y coincide con la
    /// que presenta el servidor (AD-27).
    ///
    /// <para>
    /// AD-27: con el ancla fijada y validada contra el valor PERSISTIDO, la conexión pasa
    /// a «Ancla verificada» y el cliente puede validar los manifiestos firmados (AD-2,
    /// AD-27).
    /// </para>
    /// </summary>
    public void ApplyAnchorVerified()
    {
        ConnectionState = ConnectionState.AnchorVerified;
        PresentedAnchorFingerprint = null;
        FixedAnchorFingerprint = null;

        Changed?.Invoke();
    }

    /// <summary>
    /// Applies the fingerprint que el servidor presentó, para que la superficie lo muestre
    /// junto al esperado ANTES de que el jugador confirme (AC2, AD-27).
    /// </summary>
    /// <param name="presentedFingerprint">Fingerprint that the server presented.</param>
    public void ApplyPresentedFingerprint(string presentedFingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(presentedFingerprint);

        PresentedAnchorFingerprint = presentedFingerprint;

        Changed?.Invoke();
    }

    /// <summary>
    /// Applies the diagnostic de la incompatibilidad de la versión (AD-6, FR-4).
    ///
    /// <para>
    /// El diagnóstico NO es un estado: el estado de la conexión sigue siendo el que haya
    /// y lo que la superficie muestra es el texto con las dos versiones y el siguiente
    /// paso (AD-6, AD-27, UX-DR-31).
    /// </para>
    /// </summary>
    /// <param name="clientVersion">Version of the client that the server rejected.</param>
    /// <param name="serverVersion">Version of the server, when it published it.</param>
    public void ApplyVersionIncompatibility(string clientVersion, string? serverVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientVersion);

        IncompatibleClientVersion = clientVersion;
        IncompatibleServerVersion = serverVersion;

        // El cliente incompatible no opera: el estado queda DESCONECTADO (AD-6).
        ConnectionState = ConnectionState.Disconnected;
        EndpointInUse = null;

        Changed?.Invoke();
    }

    /// <summary>
    /// Clears the diagnostic de la incompatibilidad, cuando el jugador vuelve a intentar
    /// con un cliente compatible (AD-6).
    /// </summary>
    public void ClearVersionIncompatibility()
    {
        IncompatibleClientVersion = null;
        IncompatibleServerVersion = null;

        Changed?.Invoke();
    }
}
