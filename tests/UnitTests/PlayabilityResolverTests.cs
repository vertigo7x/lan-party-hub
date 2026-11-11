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

    [Fact]
    public void An_outdated_installation_makes_the_update_the_primary_action()
    {
        // AC1, AC4: una instalación VÁLIDA cuya versión es anterior a la publicada hace
        // «Actualizar» la primaria.
        var decision = PlayabilityResolver.Resolve(
            Certified(),
            installedVersion: "1.9.0",
            publishedVersion: "1.10.0");

        Assert.True(decision.IsOutdated);
        Assert.Equal(PlayabilityAction.Update, decision.PrimaryAction);
    }

    [Fact]
    public void An_outdated_installation_keeps_the_action_of_playing_available()
    {
        // AC4: «Jugar» permanece disponible SOLO si la instalación sigue siendo válida, así
        // que una instalación válida desactualizada NO bloquea «Jugar».
        var decision = PlayabilityResolver.Resolve(
            Certified(),
            installedVersion: "1.9.0",
            publishedVersion: "1.10.0");

        Assert.False(decision.IsBlocked);
        Assert.True(decision.CanPlay);
        Assert.Null(decision.ReasonText);
        Assert.Null(decision.SecondaryAction);
    }

    [Fact]
    public void An_outdated_installation_carries_the_published_version()
    {
        // AC1, AC3: la insignia necesita MOSTRAR la versión disponible junto a la
        // instalada.
        var decision = PlayabilityResolver.Resolve(
            Certified(),
            installedVersion: "1.9.0",
            publishedVersion: "1.10.0");

        Assert.Equal("1.10.0", decision.PublishedVersion);
    }

    [Fact]
    public void The_outdated_installation_is_derived_with_the_semantic_order_of_the_versions()
    {
        // AC2, AR-5: el orden LEXICOGRÁFICO diría que «1.10.0» es anterior a «1.9.0», así
        // que con la regla del catálogo la instalación «1.9.0» es la que está desactualizada.
        var decision = PlayabilityResolver.Resolve(
            Certified(),
            installedVersion: "1.9.0",
            publishedVersion: "1.10.0");

        Assert.True(decision.IsOutdated);
    }

    [Fact]
    public void An_up_to_date_installation_is_not_outdated()
    {
        var decision = PlayabilityResolver.Resolve(
            Certified(),
            installedVersion: "1.10.0",
            publishedVersion: "1.10.0");

        Assert.False(decision.IsOutdated);
        Assert.Equal(PlayabilityAction.Play, decision.PrimaryAction);
        Assert.False(decision.IsBlocked);
    }

    [Fact]
    public void An_installation_that_is_newer_than_the_published_one_is_not_outdated()
    {
        var decision = PlayabilityResolver.Resolve(
            Certified(),
            installedVersion: "2.0.0",
            publishedVersion: "1.9.0");

        Assert.False(decision.IsOutdated);
        Assert.Equal(PlayabilityAction.Play, decision.PrimaryAction);
    }

    [Fact]
    public void An_invalid_installation_that_is_outdated_is_not_repairable()
    {
        // AC4: la INVALIDEZ MANDA sobre «Actualizar»: una instalación inválida es inválida y
        // NO reparable por el hecho de estar desactualizada.
        var decision = PlayabilityResolver.Resolve(
            NotRepairable(),
            installedVersion: "1.9.0",
            publishedVersion: "1.10.0");

        Assert.False(decision.IsOutdated);
        Assert.NotEqual(PlayabilityAction.Update, decision.PrimaryAction);
        Assert.Equal(PlayabilityAction.Redownload, decision.PrimaryAction);
        Assert.True(decision.IsBlocked);
    }

    [Fact]
    public void A_repairable_installation_that_is_outdated_stays_repairable()
    {
        // AC4: una instalación reparable sigue siendo reparable, y «Actualizar» NO sustituye
        // a «Reparar».
        var decision = PlayabilityResolver.Resolve(
            Repairable(),
            installedVersion: "1.9.0",
            publishedVersion: "1.10.0");

        Assert.False(decision.IsOutdated);
        Assert.Equal(PlayabilityAction.Repair, decision.PrimaryAction);
        Assert.True(decision.IsBlocked);
    }

    [Fact]
    public void An_outdated_installation_shows_its_reason_when_it_is_invalid()
    {
        // AC4: una instalación no válida lleva su MOTIVO VISIBLE, así que el jugador sabe
        // por qué no puede jugar.
        var decision = PlayabilityResolver.Resolve(
            NotRepairable(),
            installedVersion: "1.9.0",
            publishedVersion: "1.10.0");

        Assert.NotNull(decision.ReasonText);
        Assert.Equal(ErrorCatalog.Resolve(decision.ReasonCode!).Message, decision.ReasonText);
    }

    [Fact]
    public void An_installation_without_a_known_published_version_is_not_outdated()
    {
        // El cliente que no conoce la versión publicada NO puede afirmar que la instalación
        // está desactualizada (AC3).
        var decision = PlayabilityResolver.Resolve(
            Certified(),
            installedVersion: "1.9.0",
            publishedVersion: null);

        Assert.False(decision.IsOutdated);
        Assert.Equal(PlayabilityAction.Play, decision.PrimaryAction);
    }

    [Fact]
    public void An_installation_without_a_known_installed_version_is_not_outdated()
    {
        var decision = PlayabilityResolver.Resolve(
            Certified(),
            installedVersion: null,
            publishedVersion: "1.10.0");

        Assert.False(decision.IsOutdated);
        Assert.Equal(PlayabilityAction.Play, decision.PrimaryAction);
    }

    [Fact]
    public void The_resolver_without_the_versions_keeps_the_previous_behaviour()
    {
        // La sobrecarga sin versiones NO cambia la regla de la instalación válida: la
        // decisión es la MISMA que antes de esta historia (AC1, AC2).
        var decision = PlayabilityResolver.Resolve(Certified());

        Assert.False(decision.IsBlocked);
        Assert.Equal(PlayabilityAction.Play, decision.PrimaryAction);
        Assert.False(decision.IsOutdated);
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
