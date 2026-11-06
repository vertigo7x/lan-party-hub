using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using LANPartyHub.Client.Application.GameLauncher;
using LANPartyHub.Client.Domain.Install;
using LANPartyHub.Client.Presentation.GameLauncher;
using LANPartyHub.Client.Presentation.Library;
using LANPartyHub.Shared.Contracts;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The WIRING of the table of actions of the Biblioteca (FR-9, AC1, AC2, UX-DR-17,
/// UX-DR-18, UX-DR-20).
///
/// <para>
/// La fila de la Biblioteca lleva la MISMA tabla de acciones que la ficha, así que sus
/// delegates de «Jugar», de la primaria y de la secundaria tienen que LLEGAR a la fila:
/// sin ellos las tres pulsaciones son MUERTAS y la reparación, la re-descarga y el
/// diagnóstico NUNCA se ejecutan (AC1, AC2).
/// </para>
/// <para>
/// La primaria es POLIMÓRFICA —«Reparar» o «Descargar de nuevo»— así que el seam
/// transporta ALSO WHICH primary action was pressed, and the wiring registers the RIGHT
/// intention (AC1, AC2, UX-DR-20).
/// </para>
/// <para>
/// La clase vive en la COLECCIÓN de las superficies de Avalonia, que NO corre en
/// paralelo: la plataforma headless tiene UN dispatcher con un hilo propio (AC2,
/// UX-DR-37).
/// </para>
/// </summary>
[Collection(AvaloniaSurfaceCollection.Name)]
public sealed class LibrarySurfaceActionsTests
{
    private const string GameId = "0193f2a1b2c34d4e5f60718293a4b5c6";

    /// <summary>
    /// AC1: the action «Jugar» of a row of the Biblioteca is NOT a dead press: it
    /// reaches the delegate that the surface received (AC1, UX-DR-19).
    /// </summary>
    [AvaloniaFact]
    public void The_play_action_of_a_row_reaches_the_delegate_of_the_surface()
    {
        var played = new List<string>();

        var surface = new LibrarySurface(
            new LibrarySurfaceViewModel(),
            gameId => played.Add(gameId));

        surface.ApplyInstallations([PlayableInstallation()]);

        var actions = SingleActionsOf(surface);

        actions.PlayButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        Assert.Equal([GameId], played);
    }

    /// <summary>
    /// AC1, AC2: the PRIMARY action of a row of the Biblioteca is NOT a dead press: it
    /// reaches the delegate of the surface (AC1, AC2).
    /// </summary>
    [AvaloniaFact]
    public void The_primary_action_of_a_row_reaches_the_delegate_of_the_surface()
    {
        var primaries = new List<(string GameId, PlayabilityAction Action)>();

        var surface = new LibrarySurface(
            new LibrarySurfaceViewModel(),
            onPrimary: (gameId, action) => primaries.Add((gameId, action)));

        surface.ApplyInstallations([RepairableInstallation()]);

        var actions = SingleActionsOf(surface);

        actions.PrimaryButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        var primary = Assert.Single(primaries);

        Assert.Equal(GameId, primary.GameId);
        Assert.Equal(PlayabilityAction.Repair, primary.Action);
    }

    /// <summary>
    /// AC1, AC2: the SECONDARY action of a row of the Biblioteca is NOT a dead press: it
    /// reaches the delegate of the surface (AC1, AC2).
    /// </summary>
    [AvaloniaFact]
    public void The_secondary_action_of_a_row_reaches_the_delegate_of_the_surface()
    {
        var diagnosed = new List<string>();

        var surface = new LibrarySurface(
            new LibrarySurfaceViewModel(),
            onSecondary: gameId => diagnosed.Add(gameId));

        surface.ApplyInstallations([RepairableInstallation()]);

        var actions = SingleActionsOf(surface);

        actions.SecondaryButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        Assert.Equal([GameId], diagnosed);
    }

    /// <summary>
    /// AC1, UX-DR-20: the primary action of a REPAIRABLE installation reports «Reparar»,
    /// so the wiring registers the intention of REPAIRING (AC1, UX-DR-20).
    /// </summary>
    [AvaloniaFact]
    public void The_primary_action_of_a_repairable_installation_reports_the_repair()
    {
        var primaries = new List<PlayabilityAction>();

        var surface = new LibrarySurface(
            new LibrarySurfaceViewModel(),
            onPrimary: (_, action) => primaries.Add(action));

        surface.ApplyInstallations([RepairableInstallation()]);

        SingleActionsOf(surface)
            .PrimaryButton
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        Assert.Equal([PlayabilityAction.Repair], primaries);
    }

    /// <summary>
    /// AC2: the primary action of an installation that is NOT repairable reports
    /// «Descargar de nuevo», so the wiring registers the intention of RE-DOWNLOADING and
    /// NOT the one of repairing (AC2).
    /// </summary>
    [AvaloniaFact]
    public void The_primary_action_of_an_installation_that_is_not_repairable_reports_the_redownload()
    {
        var primaries = new List<PlayabilityAction>();

        var surface = new LibrarySurface(
            new LibrarySurfaceViewModel(),
            onPrimary: (_, action) => primaries.Add(action));

        surface.ApplyInstallations([NotRepairableInstallation()]);

        SingleActionsOf(surface)
            .PrimaryButton
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        Assert.Equal([PlayabilityAction.Redownload], primaries);
    }

    /// <summary>
    /// AC1, AC2: the blocked «Jugar» of the Biblioteca REHUSA actuar, así que su
    /// pulsación NO lanza el juego y NO dispara ninguna otra acción (AC6, UX-DR-26).
    /// </summary>
    [AvaloniaFact]
    public void The_blocked_play_action_of_a_row_does_nothing()
    {
        var played = new List<string>();

        var surface = new LibrarySurface(
            new LibrarySurfaceViewModel(),
            gameId => played.Add(gameId));

        surface.ApplyInstallations([RepairableInstallation()]);

        var actions = SingleActionsOf(surface);

        actions.PlayButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        Assert.Empty(played);
    }

    /// <summary>
    /// Finds the table of actions of the only row of the Biblioteca (AC1, AC2).
    /// </summary>
    /// <param name="surface">Surface of the Biblioteca.</param>
    /// <returns>The table of actions of the row.</returns>
    private static GamePlayActionsView SingleActionsOf(LibrarySurface surface)
    {
        var row = Assert.Single(surface.List.Children);

        var actions = FindDescendants(row)
            .OfType<GamePlayActionsView>()
            .Single();

        return actions;
    }

    /// <summary>
    /// Finds the controls of a tree, so the test can reach the table of actions of the
    /// row (AC1, AC2).
    /// </summary>
    /// <param name="root">Root of the tree of controls.</param>
    /// <returns>The controls of the tree.</returns>
    private static IEnumerable<Control> FindDescendants(Control root)
    {
        foreach (var child in root.GetVisualChildren().OfType<Control>())
        {
            yield return child;

            foreach (var descendant in FindDescendants(child))
            {
                yield return descendant;
            }
        }
    }

    /// <summary>
    /// Builds an installation that is CERTIFIED, so «Jugar» is enabled (AC1).
    /// </summary>
    /// <returns>The installation of the row.</returns>
    private static InstalledGamePlayability PlayableInstallation() => new(
        GameId,
        "1.0.0",
        InstallationState.Installed,
        PlayabilityDecision.Playable());

    /// <summary>
    /// Builds an installation that is invalid and REPAIRABLE, so la primaria es «Reparar»
    /// (AC1, UX-DR-17).
    /// </summary>
    /// <returns>The installation of the row.</returns>
    private static InstalledGamePlayability RepairableInstallation() => new(
        GameId,
        "1.0.0",
        InstallationState.Repairable,
        PlayabilityDecision.Blocked(
            ErrorCatalog.InstallationIncompleteCode,
            PlayabilityAction.Repair));

    /// <summary>
    /// Builds an installation that is invalid and NOT repairable, so la primaria es
    /// «Descargar de nuevo» (AC2, UX-DR-17).
    /// </summary>
    /// <returns>The installation of the row.</returns>
    private static InstalledGamePlayability NotRepairableInstallation() => new(
        GameId,
        "1.0.0",
        InstallationState.Failed,
        PlayabilityDecision.Blocked(
            ErrorCatalog.GameExecutableMissingCode,
            PlayabilityAction.Redownload));
}
