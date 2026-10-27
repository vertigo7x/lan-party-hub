namespace LANPartyHub.Shared.Contracts;

/// <summary>
/// Closed enumeration with exactly three values: the result of a finished operation
/// (AD-33). It is not a machine state: <see cref="PackageWithdrawn"/> is a successful
/// outcome, not an error, and it never produces an <see cref="ApiError"/>.
/// </summary>
public enum OperationOutcome
{
    /// <summary>The operation finished successfully.</summary>
    Succeeded,

    /// <summary>
    /// The operation finished successfully but its package was withdrawn from the catalog
    /// while it was running (AD-26). It is not a failure and it is not registered as an error.
    /// </summary>
    PackageWithdrawn,

    /// <summary>The operation finished with a failure. Only this outcome admits a registered error.</summary>
    Failed,
}
