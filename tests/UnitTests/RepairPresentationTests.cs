using LANPartyHub.Client.Application.Catalog;
using LANPartyHub.Client.Application.GameLauncher;
using LANPartyHub.Client.Presentation.Catalog;
using LANPartyHub.Client.Presentation.GameLauncher;
using LANPartyHub.Client.Presentation.Library;
using LANPartyHub.Shared.Contracts;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The PRESENTATION of the repair (AC1, AC2, AC3, UX-DR-6, UX-DR-17, UX-DR-18,
/// UX-DR-20, AR-8).
///
/// <para>
/// La consecuencia de la reparación se NOMBRA junto a «Reparar» y SIN modal, la acción
/// «Jugar» sigue BLOQUEADA con el motivo VISIBLE mientras la reparación dura, y la
/// insignia final compone el texto de la tabla de microcopia con la versión instalada
/// (AC1, AC2, AC3).
/// </para>
/// </summary>
public sealed class RepairPresentationTests
{
    private const string GameId = "0193f2a1b2c34d4e5f60718293a4b5c6";

    /// <summary>
    /// AC1, UX-DR-20: la consecuencia de la reparación se NOMBRA, con el texto EXACTO del
    /// contrato.
    /// </summary>
    [Fact]
    public void The_consequence_of_the_repair_is_named()
    {
        var viewModel = new GamePlayActionsViewModel(GameId, Repairable());

        Assert.Equal(
            "Se reparará la instalación; la descarga no se repite",
            viewModel.RepairConsequenceText);
    }

    /// <summary>
    /// AC1, UX-DR-20: la consecuencia se nombra SÓLO cuando la primaria es «Reparar», así
    /// que una instalación que NO es reparable no promete una reparación.
    /// </summary>
    [Fact]
    public void The_consequence_is_not_named_when_the_primary_action_is_not_the_repair()
    {
        var viewModel = new GamePlayActionsViewModel(
            GameId,
            PlayabilityDecision.Blocked(
                ErrorCatalog.GameExecutableMissingCode,
                PlayabilityAction.Redownload));

        Assert.Null(viewModel.RepairConsequenceText);
    }

    /// <summary>
    /// AC1, UX-DR-20: la consecuencia se NOMBRA y no se CONFIRMA: la reparación no pide un
    /// modal, porque no destruye trabajo recuperable.
    /// </summary>
    [Fact]
    public void The_repair_does_not_ask_for_a_confirmation()
    {
        var viewModel = new GamePlayActionsViewModel(GameId, Repairable());

        // La tabla de acciones NO tiene ninguna acción de confirmación: sólo la primaria
        // «Reparar» y la secundaria «Diagnosticar».
        Assert.Equal(
            PlayabilityAction.Repair,
            viewModel.PrimaryAction);
        Assert.Equal(
            PlayabilityAction.Diagnose,
            viewModel.SecondaryAction);
        Assert.DoesNotContain(
            "confirm",
            viewModel.RepairConsequenceText!.ToLowerInvariant(),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// AC2, UX-DR-17, UX-DR-18: mientras la reparación dura, la acción «Jugar» sigue
    /// BLOQUEADA con el motivo VISIBLE de la reparación en curso.
    /// </summary>
    [Fact]
    public void While_the_repair_runs_the_play_action_stays_blocked_with_a_visible_reason()
    {
        var viewModel = new GamePlayActionsViewModel(GameId, PlayabilityDecision.RepairInProgress());

        Assert.True(viewModel.IsBlocked);
        Assert.False(viewModel.CanPlay);
        Assert.True(viewModel.ShowsReason);
        Assert.True(viewModel.IsRepairInProgress);

        // El motivo visible sale del CATÁLOGO y nunca es el código estable (AD-15).
        Assert.Equal(
            ErrorCatalog.InstallationRepairInProgressCode,
            viewModel.ReasonCode);
        Assert.Equal(
            ErrorCatalog.Resolve(ErrorCatalog.InstallationRepairInProgressCode).Message,
            viewModel.ReasonText);
        Assert.NotEqual(viewModel.ReasonCode, viewModel.ReasonText);
    }

    /// <summary>
    /// AC2, UX-DR-18: el motivo de la reparación en curso NO es el de la instalación
    /// incompleta, así que el jugador puede distinguir los tres diagnósticos.
    /// </summary>
    [Fact]
    public void The_reason_of_the_repair_in_progress_is_its_own()
    {
        var repairInProgress = new GamePlayActionsViewModel(
            GameId,
            PlayabilityDecision.RepairInProgress());

        var incomplete = new GamePlayActionsViewModel(
            GameId,
            PlayabilityDecision.Blocked(
                ErrorCatalog.InstallationIncompleteCode,
                PlayabilityAction.Repair));

        Assert.NotEqual(incomplete.ReasonText, repairInProgress.ReasonText);
        Assert.NotEqual(incomplete.ReasonCode, repairInProgress.ReasonCode);
    }

    /// <summary>
    /// AC2, UX-DR-26: el nombre accesible de «Jugar» anuncia su estado bloqueado y el
    /// motivo visible, así que el lector de pantalla no depende del color.
    /// </summary>
    [Fact]
    public void The_accessible_name_announces_the_block_and_the_reason_of_the_repair()
    {
        var viewModel = new GamePlayActionsViewModel(GameId, PlayabilityDecision.RepairInProgress());

        Assert.Equal(
            $"Jugar (bloqueado): {ErrorCatalog.Resolve(ErrorCatalog.InstallationRepairInProgressCode).Message}",
            viewModel.PlayAccessibleName);
    }

    /// <summary>
    /// AC2, AC6: con la reparación en curso, la acción «Jugar» REHUSA actuar: no lanza
    /// ningún proceso ni dispara ninguna reparación implícita.
    /// </summary>
    [Fact]
    public async Task The_play_action_refuses_to_act_while_the_repair_runs()
    {
        var launches = 0;

        var command = new GamePlayCommand((_, _) =>
        {
            launches++;

            return Task.FromResult(true);
        });

        var launched = await command.ExecuteAsync(
            PlayabilityDecision.RepairInProgress(),
            GameId,
            TestContext.Current.CancellationToken);

        Assert.False(launched);
        Assert.Equal(0, launches);
    }

    /// <summary>
    /// AC3, UX-DR-6, AR-8: la insignia de la Biblioteca dice «Instalado · v1.2», con el
    /// texto de la tabla de microcopia y la versión EXACTA.
    /// </summary>
    [Fact]
    public void The_badge_of_the_library_says_installed_with_the_version()
    {
        var viewModel = new LibrarySurfaceViewModel();

        viewModel.ApplyInstallations(
        [
            new InstalledGamePlayability(
                GameId,
                "1.2",
                InstallationState.Installed,
                PlayabilityDecision.Playable()),
        ]);

        var entry = Assert.Single(viewModel.Entries);

        Assert.Equal("Instalado · 1.2", entry.InstalledVersionText);
        Assert.StartsWith(
            StateDisplayMap.GetText(InstallationState.Installed),
            entry.InstalledVersionText,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// AC3, UX-DR-6, AR-8: la insignia de la ficha dice «Ya instalado · v1.2», con el
    /// texto de la tabla de microcopia y la versión EXACTA.
    /// </summary>
    [Fact]
    public void The_badge_of_the_detail_says_already_installed_with_the_version()
    {
        var viewModel = new GameDetailViewModel(
            CreateGame(),
            CatalogViewState.Available,
            "1.2",
            PlayabilityDecision.Playable());

        Assert.Equal("Ya instalado · 1.2", viewModel.InstalledVersionText);
        Assert.StartsWith(
            StateDisplayMap.AlreadyInstalledText,
            viewModel.InstalledVersionText,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// AC3, UX-DR-17: con la instalación validada, la acción primaria es «Jugar».
    /// </summary>
    [Fact]
    public void A_validated_installation_shows_play_as_the_primary_action()
    {
        var viewModel = new GamePlayActionsViewModel(GameId, PlayabilityDecision.Playable());

        Assert.False(viewModel.IsBlocked);
        Assert.True(viewModel.CanPlay);
        Assert.Equal("Jugar", viewModel.PrimaryActionText);
        Assert.Null(viewModel.ReasonText);
    }

    /// <summary>
    /// AC1, UX-DR-20: la insignia y la primaria se REderivan al pintar, así que una
    /// reparación terminada cambia la tabla sin tocar el disco (AD-11, AD-22).
    /// </summary>
    [Fact]
    public void The_table_follows_the_new_decision_when_the_repair_finishes()
    {
        var viewModel = new GamePlayActionsViewModel(GameId, PlayabilityDecision.RepairInProgress());

        Assert.True(viewModel.IsBlocked);

        viewModel.Apply(PlayabilityDecision.Playable());

        Assert.False(viewModel.IsBlocked);
        Assert.True(viewModel.CanPlay);
        Assert.Equal("Jugar", viewModel.PrimaryActionText);
    }

    /// <summary>
    /// Builds the videogame of the ficha (AC3).
    /// </summary>
    /// <returns>The videogame of the ficha.</returns>
    private static GameDescriptor CreateGame() => new(
        GameId,
        "Juego de prueba",
        "Descripción del juego de prueba",
        ["Acción"],
        ["Estrategia"],
        "cover.png",
        ["captura-1.png"],
        "1.2",
        1024,
        1,
        4,
        "Windows 10",
        "Servidor",
        "Portable",
        new InstallationManifest.LaunchSettings("game.exe", ["-novid"], "game"),
        ["Windows 10"],
        "MIT",
        "Solo para el evento");

    /// <summary>
    /// Builds the decision of an installation that is invalid and REPARABLE (AC1).
    /// </summary>
    /// <returns>The decision of the playability.</returns>
    private static PlayabilityDecision Repairable() => PlayabilityDecision.Blocked(
        ErrorCatalog.InstallationIncompleteCode,
        PlayabilityAction.Repair);
}
