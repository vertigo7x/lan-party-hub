namespace LANPartyHub.Shared.Contracts;

/// <summary>
/// Closed enumeration on the installation machine states (AD-10, AD-18).
/// The values are the same on the client and on the server.
/// </summary>
public enum InstallationState
{
    /// <summary>The installation is registered and has not started yet.</summary>
    Pending,

    /// <summary>The typed manifest operations are being applied inside the install root.</summary>
    Installing,

    /// <summary>A privileged operation is waiting for an explicit confirmation.</summary>
    AwaitingConfirmation,

    /// <summary>The installed files are being verified.</summary>
    ValidatingInstallation,

    /// <summary>Terminal state: the installation is valid and the game can be launched.</summary>
    Installed,

    /// <summary>Terminal state: the installation is invalid but repairable from the validated package.</summary>
    Repairable,

    /// <summary>Terminal state: the installation failed.</summary>
    Failed,
}
