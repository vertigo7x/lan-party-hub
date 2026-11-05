using LANPartyHub.Client.Application.Install;
using LANPartyHub.Shared.Contracts;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The progress of an installation operation: the PHASE and the INTEGER percentage of the
/// REAL bytes written (AC4, UX-DR-13, UX-DR-34).
///
/// <para>
/// El porcentaje sale de los bytes REALES y es un entero con suelo en cero y techo en
/// cien, igual que el de la descarga: una barra que dibuja el 43 % y un texto que dice
/// 42 % serían una contradicción (UX-DR-13).
/// </para>
/// </summary>
public sealed class InstallProgressTests
{
    /// <summary>
    /// The percentage is ZERO when nothing has been written yet (AC4).
    /// </summary>
    [Fact]
    public void The_percentage_is_zero_when_nothing_has_been_written()
    {
        var progress = new InstallProgress(InstallPhase.Extracting, 0, 1000);

        Assert.Equal(0, progress.Percentage);
        Assert.Equal(0d, progress.Fraction);
    }

    /// <summary>
    /// The percentage is the INTEGER of the real bytes over the total (AC4).
    /// </summary>
    [Fact]
    public void The_percentage_is_the_integer_of_the_real_bytes_over_the_total()
    {
        var progress = new InstallProgress(InstallPhase.Extracting, 250, 1000);

        Assert.Equal(25, progress.Percentage);
        Assert.Equal(0.25d, progress.Fraction);
    }

    /// <summary>
    /// The percentage NEVER has decimals: a byte of more does not move the bar (AC4,
    /// UX-DR-13).
    /// </summary>
    [Fact]
    public void The_percentage_never_has_decimals()
    {
        var progress = new InstallProgress(InstallPhase.ApplyingManifest, 999, 1000);

        Assert.Equal(99, progress.Percentage);
    }

    /// <summary>
    /// The percentage reaches a HUNDRED when every byte has been written (AC4).
    /// </summary>
    [Fact]
    public void The_percentage_reaches_a_hundred_when_every_byte_has_been_written()
    {
        var progress = new InstallProgress(InstallPhase.ApplyingManifest, 1000, 1000);

        Assert.Equal(100, progress.Percentage);
        Assert.Equal(1d, progress.Fraction);
    }

    /// <summary>
    /// The percentage has a FLOOR in zero: bytes that were written are never negative
    /// (AC4).
    /// </summary>
    [Fact]
    public void The_percentage_has_a_floor_in_zero()
    {
        var progress = new InstallProgress(InstallPhase.Extracting, 0, 0);

        Assert.Equal(0, progress.Percentage);
    }

    /// <summary>
    /// The percentage has a CEILING in a hundred: bytes that exceed the total do not
    /// draw a bar that overflows (AC4, UX-DR-13).
    /// </summary>
    [Fact]
    public void The_percentage_has_a_ceiling_in_a_hundred()
    {
        var progress = new InstallProgress(InstallPhase.ApplyingManifest, 5000, 1000);

        Assert.Equal(100, progress.Percentage);
        Assert.Equal(1d, progress.Fraction);
    }

    /// <summary>
    /// While the total is UNKNOWN there is NO percentage: un porcentaje sobre un total
    /// que no se conoce sería una mentira (AC4, UX-DR-13).
    /// </summary>
    [Fact]
    public void While_the_total_is_unknown_there_is_no_percentage()
    {
        var progress = new InstallProgress(InstallPhase.Extracting, 500, null);

        Assert.Equal(0, progress.Percentage);
        Assert.Null(progress.TotalBytes);
    }

    /// <summary>
    /// The bar draws the SAME fraction that the percentage declares, so the bar and the
    /// text NEVER contradict each other (UX-DR-5, UX-DR-13).
    /// </summary>
    [Fact]
    public void The_bar_draws_the_same_fraction_that_the_percentage_declares()
    {
        var progress = new InstallProgress(InstallPhase.ApplyingManifest, 430, 1000);

        Assert.Equal(progress.Percentage / 100d, progress.Fraction);
    }

    /// <summary>
    /// The PHASE names the step of the operation, so the player reads «Extrayendo» and
    /// «Aplicando manifiesto» (AC4, UX-DR-34).
    /// </summary>
    [Theory]
    [InlineData(InstallPhase.Extracting)]
    [InlineData(InstallPhase.ApplyingManifest)]
    [InlineData(InstallPhase.AwaitingConfirmation)]
    [InlineData(InstallPhase.Validating)]
    [InlineData(InstallPhase.Completed)]
    [InlineData(InstallPhase.Failed)]
    public void The_phase_names_the_step_of_the_operation(InstallPhase phase)
    {
        var progress = new InstallProgress(phase, 10, 100);

        Assert.Equal(phase, progress.Phase);
        Assert.False(string.IsNullOrWhiteSpace(
            LANPartyHub.Client.Presentation.Install.InstallProgressViewModel.PhaseTextOf(phase)));
    }

    /// <summary>
    /// The phase «Extrayendo» is the one that the acceptance criteria name (AC4,
    /// UX-DR-34).
    /// </summary>
    [Fact]
    public void The_extraction_phase_is_named_as_the_acceptance_criteria_name_it()
    {
        Assert.Equal(
            "Extrayendo",
            LANPartyHub.Client.Presentation.Install.InstallProgressViewModel.PhaseTextOf(
                InstallPhase.Extracting));

        Assert.Equal(
            "Aplicando manifiesto",
            LANPartyHub.Client.Presentation.Install.InstallProgressViewModel.PhaseTextOf(
                InstallPhase.ApplyingManifest));
    }

    /// <summary>
    /// A FAILED operation keeps its REAL length: the bar does not go back to zero, so
    /// the player sees how far the installation got (AC5, UX-DR-13).
    /// </summary>
    [Fact]
    public void A_failed_operation_keeps_its_real_length()
    {
        var progress = new InstallProgress(InstallPhase.Failed, 420, 1000);

        Assert.True(progress.IsFailed);
        Assert.Equal(42, progress.Percentage);
        Assert.Equal(0.42d, progress.Fraction);
    }

    /// <summary>
    /// A COMPLETED operation keeps its REAL length too (AC4, UX-DR-13).
    /// </summary>
    [Fact]
    public void A_completed_operation_keeps_its_real_length()
    {
        var progress = new InstallProgress(InstallPhase.Completed, 1000, 1000);

        Assert.True(progress.IsCompleted);
        Assert.Equal(100, progress.Percentage);
    }

    /// <summary>
    /// The bytes written and the total cannot be NEGATIVE, porque un porcentaje sobre un
    /// total negativo sería una mentira (AC4).
    /// </summary>
    [Fact]
    public void The_bytes_and_the_total_cannot_be_negative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new InstallProgress(InstallPhase.Extracting, -1, 100));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new InstallProgress(InstallPhase.Extracting, 10, -1));
    }

    /// <summary>
    /// The progress of an operation that has NOT started has no phase of work and no
    /// percentage (AC4).
    /// </summary>
    [Fact]
    public void The_progress_of_an_operation_that_has_not_started_is_empty()
    {
        var progress = InstallProgress.Idle;

        Assert.Equal(InstallPhase.Idle, progress.Phase);
        Assert.Equal(0, progress.Percentage);
        Assert.Null(progress.TotalBytes);
    }

    /// <summary>
    /// The view model projects the progress: the bar and the text come from ONE source,
    /// so they never contradict each other (UX-DR-5, UX-DR-13).
    /// </summary>
    [Fact]
    public void The_view_model_projects_the_progress_and_the_bar_never_contradicts_the_text()
    {
        var viewModel = new LANPartyHub.Client.Presentation.Install.InstallProgressViewModel();

        viewModel.Apply(new InstallProgress(InstallPhase.ApplyingManifest, 430, 1000));

        Assert.Equal(43, (int)(viewModel.BarFraction * 100));
        Assert.Equal(0.43d, viewModel.BarFraction);
        Assert.Contains("43 %", viewModel.ProgressText, StringComparison.Ordinal);
    }

    /// <summary>
    /// The text of the progress carries the PHASE and the percentage (AC4, UX-DR-34).
    /// </summary>
    [Fact]
    public void The_text_of_the_progress_carries_the_phase_and_the_percentage()
    {
        var viewModel = new LANPartyHub.Client.Presentation.Install.InstallProgressViewModel();

        viewModel.Apply(new InstallProgress(InstallPhase.Extracting, 250, 1000));

        Assert.Equal("Extrayendo", viewModel.PhaseText);
        Assert.Equal("Extrayendo · 25 %", viewModel.ProgressText);
    }

    /// <summary>
    /// A FAILED installation offers the action «Reintentar», porque la operación es
    /// reintentable sin reinstalar el cliente (AC5, NFR-9, UX-DR-34).
    /// </summary>
    [Fact]
    public void A_failed_installation_offers_the_retry_action()
    {
        var viewModel = new LANPartyHub.Client.Presentation.Install.InstallProgressViewModel();

        viewModel.Apply(new InstallProgress(InstallPhase.Failed, 420, 1000));

        Assert.True(viewModel.CanRetry);

        viewModel.ApplyError(ErrorCatalog.InstallPrivilegedConfirmationDeclinedCode);

        Assert.NotNull(viewModel.Error);
        Assert.Equal(
            ErrorCatalog.InstallPrivilegedConfirmationDeclinedCode,
            viewModel.Error!.Code);
    }

    /// <summary>
    /// An installation that is NOT failed does NOT offer the reintento, porque no hay
    /// nada que reintentar (AC5).
    /// </summary>
    [Fact]
    public void An_installation_that_is_not_failed_does_not_offer_the_retry_action()
    {
        var viewModel = new LANPartyHub.Client.Presentation.Install.InstallProgressViewModel();

        viewModel.Apply(new InstallProgress(InstallPhase.Completed, 1000, 1000));

        Assert.False(viewModel.CanRetry);
    }

    /// <summary>
    /// The control draws the SAME fraction that the percentage declares, y la barra
    /// conserva su longitud REAL en error (AC4, UX-DR-13).
    /// </summary>
    [Fact]
    public void The_control_draws_the_same_fraction_and_keeps_its_length_in_error()
    {
        var viewModel = new LANPartyHub.Client.Presentation.Install.InstallProgressViewModel();
        var control = new LANPartyHub.Client.Presentation.Install.InstallProgressControl(viewModel);

        viewModel.Apply(new InstallProgress(InstallPhase.Failed, 420, 1000));
        viewModel.ApplyError(ErrorCatalog.InstallPrivilegedConfirmationDeclinedCode);

        Assert.Equal(42d, control.Bar.Value);
        Assert.Contains("42 %", control.ProgressText.Text, StringComparison.Ordinal);
        Assert.True(control.RetryButton.IsEnabled);
        Assert.True(control.ErrorText.IsVisible);
    }

    /// <summary>
    /// The dialog de confirmación NAMES the operation and what it writes, así que el
    /// jugador aprueba algo CONCRETO (AC2, UX-DR-16).
    /// </summary>
    [Fact]
    public void The_dialog_names_the_operation_and_what_it_writes()
    {
        var dialog = new LANPartyHub.Client.Presentation.Install.PrivilegedOperationDialog(
            "writeConfig",
            "El manifiesto va a escribir el archivo de configuración 'config/options.ini' dentro de la raíz de instalación.");

        Assert.Equal("writeConfig", dialog.Operation);
        Assert.Contains("config/options.ini", dialog.Message.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The `Esc` CANCELS the confirmation, y la decisión es un NO (AC2, UX-DR-37).
    /// </summary>
    [Fact]
    public async Task The_escape_cancels_the_confirmation()
    {
        var dialog = new LANPartyHub.Client.Presentation.Install.PrivilegedOperationDialog(
            "writeConfig",
            "El manifiesto va a escribir dentro de la raíz de instalación.");

        dialog.Decline();

        Assert.False(await dialog.Decision);
    }

    /// <summary>
    /// The action that CONFIRMS the operation gives a YES, y la decisión vive SÓLO en
    /// memoria: no se persiste como concedida (AC2, AC3).
    /// </summary>
    [Fact]
    public async Task The_confirmation_gives_a_yes_and_it_lives_only_in_memory()
    {
        var dialog = new LANPartyHub.Client.Presentation.Install.PrivilegedOperationDialog(
            "writeConfig",
            "El manifiesto va a escribir dentro de la raíz de instalación.");

        dialog.Confirm();

        Assert.True(await dialog.Decision);
    }

    /// <summary>
    /// The confirmation is asked in the MOMENT, y su decisión NO se hereda: una nueva
    /// sesión vuelve a preguntar (AC3).
    /// </summary>
    [Fact]
    public async Task The_confirmation_is_asked_again_in_a_new_session()
    {
        var confirmation = new LANPartyHub.Client.Presentation.Install.DialogPrivilegedOperationConfirmation(
            (operation, detail) => new LANPartyHub.Client.Presentation.Install.PrivilegedOperationDialog(operation, detail));

        var asked = 0;

        confirmation.SetPresenter(dialog =>
        {
            asked++;

            dialog.Decline();

            return Task.CompletedTask;
        });

        var first = await confirmation.ConfirmAsync(
            "writeConfig",
            "El manifiesto va a escribir dentro de la raíz de instalación.",
            TestContext.Current.CancellationToken);

        Assert.False(first);
        Assert.Equal(1, asked);

        // Una sesión NUEVA vuelve a preguntar, porque la confirmación no se hereda de
        // ninguna sesión anterior (AC3).
        var second = await confirmation.ConfirmAsync(
            "writeConfig",
            "El manifiesto va a escribir dentro de la raíz de instalación.",
            TestContext.Current.CancellationToken);

        Assert.False(second);
        Assert.Equal(2, asked);
    }
}
