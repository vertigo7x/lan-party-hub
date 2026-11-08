using LANPartyHub.Client.Application.GameLauncher;
using LANPartyHub.Client.Presentation.Library;
using LANPartyHub.Shared.Contracts;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The Biblioteca of the player (FR-9, AC1, AC2, AC3, AC8, UX-DR-17, UX-DR-18,
/// UX-DR-28).
///
/// <para>
/// Los tests fijan el contenido de la Biblioteca: las instalaciones con sus acciones y
/// su motivo VISIBLE, y el estado vacío con su texto EXACTO y su salida accionable
/// (AC1, AC2, AC3, AC8).
/// </para>
/// </summary>
public sealed class LibrarySurfaceViewModelTests
{
    private const string GameId = "0193f2a1b2c34d4e5f60718293a4b5c6";

    [Fact]
    public void A_client_without_installations_shows_the_exact_empty_text()
    {
        // AC8, UX-DR-28: el estado vacío de la Biblioteca.
        var viewModel = new LibrarySurfaceViewModel();

        Assert.True(viewModel.IsEmpty);
        Assert.Equal("No tienes juegos instalados todavía.", viewModel.EmptyMessage);
    }

    [Fact]
    public void A_client_without_installations_offers_the_action_of_seeing_the_catalog()
    {
        // AC8, UX-DR-28: ningún estado vacío queda SIN salida accionable.
        var viewModel = new LibrarySurfaceViewModel();

        Assert.Equal("Ver catálogo", viewModel.EmptyActionText);
    }

    [Fact]
    public void A_client_with_installations_is_not_empty()
    {
        var viewModel = new LibrarySurfaceViewModel();

        viewModel.ApplyInstallations(
        [
            new InstalledGamePlayability(
                GameId,
                "1.0.0",
                InstallationState.Installed,
                PlayabilityDecision.Playable()),
        ]);

        Assert.False(viewModel.IsEmpty);
        Assert.Single(viewModel.Entries);
    }

    [Fact]
    public void The_installed_game_shows_its_state_and_its_version()
    {
        var viewModel = new LibrarySurfaceViewModel();

        viewModel.ApplyInstallations(
        [
            new InstalledGamePlayability(
                GameId,
                "1.0.0",
                InstallationState.Installed,
                PlayabilityDecision.Playable()),
        ]);

        var entry = Assert.Single(viewModel.Entries);

        Assert.Equal(GameId, entry.GameId);
        Assert.Equal("1.0.0", entry.InstalledVersion);
        Assert.Equal(StateDisplayMap.GetText(InstallationState.Installed), entry.ValidationStateText);
        Assert.Equal("Instalado · 1.0.0", entry.InstalledVersionText);
    }

    [Fact]
    public void An_installed_game_shows_the_same_table_of_actions_than_the_detail()
    {
        // AC1, AC2, UX-DR-17: la Biblioteca y la ficha aplican la MISMA tabla de
        // acciones, así que ninguna de las dos abre un camino de lanzamiento distinto.
        var viewModel = new LibrarySurfaceViewModel();

        viewModel.ApplyInstallations(
        [
            new InstalledGamePlayability(
                GameId,
                "1.0.0",
                InstallationState.Repairable,
                PlayabilityDecision.Blocked(
                    ErrorCatalog.InstallationIncompleteCode,
                    PlayabilityAction.Repair)),
        ]);

        var entry = Assert.Single(viewModel.Entries);

        Assert.True(entry.Actions.IsBlocked);
        Assert.Equal("Reparar", entry.Actions.PrimaryActionText);
        Assert.Equal("Diagnosticar", entry.Actions.SecondaryActionText);
        Assert.Equal(
            "No se puede jugar: instalación incompleta",
            entry.Actions.ReasonText);
    }

    [Fact]
    public void The_visible_reason_of_the_block_comes_from_the_catalog()
    {
        // AD-15, AD-25: el motivo visible es el `message` de la entrada del catálogo.
        var viewModel = new LibrarySurfaceViewModel();

        viewModel.ApplyInstallations(
        [
            new InstalledGamePlayability(
                GameId,
                "1.0.0",
                InstallationState.Failed,
                PlayabilityDecision.Blocked(
                    ErrorCatalog.GameExecutableMissingCode,
                    PlayabilityAction.Redownload)),
        ]);

        var entry = Assert.Single(viewModel.Entries);

        Assert.Equal(
            ErrorCatalog.Resolve(ErrorCatalog.GameExecutableMissingCode).Message,
            entry.Actions.ReasonText);
    }

    [Fact]
    public void The_state_of_the_installation_comes_from_the_single_table_of_microcopy()
    {
        // AC4, AR-8: el texto del estado sale EXCLUSIVAMENTE de la tabla de microcopy.
        var viewModel = new LibrarySurfaceViewModel();

        viewModel.ApplyInstallations(
        [
            new InstalledGamePlayability(
                GameId,
                "1.0.0",
                InstallationState.Repairable,
                PlayabilityDecision.Blocked(
                    ErrorCatalog.InstallationIncompleteCode,
                    PlayabilityAction.Repair)),
        ]);

        var entry = Assert.Single(viewModel.Entries);

        Assert.Equal(StateDisplayMap.GetText(InstallationState.Repairable), entry.ValidationStateText);
    }

    [Fact]
    public void The_library_never_shows_the_path_of_the_installation()
    {
        // AD-22: la ruta de la instalación es local y NUNCA sale del cliente.
        var viewModel = new LibrarySurfaceViewModel();

        viewModel.ApplyInstallations(
        [
            new InstalledGamePlayability(
                GameId,
                "1.0.0",
                InstallationState.Installed,
                PlayabilityDecision.Playable()),
        ]);

        var entry = Assert.Single(viewModel.Entries);

        Assert.DoesNotContain(
            "InstallPath",
            typeof(LibraryEntryViewModel).GetProperties().Select(property => property.Name),
            StringComparer.Ordinal);
    }

    [Fact]
    public void The_library_notifies_that_it_has_to_be_rendered_again()
    {
        var viewModel = new LibrarySurfaceViewModel();

        var changes = 0;

        viewModel.Changed += () => changes++;

        viewModel.ApplyInstallations(
        [
            new InstalledGamePlayability(
                GameId,
                "1.0.0",
                InstallationState.Installed,
                PlayabilityDecision.Playable()),
        ]);

        Assert.Equal(1, changes);
    }

    [Fact]
    public void A_client_with_several_installations_lists_them_all()
    {
        var viewModel = new LibrarySurfaceViewModel();

        viewModel.ApplyInstallations(
        [
            new InstalledGamePlayability(
                "juego-a",
                "1.0.0",
                InstallationState.Installed,
                PlayabilityDecision.Playable()),
            new InstalledGamePlayability(
                "juego-b",
                "2.0.0",
                InstallationState.Repairable,
                PlayabilityDecision.Blocked(
                    ErrorCatalog.InstallationIncompleteCode,
                    PlayabilityAction.Repair)),
        ]);

        Assert.Equal(2, viewModel.Entries.Count);
        Assert.False(viewModel.IsEmpty);
    }
}
