using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Client.Domain.Install;

/// <summary>
/// The verdict of the certification of an installation (AD-10, AC5, AC6).
///
/// <para>
/// El veredicto es la ÚNICA definición de «instalación válida» y de «instalación
/// reparable» del sistema: las historias que ramifican sobre él (8.2, 10.3 y la
/// proyección del servidor) leen estos mismos valores, así que no puede existir una
/// segunda decisión sobre la integridad del árbol (AC7).
/// </para>
/// <para>
/// El veredicto es DETERMINISTA: el mismo árbol de archivos y el mismo manifiesto
/// producen el mismo veredicto, y el mismo veredicto produce el mismo estado de la
/// máquina de estados (AC5).
/// </para>
/// </summary>
public sealed record InstallationVerdict
{
    /// <summary>
    /// The three conditions that certify an installation as <c>Installed</c> (AC5).
    ///
    /// <para>
    /// Ninguna condición SUSTITUYE a otra: la sola presencia del ejecutable, la sola
    /// presencia del directorio o un árbol no vacío NO bastan para certificar
    /// <c>Installed</c> (AC5).
    /// </para>
    /// </summary>
    public enum Condition
    {
        /// <summary>
        /// The package was validated against the FIXED trust anchor, and its
        /// <c>sha256</c> still matches the one that the SIGNED manifest declares
        /// (AD-2, AD-8, AC5).
        /// </summary>
        PackageValidatedAgainstAnchor,

        /// <summary>
        /// The <c>verify</c> operation of the manifest passes over the INSTALLED tree
        /// (AD-7, AC5).
        /// </summary>
        ManifestVerifyPassed,

        /// <summary>
        /// Every written path resolves INSIDE the configured installation root
        /// (AD-9, AC5).
        /// </summary>
        PathsConfinedToRoot,
    }

    /// <summary>
    /// The state of the installation machine that the verdict leads to (AD-10).
    /// </summary>
    public required InstallationState State { get; init; }

    /// <summary>
    /// The conditions that the certification checked, in the order that the
    /// certification declares them. The list is NEVER empty: a verdict always says
    /// which conditions it evaluated (AC6).
    /// </summary>
    public required IReadOnlyList<Condition> EvaluatedConditions { get; init; }

    /// <summary>
    /// The conditions that did NOT hold, in the order of
    /// <see cref="EvaluatedConditions"/>. An empty list means that the installation is
    /// certified (AC5).
    /// </summary>
    public required IReadOnlyList<Condition> FailedConditions { get; init; }

    /// <summary>
    /// Whether the installation is certified as valid, that is, whether ALL the three
    /// conditions hold (AC5).
    /// </summary>
    public bool IsInstalled => State == InstallationState.Installed;

    /// <summary>
    /// Whether the installation is invalid but repairable from the validated package
    /// (AC6).
    /// </summary>
    public bool IsRepairable => State == InstallationState.Repairable;

    /// <summary>
    /// The stable code of the reason that the certification failed, or <c>null</c> when
    /// the installation is certified (AC6).
    /// </summary>
    public required string? ReasonCode { get; init; }

    /// <summary>
    /// The visible text of the reason that the certification failed, or <c>null</c> when
    /// the installation is certified (AC6, UX-DR-17, UX-DR-18, UX-DR-20).
    /// </summary>
    public required string? ReasonText { get; init; }

    /// <summary>
    /// The operation that repairs the installation, or <c>null</c> when the installation
    /// is not repairable (AC6, UX-DR-20).
    /// </summary>
    public required string? RepairAction { get; init; }

    /// <summary>
    /// The condition that the repair fixes, or <c>null</c> when the installation is not
    /// repairable (AC6).
    /// </summary>
    public required Condition? RepairedCondition { get; init; }

    /// <summary>
    /// The verification of the conditions of the certification that hold, and the reason
    /// of the ones that do not.
    /// </summary>
    /// <param name="packageValidatedAgainstAnchor">
    /// Whether the package was validated against the fixed anchor and its
    /// <c>sha256</c> still matches.
    /// </param>
    /// <param name="manifestVerifyPassed">
    /// Whether the <c>verify</c> operation of the manifest passes over the installed
    /// tree.
    /// </param>
    /// <param name="pathsConfinedToRoot">
    /// Whether every written path resolves inside the configured installation root.
    /// </param>
    /// <param name="repairablePackageAvailable">
    /// Whether the package that was already validated is STILL AVAILABLE and its
    /// <c>sha256</c> STILL matches, so the installation can be redone from it without
    /// downloading again (AC6).
    /// </param>
    /// <returns>The verdict of the certification.</returns>
    public static InstallationVerdict Certify(
        bool packageValidatedAgainstAnchor,
        bool manifestVerifyPassed,
        bool pathsConfinedToRoot,
        bool repairablePackageAvailable)
    {
        var evaluated = new[]
        {
            Condition.PackageValidatedAgainstAnchor,
            Condition.ManifestVerifyPassed,
            Condition.PathsConfinedToRoot,
        };

        var failed = new List<Condition>(3);

        if (!packageValidatedAgainstAnchor)
        {
            failed.Add(Condition.PackageValidatedAgainstAnchor);
        }

        if (!manifestVerifyPassed)
        {
            failed.Add(Condition.ManifestVerifyPassed);
        }

        if (!pathsConfinedToRoot)
        {
            failed.Add(Condition.PathsConfinedToRoot);
        }

        // AC5: Installed exige las TRES condiciones y ninguna sustituye a otra.
        if (failed.Count == 0)
        {
            return new InstallationVerdict
            {
                State = InstallationState.Installed,
                EvaluatedConditions = evaluated,
                FailedConditions = [],
                ReasonCode = null,
                ReasonText = null,
                RepairAction = null,
                RepairedCondition = null,
            };
        }

        // AC6: Repairable solo si el paquete validado SIGUE disponible con su sha256
        // coincidente, de modo que la instalación puede rehacerse sin volver a descargar.
        if (repairablePackageAvailable)
        {
            var repaired = failed[0];

            return new InstallationVerdict
            {
                State = InstallationState.Repairable,
                EvaluatedConditions = evaluated,
                FailedConditions = failed,
                ReasonCode = RepairReasonCode(repaired),
                ReasonText = RepairReasonText(repaired),
                RepairAction = RepairActionText(repaired),
                RepairedCondition = repaired,
            };
        }

        // AC6: si NINGUNA condición reparable se cumple, la instalación es inválida y NO
        // reparable, con su motivo visible.
        var notRepairable = failed[0];

        return new InstallationVerdict
        {
            State = InstallationState.Failed,
            EvaluatedConditions = evaluated,
            FailedConditions = failed,
            ReasonCode = NotRepairableReasonCode(notRepairable),
            ReasonText = NotRepairableReasonText(notRepairable),
            RepairAction = null,
            RepairedCondition = null,
        };
    }

    /// <summary>
    /// The stable code of the reason of a repairable installation (AC6).
    /// </summary>
    /// <param name="condition">Condition that failed and that the repair fixes.</param>
    /// <returns>The stable code of the reason.</returns>
    private static string RepairReasonCode(Condition condition) => condition switch
    {
        Condition.PackageValidatedAgainstAnchor => ErrorCatalog.PackageIntegrityMismatchCode,
        Condition.ManifestVerifyPassed => ErrorCatalog.PackageIntegrityMismatchCode,
        Condition.PathsConfinedToRoot => ErrorCatalog.ManifestOperationNotAllowedCode,
        _ => ErrorCatalog.UnknownErrorCode,
    };

    /// <summary>
    /// The visible text of the reason of a repairable installation (AC6, UX-DR-17).
    /// </summary>
    /// <param name="condition">Condition that failed and that the repair fixes.</param>
    /// <returns>The visible text of the reason.</returns>
    private static string RepairReasonText(Condition condition) => condition switch
    {
        Condition.PackageValidatedAgainstAnchor =>
            "El paquete ya validado no conserva su hash: la instalación se puede rehacer desde el paquete de nuevo.",
        Condition.ManifestVerifyPassed =>
            "La verificación del manifiesto sobre el árbol instalado no pasa: la instalación se puede rehacer desde el paquete ya validado.",
        Condition.PathsConfinedToRoot =>
            "Alguna ruta escrita sale de la raíz de instalación configurada: la instalación se puede rehacer desde el paquete ya validado.",
        _ => ErrorCatalog.Resolve(ErrorCatalog.UnknownErrorCode).Message,
    };

    /// <summary>
    /// The operation that repairs the installation (AC6, UX-DR-20).
    /// </summary>
    /// <param name="condition">Condition that failed and that the repair fixes.</param>
    /// <returns>The text of the repair operation.</returns>
    private static string RepairActionText(Condition condition) => condition switch
    {
        Condition.PackageValidatedAgainstAnchor => "Reparar la instalación",
        Condition.ManifestVerifyPassed => "Reparar la instalación",
        Condition.PathsConfinedToRoot => "Reparar la instalación",
        _ => "Reparar la instalación",
    };

    /// <summary>
    /// The stable code of the reason of an installation that is NOT repairable (AC6).
    /// </summary>
    /// <param name="condition">Condition that failed.</param>
    /// <returns>The stable code of the reason.</returns>
    private static string NotRepairableReasonCode(Condition condition) => condition switch
    {
        Condition.PackageValidatedAgainstAnchor => ErrorCatalog.PackageIntegrityMismatchCode,
        Condition.ManifestVerifyPassed => ErrorCatalog.PackageIntegrityMismatchCode,
        Condition.PathsConfinedToRoot => ErrorCatalog.ManifestOperationNotAllowedCode,
        _ => ErrorCatalog.UnknownErrorCode,
    };

    /// <summary>
    /// The visible text of the reason of an installation that is NOT repairable (AC6,
    /// UX-DR-18).
    /// </summary>
    /// <param name="condition">Condition that failed.</param>
    /// <returns>The visible text of the reason.</returns>
    private static string NotRepairableReasonText(Condition condition) => condition switch
    {
        Condition.PackageValidatedAgainstAnchor =>
            "El paquete ya validado no está disponible o su hash diverge: la instalación no se puede reparar sin volver a descargar.",
        Condition.ManifestVerifyPassed =>
            "El paquete ya validado no está disponible o su hash diverge: la instalación no se puede reparar sin volver a descargar.",
        Condition.PathsConfinedToRoot =>
            "El paquete ya validado no está disponible o su hash diverge: la instalación no se puede reparar sin volver a descargar.",
        _ => ErrorCatalog.Resolve(ErrorCatalog.UnknownErrorCode).Message,
    };
}
