namespace LANPartyHub.Shared.Contracts;

/// <summary>
/// Organization view of a registered client (AD-18, AD-29). It is the shape returned by
/// <c>GET /api/v1/clients</c>: the client list never travels through a SignalR event.
///
/// <para>
/// The view carries the six fields that the registration already had and ADDS the
/// projection of what the client really has: the games it has installed, the operations
/// it is running right now, the terminal results it reported and the errors that are
/// already registered (AD-22, AD-29, FR-10).
/// </para>
/// <para>
/// The state of the machine of another client NEVER travels as an enumeration: the
/// server stores and publishes the PRESENTATION TEXT copied from
/// <see cref="StateDisplayMap"/>, so the view carries texts and not enums of the
/// client (AD-10, AD-22).
/// </para>
/// <para>
/// The path of the installation of the client NEVER travels: the view shows the
/// version that is installed and the existence of the installation, not its route
/// (AD-22).
/// </para>
/// </summary>
public sealed record ClientRegistrationView
{
    /// <summary>Creates the view of a registered client.</summary>
    /// <param name="clientId">Opaque UUID v7 identifier, technical key of the row (AD-23).</param>
    /// <param name="machineName">Effective human label of the team, unique and validated by the server (AD-30).</param>
    /// <param name="clientVersion">Version of the client binary.</param>
    /// <param name="protocolVersion">Protocol version the client speaks (AD-6).</param>
    /// <param name="connectionState">Current connection state of the client.</param>
    /// <param name="lastSeenAt">Instant of the last contact with the server, normalized to UTC.</param>
    /// <param name="installedGames">
    /// Installations that the client reported, ordered by game identifier. It is EMPTY when
    /// the client has not reported any (AD-22).
    /// </param>
    /// <param name="activeOperations">
    /// Operations that the client is running right now, ordered by operation identifier. It
    /// is EMPTY when the client has not reported any (AD-22).
    /// </param>
    /// <param name="recentOutcomes">
    /// Terminal results that the client reported, ordered by operation identifier. The
    /// result of a withdrawn package is a RESULT and never an error (AD-33).
    /// </param>
    /// <param name="errors">
    /// Errors that are already registered for the client, ordered by instant. They are the
    /// errors that the server already holds: this story SHOWS them and does not produce
    /// new ones (AD-22, AD-15).
    /// </param>
    public ClientRegistrationView(
        string clientId,
        string machineName,
        string clientVersion,
        int protocolVersion,
        ConnectionState connectionState,
        DateTimeOffset lastSeenAt,
        IReadOnlyList<ClientRegistrationView.InstalledGame>? installedGames = null,
        IReadOnlyList<ClientRegistrationView.ActiveOperation>? activeOperations = null,
        IReadOnlyList<ClientRegistrationView.OperationOutcomeResult>? recentOutcomes = null,
        IReadOnlyList<ClientRegistrationView.RegisteredError>? errors = null)
    {
        ClientId = clientId;
        MachineName = machineName;
        ClientVersion = clientVersion;
        ProtocolVersion = protocolVersion;
        ConnectionState = connectionState;
        LastSeenAt = lastSeenAt.ToUniversalTime();
        InstalledGames = installedGames ?? [];
        ActiveOperations = activeOperations ?? [];
        RecentOutcomes = recentOutcomes ?? [];
        Errors = errors ?? [];
    }

    /// <summary>Opaque UUID v7 identifier, technical key of the row (AD-23).</summary>
    public string ClientId { get; init; }

    /// <summary>Effective human label of the team, unique and validated by the server (AD-30).</summary>
    public string MachineName { get; init; }

    /// <summary>Version of the client binary.</summary>
    public string ClientVersion { get; init; }

    /// <summary>Protocol version the client speaks (AD-6).</summary>
    public int ProtocolVersion { get; init; }

    /// <summary>Current connection state of the client.</summary>
    public ConnectionState ConnectionState { get; init; }

    /// <summary>UTC instant of the last contact with the server.</summary>
    public DateTimeOffset LastSeenAt { get; init; }

    /// <summary>
    /// Installations that the client reported, with the PRESENTATION TEXT of the state
    /// copied from <see cref="StateDisplayMap"/> and never the enumeration itself
    /// (AD-10, AD-22).
    /// </summary>
    public IReadOnlyList<InstalledGame> InstalledGames { get; init; }

    /// <summary>
    /// Operations that the client is running right now. The state is the PRESENTATION
    /// TEXT copied from <see cref="StateDisplayMap"/> (AD-10, AD-22).
    /// </summary>
    public IReadOnlyList<ActiveOperation> ActiveOperations { get; init; }

    /// <summary>
    /// Terminal results that the client reported. The text comes from
    /// <see cref="StateDisplayMap"/> and the result of a withdrawn package is NOT an error
    /// (AD-25, AD-33).
    /// </summary>
    public IReadOnlyList<OperationOutcomeResult> RecentOutcomes { get; init; }

    /// <summary>
    /// Errors that are already registered for the client, with the three parts of the
    /// single error shape of the system (AD-15, AD-22).
    /// </summary>
    public IReadOnlyList<RegisteredError> Errors { get; init; }

    /// <summary>
    /// One installation that the client reported (AD-22).
    /// </summary>
    /// <param name="GameId">Identifier of the game.</param>
    /// <param name="InstalledVersion">Version that is installed on the client.</param>
    /// <param name="ValidationStateText">
    /// Text of the state of the installation, copied from <see cref="StateDisplayMap"/>.
    /// It is TEXT and never the enumeration of the client (AD-10, AD-22).
    /// </param>
    public sealed record InstalledGame(string GameId, string InstalledVersion, string ValidationStateText);

    /// <summary>
    /// One operation that the client is running right now (AD-22).
    /// </summary>
    /// <param name="OperationId">Opaque UUID v7 identifier of the operation.</param>
    /// <param name="OperationType">
    /// Name of the kind of operation, for example the name of a download or the name of an
    /// installation. It is the stable name of the value, not a free text.
    /// </param>
    /// <param name="StateText">
    /// Text of the state of the operation, copied from <see cref="StateDisplayMap"/>
    /// (AD-10, AD-25).
    /// </param>
    /// <param name="BytesTransferred">
    /// Bytes transferred so far, when the client reported them. It is <c>null</c> when
    /// the operation does not transfer bytes, and NEVER an invented value (AD-22).
    /// </param>
    /// <param name="TotalBytes">
    /// Total bytes of the operation, when the client reported them. It is <c>null</c>
    /// when the client does not know them (AD-22).
    /// </param>
    /// <param name="Phase">
    /// Real phase of the operation when it is an installation, or <c>null</c> when the
    /// operation does not have one. It NEVER carries an invented percentage (AD-22).
    /// </param>
    public sealed record ActiveOperation(
        string OperationId,
        string OperationType,
        string StateText,
        long? BytesTransferred = null,
        long? TotalBytes = null,
        string? Phase = null);

    /// <summary>
    /// One terminal result that the client reported (AD-33).
    /// </summary>
    /// <param name="OperationId">Opaque identifier of the operation that finished.</param>
    /// <param name="OutcomeText">
    /// Text of the result, copied from <see cref="StateDisplayMap"/>. The result of a
    /// withdrawn package is a RESULT and never an error (AD-25, AD-33).
    /// </param>
    public sealed record OperationOutcomeResult(string OperationId, string OutcomeText);

    /// <summary>
    /// One error that is already registered for the client (AD-15, AD-22).
    /// </summary>
    /// <param name="ErrorId">Opaque identifier of the registered error.</param>
    /// <param name="OperationId">
    /// Opaque identifier of the operation the error belongs to, empty when the error is
    /// not bound to one.
    /// </param>
    /// <param name="Code">Stable code of the error of the catalog.</param>
    /// <param name="Message">What failed.</param>
    /// <param name="Cause">Why it failed.</param>
    /// <param name="NextStep">What to do now.</param>
    /// <param name="OccurredAt">Instant of the error, in UTC.</param>
    public sealed record RegisteredError(
        string ErrorId,
        string OperationId,
        string Code,
        string Message,
        string Cause,
        string NextStep,
        DateTimeOffset OccurredAt);
}
