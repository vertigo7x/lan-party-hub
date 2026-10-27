namespace LANPartyHub.Shared.Contracts;

/// <summary>
/// State pushed by the client to the server (AD-18, AD-22). The shape is exact:
/// a server can never expect a field that this contract does not declare.
/// </summary>
/// <param name="ClientId">Opaque UUID v7 identifier emitted once by the client (AD-23).</param>
/// <param name="ConnectionState">Connection state of the client.</param>
/// <param name="InstalledGames">Installations the client currently owns.</param>
/// <param name="ActiveOperations">Operations the client is currently running.</param>
public sealed record ClientStateChanged(
    string ClientId,
    ConnectionState ConnectionState,
    IReadOnlyList<ClientStateChanged.InstalledGame> InstalledGames,
    IReadOnlyList<ClientStateChanged.ActiveOperation> ActiveOperations)
{
    /// <summary>One installed game reported by the client.</summary>
    /// <param name="GameId">Identifier of the game.</param>
    /// <param name="InstalledVersion">Version currently installed on the client.</param>
    /// <param name="ValidationState">Local validation state of the installation.</param>
    public sealed record InstalledGame(string GameId, string InstalledVersion, InstallationState ValidationState);

    /// <summary>One operation the client is currently running.</summary>
    /// <param name="OperationId">Opaque UUID v7 identifier of the operation.</param>
    /// <param name="OperationType">
    /// Name of the kind of operation, for example the name of a download or the name of an
    /// installation. It is the stable name of the value, not a free text.
    /// </param>
    /// <param name="State">
    /// Name of the current machine state of the operation: the name of a
    /// <see cref="DownloadState"/> value when <paramref name="OperationType"/> names a
    /// download, and the name of an <see cref="InstallationState"/> value when it names an
    /// installation. The text that shows it is obtained only through
    /// <see cref="StateDisplayMap"/> (AD-10, AD-25).
    /// </param>
    public sealed record ActiveOperation(string OperationId, string OperationType, string State);
}
