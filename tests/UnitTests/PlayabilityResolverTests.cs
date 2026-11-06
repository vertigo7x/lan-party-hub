using LANPartyHub.Client.Application.GameLauncher;
using LANPartyHub.Client.Domain.Install;
using LANPartyHub.Shared.Contracts;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The derivation of the playability of an installation (FR-9, AC1, AC2, AC3, UX-DR-17,
/// UX-DR-18, AD-10).
///
/// <para>
/// Los tests fijan la TABLA de estados y de acción primaria: una instalación reparable
/// hace «Reparar» la primaria, una que no es reparable hace «Descargar de nuevo» la
/// única primaria, y los TRES motivos visibles llevan su texto EXACTO (AC1, AC2, AC3).
/// </para>
/// </summary>
public sealed class PlayabilityResolverTests
{
    private const string GameId = "0193f2a1b2c34d4e5f60718293a4b5c6";

    [Fact]
    public void A_certified_installation_enables_the_action_of_playing()
    {
        var decision = PlayabilityResolver.Resolve(Certified());

        Assert.False(decision.IsBlocked);
        Assert.True(decision.CanPlay);
        Assert.Null(decision.ReasonText);
        Assert.Null(decision.ReasonCode);
        Assert.Equal(PlayabilityAction.Play, decision.PrimaryAction);
        Assert.Null(decision.SecondaryAction);
    }

    [Fact]
    public void A_repairable_installation_makes_the_repair_the_primary_action()
    {
        // AC1: una instalación inválida y REPARABLE bloquea «Jugar» y hace «Reparar» la
        // primaria, con «Diagnosticar» como secundaria.
        var decision = PlayabilityResolver.Resolve(Repairable());

        Assert.True(decision.IsBlocked);
        Assert.False(decision.CanPlay);
        Assert.Equal(PlayabilityAction.Repair, decision.PrimaryAction);
        Assert.Equal(PlayabilityAction.Diagnose, decision.SecondaryAction);
    }

    [Fact]
    public void An_installation_that_is_not_repairable_makes_the_redownload_the_only_primary_action()
    {
        // AC2: una instalación inválida y NO reparable hace «Descargar de nuevo» la ÚNICA
        // primaria, con «Diagnosticar» como secundaria.
        var decision = PlayabilityResolver.Resolve(NotRepairable());

        Assert.True(decision.IsBlocked);
        Assert.False(decision.CanPlay);
        Assert.Equal(PlayabilityAction.Redownload, decision.PrimaryAction);
        Assert.Equal(PlayabilityAction.Diagnose, decision.SecondaryAction);
    }

    [Fact]
    public void A_blocked_installation_carries_the_reason_of_the_incomplete_installation()
    {
        // AC1: el motivo visible de la instalación incompleta, que es la que NO se puede
        // reparar porque una ruta escrita sale del confine de la raíz.
        var decision = PlayabilityResolver.Resolve(Verdict(
            packageValidatedAgainstAnchor: true,
            manifestVerifyPassed: true,
            pathsConfinedToRoot: false,
            repairablePackageAvailable: false));

        Assert.Equal(ErrorCatalog.InstallationIncompleteCode, decision.ReasonCode);
        Assert.Equal("No se puede jugar: instalación incompleta", decision.ReasonText);
    }

    [Fact]
    public void A_blocked_installation_carries_the_reason_of_the_missing_executable()
    {
        // AC1: el motivo visible del ejecutable ausente.
        var decision = PlayabilityResolver.Resolve(Verdict(
            packageValidatedAgainstAnchor: true,
            manifestVerifyPassed: false,
            pathsConfinedToRoot: true,
            repairablePackageAvailable: false));

        Assert.Equal(ErrorCatalog.GameExecutableMissingCode, decision.ReasonCode);
        Assert.Equal("No se puede jugar: falta el ejecutable", decision.ReasonText);
    }

    [Fact]
    public void A_blocked_installation_carries_the_reason_of_the_package_that_does_not_match()
    {
        // AC1: el motivo visible del paquete que no coincide con la validación.
        var decision = PlayabilityResolver.Resolve(Verdict(
            packageValidatedAgainstAnchor: false,
            manifestVerifyPassed: true,
            pathsConfinedToRoot: true,
            repairablePackageAvailable: false));

        Assert.Equal(ErrorCatalog.InstallationPackageMismatchCode, decision.ReasonCode);
        Assert.Equal("No se puede jugar: el paquete no coincide con la validación", decision.ReasonText);
    }

    [Fact]
    public void The_visible_reason_comes_from_the_catalog_and_never_from_the_code()
    {
        // AD-15, AD-25: el motivo visible es el `message` de la entrada del catálogo, y
        // el código estable nunca es el texto principal.
        var decision = PlayabilityResolver.Resolve(Repairable());

        var entry = ErrorCatalog.Resolve(decision.ReasonCode!);

        Assert.Equal(entry.Message, decision.ReasonText);
        Assert.NotEqual(decision.ReasonCode, decision.ReasonText);
    }

    [Fact]
    public void The_block_carries_the_three_parts_of_the_error_of_the_catalog()
    {
        // AD-15, AD-25: «Diagnosticar» abre el error registrado, y el error lleva sus
        // TRES partes.
        var decision = PlayabilityResolver.Resolve(Repairable());

        var error = decision.ReasonError;

        Assert.NotNull(error);
        Assert.False(string.IsNullOrWhiteSpace(error!.Message));
        Assert.False(string.IsNullOrWhiteSpace(error.Cause));
        Assert.False(string.IsNullOrWhiteSpace(error.NextStep));
    }

    [Fact]
    public void The_three_reasons_are_three_different_entries_of_the_catalog()
    {
        // AD-15, AD-25: los tres motivos son tres diagnósticos distintos, así que la
        // métrica de errores por código no los mezcla.
        Assert.NotEqual(
            ErrorCatalog.InstallationIncompleteCode,
            ErrorCatalog.GameExecutableMissingCode);

        Assert.NotEqual(
            ErrorCatalog.InstallationIncompleteCode,
            ErrorCatalog.InstallationPackageMismatchCode);

        Assert.NotEqual(
            ErrorCatalog.GameExecutableMissingCode,
            ErrorCatalog.InstallationPackageMismatchCode);

        foreach (var code in new[]
        {
            ErrorCatalog.InstallationIncompleteCode,
            ErrorCatalog.GameExecutableMissingCode,
            ErrorCatalog.InstallationPackageMismatchCode,
        })
        {
            Assert.Contains(code, ErrorCatalog.Codes);
        }
    }

    [Fact]
    public void An_installation_without_a_verdict_is_blocked_with_the_incomplete_reason()
    {
        // AC1: sin instalación no hay nada que lanzar, así que «Jugar» queda bloqueada con
        // su motivo visible y la primaria es «Descargar de nuevo».
        var decision = PlayabilityResolver.Resolve(verdict: null);

        Assert.True(decision.IsBlocked);
        Assert.Equal(ErrorCatalog.InstallationIncompleteCode, decision.ReasonCode);
        Assert.Equal("No se puede jugar: instalación incompleta", decision.ReasonText);
        Assert.Equal(PlayabilityAction.Redownload, decision.PrimaryAction);
    }

    [Fact]
    public void The_resolver_is_pure_and_does_not_depend_on_the_verdict_instance()
    {
        // AD-1: la regla es pura, así que dos llamadas con el mismo veredicto producen la
        // misma decisión.
        var verdict = Repairable();

        var first = PlayabilityResolver.Resolve(verdict);
        var second = PlayabilityResolver.Resolve(verdict);

        Assert.Equal(first, second);
    }

    [Fact]
    public void The_reason_code_of_the_block_is_the_new_entry_of_the_catalog()
    {
        // El motivo visible es una entrada NUEVA del catálogo, y no un código que ya
        // existía para otra cosa (AD-15, AD-25).
        var decision = PlayabilityResolver.Resolve(Repairable());

        Assert.NotEqual(ErrorCatalog.PackageIntegrityMismatchCode, decision.ReasonCode);
        Assert.NotEqual(ErrorCatalog.GameLaunchInstallationInvalidCode, decision.ReasonCode);
        Assert.NotEqual(ErrorCatalog.GameLaunchExecutableMissingCode, decision.ReasonCode);
    }

    /// <summary>
    /// Builds a verdict of a certified installation (AC5).
    /// </summary>
    /// <returns>The verdict of the certification.</returns>
    private static InstallationVerdict Certified() => Verdict(
        packageValidatedAgainstAnchor: true,
        manifestVerifyPassed: true,
        pathsConfinedToRoot: true,
        repairablePackageAvailable: false);

    /// <summary>
    /// Builds a verdict of an installation that is invalid and REPAIRABLE (AC6).
    /// </summary>
    /// <returns>The verdict of the certification.</returns>
    private static InstallationVerdict Repairable() => Verdict(
        packageValidatedAgainstAnchor: true,
        manifestVerifyPassed: false,
        pathsConfinedToRoot: true,
        repairablePackageAvailable: true);

    /// <summary>
    /// Builds a verdict of an installation that is invalid and NOT repairable (AC6).
    /// </summary>
    /// <returns>The verdict of the certification.</returns>
    private static InstallationVerdict NotRepairable() => Verdict(
        packageValidatedAgainstAnchor: true,
        manifestVerifyPassed: false,
        pathsConfinedToRoot: true,
        repairablePackageAvailable: false);

    /// <summary>
    /// Builds the verdict of the certification of an installation (AC5, AC6).
    /// </summary>
    /// <param name="packageValidatedAgainstAnchor">Whether the package kept its validation.</param>
    /// <param name="manifestVerifyPassed">Whether the manifest verifies over the tree.</param>
    /// <param name="pathsConfinedToRoot">Whether every path stays inside the root.</param>
    /// <param name="repairablePackageAvailable">Whether the validated package is still available.</param>
    /// <returns>The verdict of the certification.</returns>
    private static InstallationVerdict Verdict(
        bool packageValidatedAgainstAnchor,
        bool manifestVerifyPassed,
        bool pathsConfinedToRoot,
        bool repairablePackageAvailable) =>
        InstallationCertification.Certify(
            packageValidatedAgainstAnchor,
            manifestVerifyPassed,
            pathsConfinedToRoot,
            repairablePackageAvailable);
}
