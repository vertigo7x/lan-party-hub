using LANPartyHub.Client.Application.GameLauncher;
using LANPartyHub.Client.Domain.Install;
using LANPartyHub.Client.Presentation.GameLauncher;
using LANPartyHub.Shared.Contracts;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// An installation that was INTERRUPTED is repairable, and repairing it does not force
/// the player to download the whole package again (AC1, AC3, FR-8, FR-11, UX-DR-45,
/// UX-DR-17).
///
/// <para>
/// Una instalación a medias se marca REPARABLE en vez de obligar a repetir el paquete
/// entero, y el jugador ve el motivo con «Reparar» como acción primaria.
/// </para>
/// </summary>
public sealed class InterruptedInstallationRepairTests
{
    /// <summary>
    /// An INCOMPLETE installation with the package available is repairable (AC1, FR-8).
    /// </summary>
    [Fact]
    public void An_incomplete_installation_with_the_package_available_is_repairable()
    {
        // La instalación está A MEDIAS: el manifiesto no verifica, pero el paquete
        // validado SIGUE disponible, así que se puede rehacer sin volver a descargar.
        var verdict = InstallationVerdict.Certify(
            packageValidatedAgainstAnchor: true,
            manifestVerifyPassed: false,
            pathsConfinedToRoot: true,
            repairablePackageAvailable: true);

        // La instalación se marca REPARABLE en lugar de obligar a repetir el paquete
        // entero (AC1, FR-8, FR-11).
        Assert.True(verdict.IsRepairable);
        Assert.Equal(InstallationState.Repairable, verdict.State);
    }

    /// <summary>
    /// An incomplete installation WITHOUT the package is not repairable, and has to be
    /// installed again (AC1, UX-DR-45).
    /// </summary>
    [Fact]
    public void An_incomplete_installation_without_the_package_is_not_repairable()
    {
        var verdict = InstallationVerdict.Certify(
            packageValidatedAgainstAnchor: true,
            manifestVerifyPassed: false,
            pathsConfinedToRoot: true,
            repairablePackageAvailable: false);

        // Si el paquete validado ya no está disponible, NO hay nada que reparar (AC1).
        Assert.False(verdict.IsRepairable);
    }

    /// <summary>
    /// The repairable installation stays BLOCKED with the visible reason, and «Reparar»
    /// is the PRIMARY action (AC1, UX-DR-45, UX-DR-17).
    /// </summary>
    [Fact]
    public void The_repairable_installation_stays_blocked_with_the_reason()
    {
        var verdict = InstallationVerdict.Certify(
            packageValidatedAgainstAnchor: true,
            manifestVerifyPassed: false,
            pathsConfinedToRoot: true,
            repairablePackageAvailable: true);

        var decision = PlayabilityResolver.Resolve(verdict);

        // En la Biblioteca y en la ficha «Jugar» permanece bloqueada con el motivo
        // VISIBLE (AC1, UX-DR-45).
        Assert.True(decision.IsBlocked);
        Assert.False(string.IsNullOrWhiteSpace(decision.ReasonText));

        // Y «Reparar» es la acción primaria (AC1, UX-DR-17).
        Assert.Equal(PlayabilityAction.Repair, decision.PrimaryAction);
    }

    /// <summary>
    /// The card of the game says «Reparar» (AC1, UX-DR-17).
    /// </summary>
    [Fact]
    public void The_card_of_the_game_says_repair()
    {
        var verdict = InstallationVerdict.Certify(
            packageValidatedAgainstAnchor: true,
            manifestVerifyPassed: false,
            pathsConfinedToRoot: true,
            repairablePackageAvailable: true);

        var viewModel = new GamePlayActionsViewModel("juego-uno", PlayabilityResolver.Resolve(verdict));

        // La acción primaria de la tarjeta es «Reparar» (AC1, UX-DR-17).
        Assert.True(viewModel.IsPrimaryRepair);
        Assert.Equal(GamePlayActionsViewModel.RepairActionText, viewModel.PrimaryActionText);
        Assert.True(viewModel.IsBlocked);
    }
}
