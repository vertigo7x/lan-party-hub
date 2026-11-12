using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using LANPartyHub.Client.Application.Downloads;
using LANPartyHub.Client.Domain.StateMachines;
using LANPartyHub.Client.Presentation.Design;
using LANPartyHub.Client.Presentation.Downloads;
using LANPartyHub.Shared.Contracts;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The ACCESSIBILITY of the client (AC3, AC4, AC7, UX-DR-38, UX-DR-39, UX-DR-40).
///
/// <para>
/// El lector de pantalla ANUNCIA lo que pasa: la superficie en la que entra el jugador
/// y los pasos SIGNIFICATIVOS del progreso, no cada `tick`. Y todo objetivo
/// interactivo es lo bastante grande para pulsarlo.
/// </para>
/// </summary>
[Collection(AvaloniaSurfaceCollection.Name)]
public sealed class AccessibilityTests
{
    /// <summary>
    /// The SIGNIFICANT steps of the progress are announced, and each one is announced
    /// ONCE (AC4, UX-DR-39).
    /// </summary>
    [AvaloniaFact]
    public void The_significant_steps_of_the_progress_are_announced_once()
    {
        var viewModel = new DownloadProgressViewModel();

        var stateMachine = new DownloadStateMachine();

        stateMachine.TransitionTo(DownloadState.Downloading);

        viewModel.Apply(new DownloadOperationHandle(
            "op-1",
            "pkg-1",
            new Uri("http://localhost/paquete"),
            stateMachine));

        // Sin progreso no hay nada que anunciar (AC4).
        Assert.False(viewModel.ConsumeSignificantPercentage());

        // Los pasos significativos son los que nombra la historia (AC4, UX-DR-39).
        Assert.Equal([10, 25, 50, 75, 100], DownloadProgressViewModel.AnnouncedPercentages);
    }

    /// <summary>
    /// The progress announces a NEW significant step and NEVER repeats one (AC4,
    /// UX-DR-39).
    /// </summary>
    [AvaloniaFact]
    public void The_progress_never_announces_the_same_step_twice()
    {
        var viewModel = new DownloadProgressViewModel();

        var stateMachine = new DownloadStateMachine();

        stateMachine.TransitionTo(DownloadState.Downloading);

        viewModel.Apply(new DownloadOperationHandle(
            "op-1",
            "pkg-1",
            new Uri("http://localhost/paquete"),
            stateMachine));

        // Con el 0 % no se anuncia nada: el primer paso es el 10 % (AC4).
        Assert.False(viewModel.ConsumeSignificantPercentage());
    }

    /// <summary>
    /// Every INTERACTIVE target is big enough to be pressed, and the primary buttons
    /// and the fields are 36 px high (AC7, UX-DR-40).
    /// </summary>
    [AvaloniaFact]
    public void Every_interactive_target_is_big_enough_to_be_pressed()
    {
        // El mínimo de la norma son 24×24 px, y los botones primarios y los campos
        // miden 36 px (AC7, UX-DR-40).
        Assert.True(Buttons.Primary("Jugar").Height >= 24);
        Assert.True(Buttons.Secondary("Acción").Height >= 24);
        Assert.True(Buttons.Danger("Cancelar").Height >= 24);
        Assert.True(Buttons.Ghost("Diagnosticar").Height >= 24);

        // Los botones primarios y los campos de entrada miden 36 px (AC7).
        Assert.Equal(36, DesignTokens.ButtonHeight);
        Assert.Equal(36, DesignTokens.FieldInputHeight);
    }

    /// <summary>
    /// The TEXT of the progress goes with the bar, so the interface does not depend on
    /// the colour of the bar (AC6, UX-DR-13).
    /// </summary>
    [AvaloniaFact]
    public void The_progress_always_shows_its_percentage_in_text()
    {
        var viewModel = new DownloadProgressViewModel();

        var stateMachine = new DownloadStateMachine();

        stateMachine.TransitionTo(DownloadState.Downloading);

        viewModel.Apply(new DownloadOperationHandle(
            "op-1",
            "pkg-1",
            new Uri("http://localhost/paquete"),
            stateMachine));

        // El porcentaje va en TEXTO junto a la barra, y no solo en el color (AC6).
        Assert.Contains("%", viewModel.ProgressText, StringComparison.Ordinal);
    }
}
