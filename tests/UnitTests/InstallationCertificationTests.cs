using LANPartyHub.Client.Domain.Install;
using LANPartyHub.Shared.Contracts;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The certification of an installation: its THREE conditions and its repairability
/// (AD-7, AD-9, AD-10, AC5, AC6, AC7).
///
/// <para>
/// La certificación es una REGLA PURA del dominio, así que se ejercita sin disco, sin red
/// y sin servidor (AD-1, AD-16).
/// </para>
/// <para>
/// El nivel mínimo donde «ninguna condición sustituye a otra» es observable es
/// justamente este (AD-16).
/// </para>
/// </summary>
public sealed class InstallationCertificationTests
{
    /// <summary>
    /// The three conditions hold, so the installation is CERTIFIED as valid (AC5).
    /// </summary>
    [Fact]
    public void The_three_conditions_hold_so_the_installation_is_installed()
    {
        var verdict = InstallationCertification.Certify(
            packageValidatedAgainstAnchor: true,
            manifestVerifyPassed: true,
            pathsConfinedToRoot: true,
            repairablePackageAvailable: true);

        Assert.True(verdict.IsInstalled);
        Assert.Equal(InstallationState.Installed, verdict.State);
        Assert.Empty(verdict.FailedConditions);
        Assert.Null(verdict.ReasonCode);
        Assert.Null(verdict.ReasonText);
        Assert.Null(verdict.RepairAction);
    }

    /// <summary>
    /// The three conditions are ALWAYS evaluated, so the verdict says which ones it
    /// checked (AC6).
    /// </summary>
    [Fact]
    public void The_verdict_always_says_which_conditions_it_evaluated()
    {
        var verdict = InstallationCertification.Certify(
            packageValidatedAgainstAnchor: false,
            manifestVerifyPassed: false,
            pathsConfinedToRoot: false,
            repairablePackageAvailable: false);

        Assert.Equal(3, verdict.EvaluatedConditions.Count);
        Assert.Contains(InstallationVerdict.Condition.PackageValidatedAgainstAnchor, verdict.EvaluatedConditions);
        Assert.Contains(InstallationVerdict.Condition.ManifestVerifyPassed, verdict.EvaluatedConditions);
        Assert.Contains(InstallationVerdict.Condition.PathsConfinedToRoot, verdict.EvaluatedConditions);
    }

    /// <summary>
    /// NO ONE condition alone certifies the installation: the package validated against
    /// the anchor, with the verify failing and the paths escaping, is NOT valid (AC5).
    /// </summary>
    [Fact]
    public void The_validated_package_alone_does_not_certify_the_installation()
    {
        var verdict = InstallationCertification.Certify(
            packageValidatedAgainstAnchor: true,
            manifestVerifyPassed: false,
            pathsConfinedToRoot: false,
            repairablePackageAvailable: true);

        Assert.False(verdict.IsInstalled);
        Assert.Equal(InstallationState.Repairable, verdict.State);
        Assert.Contains(InstallationVerdict.Condition.ManifestVerifyPassed, verdict.FailedConditions);
        Assert.Contains(InstallationVerdict.Condition.PathsConfinedToRoot, verdict.FailedConditions);
    }

    /// <summary>
    /// NO ONE condition alone certifies the installation: the verify passing and the
    /// paths inside the root, with the package NOT validated, is NOT valid (AC5).
    /// </summary>
    [Fact]
    public void The_passing_verify_alone_does_not_certify_the_installation()
    {
        var verdict = InstallationCertification.Certify(
            packageValidatedAgainstAnchor: false,
            manifestVerifyPassed: true,
            pathsConfinedToRoot: true,
            repairablePackageAvailable: true);

        Assert.False(verdict.IsInstalled);
        Assert.Equal(InstallationState.Repairable, verdict.State);
        Assert.Equal(
            [InstallationVerdict.Condition.PackageValidatedAgainstAnchor],
            verdict.FailedConditions);
    }

    /// <summary>
    /// NO ONE condition alone certifies the installation: the paths inside the root,
    /// with the package NOT validated and the verify failing, is NOT valid (AC5).
    /// </summary>
    [Fact]
    public void The_confined_paths_alone_do_not_certify_the_installation()
    {
        var verdict = InstallationCertification.Certify(
            packageValidatedAgainstAnchor: false,
            manifestVerifyPassed: false,
            pathsConfinedToRoot: true,
            repairablePackageAvailable: true);

        Assert.False(verdict.IsInstalled);
        Assert.Equal(InstallationState.Repairable, verdict.State);
        Assert.DoesNotContain(InstallationVerdict.Condition.PathsConfinedToRoot, verdict.FailedConditions);
    }

    /// <summary>
    /// The installation is REPAIRABLE when the certification fails and the package that
    /// was already validated is STILL AVAILABLE with its sha256 matching (AC6).
    /// </summary>
    [Fact]
    public void A_failed_certification_with_an_available_package_is_repairable()
    {
        var verdict = InstallationCertification.Certify(
            packageValidatedAgainstAnchor: true,
            manifestVerifyPassed: false,
            pathsConfinedToRoot: true,
            repairablePackageAvailable: true);

        Assert.True(verdict.IsRepairable);
        Assert.Equal(InstallationState.Repairable, verdict.State);

        // El motivo y la acción quedan REGISTRADOS y son VISIBLES (AC6, UX-DR-17, UX-DR-20).
        Assert.NotNull(verdict.ReasonText);
        Assert.False(string.IsNullOrWhiteSpace(verdict.ReasonText));
        Assert.NotNull(verdict.RepairAction);
        Assert.False(string.IsNullOrWhiteSpace(verdict.RepairAction));
        Assert.Equal(InstallationVerdict.Condition.ManifestVerifyPassed, verdict.RepairedCondition);
    }

    /// <summary>
    /// The package is NOT available, so the installation is invalid and NOT repairable,
    /// with its reason VISIBLE (AC6).
    /// </summary>
    [Fact]
    public void A_failed_certification_without_an_available_package_is_not_repairable()
    {
        var verdict = InstallationCertification.Certify(
            packageValidatedAgainstAnchor: true,
            manifestVerifyPassed: false,
            pathsConfinedToRoot: true,
            repairablePackageAvailable: false);

        Assert.False(verdict.IsRepairable);
        Assert.False(verdict.IsInstalled);
        Assert.Equal(InstallationState.Failed, verdict.State);

        // El motivo es visible, y NO hay acción de reparación (AC6, UX-DR-18).
        Assert.NotNull(verdict.ReasonText);
        Assert.False(string.IsNullOrWhiteSpace(verdict.ReasonText));
        Assert.Null(verdict.RepairAction);
        Assert.Null(verdict.RepairedCondition);
    }

    /// <summary>
    /// The sha256 of the available package DIVERGES, so the installation is invalid and
    /// NOT repairable (AC6).
    /// </summary>
    [Fact]
    public void A_failed_certification_with_a_diverging_package_is_not_repairable()
    {
        // El hash que diverge es la razón por la que el paquete ya validado NO está
        // disponible para la reparación (AC6).
        var verdict = InstallationCertification.Certify(
            packageValidatedAgainstAnchor: false,
            manifestVerifyPassed: true,
            pathsConfinedToRoot: true,
            repairablePackageAvailable: false);

        Assert.False(verdict.IsRepairable);
        Assert.Equal(InstallationState.Failed, verdict.State);
        Assert.NotNull(verdict.ReasonText);
    }

    /// <summary>
    /// The package is NOT trustworthy, so the installation is invalid and NOT repairable
    /// (AC6).
    /// </summary>
    [Fact]
    public void A_failed_certification_with_an_untrustworthy_package_is_not_repairable()
    {
        var verdict = InstallationCertification.Certify(
            packageValidatedAgainstAnchor: false,
            manifestVerifyPassed: true,
            pathsConfinedToRoot: true,
            repairablePackageAvailable: false);

        Assert.False(verdict.IsRepairable);
        Assert.Equal(InstallationState.Failed, verdict.State);
        Assert.NotNull(verdict.ReasonText);
    }

    /// <summary>
    /// The certification is DETERMINISTA: the same tree and the same manifest conclude
    /// the same state (AC5).
    /// </summary>
    [Fact]
    public void The_same_tree_concludes_the_same_verdict()
    {
        var first = InstallationCertification.Certify(
            packageValidatedAgainstAnchor: true,
            manifestVerifyPassed: false,
            pathsConfinedToRoot: true,
            repairablePackageAvailable: true);

        var second = InstallationCertification.Certify(
            packageValidatedAgainstAnchor: true,
            manifestVerifyPassed: false,
            pathsConfinedToRoot: true,
            repairablePackageAvailable: true);

        Assert.Equal(first.State, second.State);
        Assert.Equal(first.FailedConditions, second.FailedConditions);
        Assert.Equal(first.ReasonCode, second.ReasonCode);
        Assert.Equal(first.ReasonText, second.ReasonText);
        Assert.Equal(first.RepairAction, second.RepairAction);
    }

    /// <summary>
    /// The identifier of the installation root is DERIVED from its canonical path, so it
    /// is stable against a repair and against the reinstallation of the binary (AC1).
    /// </summary>
    [Fact]
    public void The_identifier_of_the_root_is_stable_against_a_repair()
    {
        var first = InstallationCertification.DeriveInstallDirectoryId(@"C:\Juegos\Instalaciones");
        var second = InstallationCertification.DeriveInstallDirectoryId(@"C:\Juegos\Instalaciones");

        Assert.Equal(first, second);
        Assert.Equal(InstallationCertification.InstallDirectoryIdLength, first.Length);
    }

    /// <summary>
    /// The identifier of the installation root does NOT depend on the CASE of the path:
    /// it describes the SAME root and not a way of writing it (AC1).
    /// </summary>
    [Fact]
    public void The_identifier_of_the_root_does_not_depend_on_the_case_of_the_path()
    {
        var lower = InstallationCertification.DeriveInstallDirectoryId(@"C:\juegos\instalaciones");
        var upper = InstallationCertification.DeriveInstallDirectoryId(@"C:\JUEGOS\INSTALACIONES");

        Assert.Equal(lower, upper);
    }

    /// <summary>
    /// Two DIFFERENT roots have two DIFFERENT identifiers (AC1).
    /// </summary>
    [Fact]
    public void Two_different_roots_have_two_different_identifiers()
    {
        var first = InstallationCertification.DeriveInstallDirectoryId(@"C:\Juegos\Uno");
        var second = InstallationCertification.DeriveInstallDirectoryId(@"C:\Juegos\Dos");

        Assert.NotEqual(first, second);
    }

    /// <summary>
    /// The reason of the certification is a STABLE code of the catalog, so it reaches the
    /// log and the metric per code (AD-15, AC6).
    /// </summary>
    [Fact]
    public void The_reason_of_the_certification_is_a_stable_code_of_the_catalog()
    {
        var verdict = InstallationCertification.Certify(
            packageValidatedAgainstAnchor: true,
            manifestVerifyPassed: false,
            pathsConfinedToRoot: true,
            repairablePackageAvailable: true);

        Assert.NotNull(verdict.ReasonCode);
        Assert.Contains(verdict.ReasonCode, ErrorCatalog.Codes);
    }

    /// <summary>
    /// The state of the certification is a value of the CLOSED enumeration of the
    /// contract, and its text comes from the single table of microcopy (AD-10, AC7).
    /// </summary>
    [Fact]
    public void The_state_of_the_certification_has_its_text_in_the_state_display_map()
    {
        var verdict = InstallationCertification.Certify(
            packageValidatedAgainstAnchor: true,
            manifestVerifyPassed: true,
            pathsConfinedToRoot: true,
            repairablePackageAvailable: true);

        Assert.Equal("Instalado", StateDisplayMap.GetText(verdict.State));
    }
}
