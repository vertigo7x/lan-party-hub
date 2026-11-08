using LANPartyHub.Client.Domain.Install;
using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Client.Application.Install;

/// <summary>
/// The OUTCOME of a REPAIR of an invalid installation (AC1, AC2, UX-DR-20).
///
/// <para>
/// El resultado distingue el RECHAZO —que no deja ninguna escritura— del VEREDICTO de una
/// reparación que sí llegó a aplicarse, y el RECHAZO lleva la CONSECUENCIA que la
/// interfaz nombra: la reparación NO repite la descarga (AC1, UX-DR-20).
/// </para>
/// </summary>
public sealed record RepairOutcome
{
    /// <summary>
    /// The repair was REJECTED and nothing was written, because the validated package is
    /// NOT available or its <c>sha256</c> diverges (AC1, AC2).
    /// </summary>
    /// <param name="error">Error of the catalog that explains the rejection.</param>
    /// <param name="reason">Human readable reason of the rejection.</param>
    /// <returns>The outcome of the rejection.</returns>
    public static RepairOutcome Rejected(ApiError error, string reason) =>
        new() { IsRejected = true, Error = error, Reason = reason };

    /// <summary>
    /// The repair was APPLIED and the tree was CERTIFIED (AC1, AC3).
    /// </summary>
    /// <param name="verdict">Verdict of the certification.</param>
    /// <param name="installDirectoryId">Opaque identifier of the installation root.</param>
    /// <param name="installPath">Local path of the installation root.</param>
    /// <returns>The outcome of the certification.</returns>
    public static RepairOutcome Repaired(
        InstallationVerdict verdict,
        string installDirectoryId,
        string installPath) =>
        new()
        {
            IsRejected = false,
            Verdict = verdict,
            InstallDirectoryId = installDirectoryId,
            InstallPath = installPath,
        };

    /// <summary>Whether the repair was rejected before writing anything (AC1).</summary>
    public bool IsRejected { get; init; }

    /// <summary>Whether the repair reached a certified state (AC1).</summary>
    public bool IsRepaired => !IsRejected;

    /// <summary>
    /// Error of the catalog that explains the rejection, or <c>null</c> when the repair
    /// was applied (AD-15, AD-25).
    /// </summary>
    public ApiError? Error { get; init; }

    /// <summary>
    /// Human readable reason of the rejection, or <c>null</c> when the repair was applied
    /// (AC1).
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// Verdict of the certification, or <c>null</c> when the repair was rejected (AC1).
    /// </summary>
    public InstallationVerdict? Verdict { get; init; }

    /// <summary>
    /// Opaque identifier of the installation root, or <c>null</c> when the repair was
    /// rejected (AC1, AD-22).
    /// </summary>
    public string? InstallDirectoryId { get; init; }

    /// <summary>
    /// Local path of the installation root, or <c>null</c> when the repair was rejected.
    /// It never leaves the client (AD-22).
    /// </summary>
    public string? InstallPath { get; init; }
}
