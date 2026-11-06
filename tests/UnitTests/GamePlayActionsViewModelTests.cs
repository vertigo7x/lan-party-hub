using LANPartyHub.Client.Application.GameLauncher;
using LANPartyHub.Client.Domain.Install;
using LANPartyHub.Client.Presentation.GameLauncher;
using LANPartyHub.Shared.Contracts;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The table of the actions of a videogame (FR-9, AC1, AC2, AC3, AC5, AC6, UX-DR-17,
/// UX-DR-18, UX-DR-26, UX-DR-38).
///
/// <para>
/// Los tests fijan el comportamiento del comando y del nombre accesible: «Jugar»
/// bloqueada NO cambia el estado ni dispara descarga, instalación o reparación, y su
/// nombre accesible lleva el estado y el motivo (AC5, AC6, UX-DR-26, UX-DR-38).
/// </para>
/// </summary>
public sealed class GamePlayActionsViewModelTests
{
    private const string GameId = "0193f2a1b2c34d4e5f60718293a4b5c6";

    [Fact]
    public void A_repairable_installation_shows_the_repair_as_the_primary_action()
    {
        var viewModel = new GamePlayActionsViewModel(GameId, Repairable());

        Assert.True(viewModel.IsBlocked);
        Assert.Equal("Reparar", viewModel.PrimaryActionText);
        Assert.True(viewModel.IsPrimaryRepair);
        Assert.False(viewModel.IsPrimaryRedownload);
    }

    [Fact]
    public void An_installation_that_is_not_repairable_shows_the_redownload_as_the_only_primary_action()
    {
        var viewModel = new GamePlayActionsViewModel(GameId, NotRepairable());

        Assert.True(viewModel.IsBlocked);
        Assert.Equal("Descargar de nuevo", viewModel.PrimaryActionText);
        Assert.True(viewModel.IsPrimaryRedownload);
        Assert.False(viewModel.IsPrimaryRepair);
    }

    [Fact]
    public void A_blocked_action_shows_the_diagnose_as_the_secondary_action()
    {
        var viewModel = new GamePlayActionsViewModel(GameId, Repairable());

        Assert.Equal("Diagnosticar", viewModel.SecondaryActionText);
        Assert.Equal(PlayabilityAction.Diagnose, viewModel.SecondaryAction);
    }

    [Fact]
    public void A_playable_action_has_no_secondary_action()
    {
        var viewModel = new GamePlayActionsViewModel(GameId, Playable());

        Assert.Null(viewModel.SecondaryActionText);
        Assert.Null(viewModel.SecondaryAction);
        Assert.Equal("Jugar", viewModel.PrimaryActionText);
    }

    [Theory]
    [InlineData(ErrorCatalog.InstallationIncompleteCode, "No se puede jugar: instalación incompleta")]
    [InlineData(ErrorCatalog.GameExecutableMissingCode, "No se puede jugar: falta el ejecutable")]
    [InlineData(ErrorCatalog.InstallationPackageMismatchCode, "No se puede jugar: el paquete no coincide con la validación")]
    public void The_visible_reason_has_the_exact_text_of_its_code(string code, string expected)
    {
        // AC3, UX-DR-18: el motivo visible lleva su texto EXACTO, y sale del catálogo.
        var viewModel = new GamePlayActionsViewModel(
            GameId,
            PlayabilityDecision.Blocked(code, PlayabilityAction.Repair));

        Assert.True(viewModel.ShowsReason);
        Assert.Equal(expected, viewModel.ReasonText);
        Assert.Equal(expected, ErrorCatalog.Resolve(code).Message);
    }

    [Fact]
    public void A_playable_action_shows_no_reason()
    {
        var viewModel = new GamePlayActionsViewModel(GameId, Playable());

        Assert.False(viewModel.ShowsReason);
        Assert.Null(viewModel.ReasonText);
    }

    [Fact]
    public void The_accessible_name_of_the_blocked_action_carries_the_state_and_the_reason()
    {
        // AC5, UX-DR-38: el lector de pantalla anuncia el nombre de la acción, su estado
        // bloqueado y el motivo visible.
        var viewModel = new GamePlayActionsViewModel(GameId, Repairable());

        Assert.Equal(
            "Jugar (bloqueado): No se puede jugar: instalación incompleta",
            viewModel.PlayAccessibleName);
    }

    [Fact]
    public void The_accessible_name_of_the_playable_action_is_the_name_of_the_action()
    {
        var viewModel = new GamePlayActionsViewModel(GameId, Playable());

        Assert.Equal("Jugar", viewModel.PlayAccessibleName);
    }

    [Fact]
    public async Task Enter_over_the_blocked_action_changes_no_state_and_starts_no_process()
    {
        // AC6: `Enter` sobre «Jugar» bloqueada NO produce ningún cambio de estado, no
        // inicia el juego y no dispara descarga, instalación ni reparación implícita.
        var launches = 0;

        var command = new GamePlayCommand((_, _) =>
        {
            launches++;

            return Task.FromResult(true);
        });

        var launched = await command.ExecuteAsync(
            Repairable(),
            GameId,
            TestContext.Current.CancellationToken);

        Assert.False(launched);
        Assert.False(command.LastLaunchSucceeded);
        Assert.Equal(0, launches);
    }

    [Fact]
    public async Task Enter_over_the_playable_action_launches_the_game()
    {
        // AC1: con la instalación certificada, el comando lanza el juego a través del
        // módulo `GameLauncher`, que es su ÚNICA vía de arranque (AC4, UX-DR-19).
        var launches = 0;

        var command = new GamePlayCommand((gameId, _) =>
        {
            launches++;

            Assert.Equal(GameId, gameId);

            return Task.FromResult(true);
        });

        var launched = await command.ExecuteAsync(
            Playable(),
            GameId,
            TestContext.Current.CancellationToken);

        Assert.True(launched);
        Assert.True(command.LastLaunchSucceeded);
        Assert.Equal(1, launches);
    }

    [Fact]
    public void A_new_decision_replaces_the_one_that_the_table_shows()
    {
        // La decisión se REderiva al pintar, así que una nueva certificación cambia la
        // tabla sin tocar el disco (AD-11, AD-22).
        var viewModel = new GamePlayActionsViewModel(GameId, Playable());

        var changes = 0;

        viewModel.Changed += () => changes++;

        viewModel.Apply(Repairable());

        Assert.Equal(1, changes);
        Assert.True(viewModel.IsBlocked);
        Assert.Equal("Reparar", viewModel.PrimaryActionText);
    }

    [Fact]
    public void The_reason_of_the_block_comes_from_the_catalog_and_never_from_the_code()
    {
        // AD-15, AD-25: el código estable NUNCA es el texto principal.
        var viewModel = new GamePlayActionsViewModel(GameId, Repairable());

        Assert.NotEqual(viewModel.ReasonCode, viewModel.ReasonText);
    }

    /// <summary>
    /// Builds the decision of an installation that is invalid and REPARABLE (AC1).
    /// </summary>
    /// <returns>The decision of the playability.</returns>
    private static PlayabilityDecision Repairable() => PlayabilityDecision.Blocked(
        ErrorCatalog.InstallationIncompleteCode,
        PlayabilityAction.Repair);

    /// <summary>
    /// Builds the decision of an installation that is invalid and NOT repairable (AC2).
    /// </summary>
    /// <returns>The decision of the playability.</returns>
    private static PlayabilityDecision NotRepairable() => PlayabilityDecision.Blocked(
        ErrorCatalog.GameExecutableMissingCode,
        PlayabilityAction.Redownload);

    /// <summary>
    /// Builds the decision of an installation that is CERTIFIED (AC1).
    /// </summary>
    /// <returns>The decision of the playability.</returns>
    private static PlayabilityDecision Playable() => PlayabilityDecision.Playable();
}
