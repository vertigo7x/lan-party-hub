using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Presentation.Status;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The state of the game that is RUNNING (AD-19, AC2, AC3, UX-DR-6, UX-DR-43).
///
/// <para>
/// Los tests fijan lo que el PANEL PERSISTENTE muestra: «En ejecución» mientras el
/// proceso vive y «Ningún juego en ejecución» cuando no hay ninguno, y el panel NUNCA
/// deja un espacio vacío (AC2, UX-DR-6).
/// </para>
/// <para>
/// El estado de ejecución vive EN MEMORIA del cliente: no se persiste, no hay tabla
/// nueva ni migración (AD-22, AC2).
/// </para>
/// </summary>
public sealed class GameRunStatusViewModelTests
{
    [Fact]
    public void The_panel_shows_that_no_game_is_running()
    {
        var viewModel = new GameRunStatusViewModel();

        Assert.False(viewModel.IsRunning);
        Assert.Equal("Ningún juego en ejecución", viewModel.StatusText);
        Assert.Equal(GameRunStatusViewModel.NoGameRunningText, viewModel.StatusText);
    }

    [Fact]
    public void The_panel_shows_that_a_game_is_running()
    {
        // AC2, UX-DR-6: el panel muestra «En ejecución» mientras el proceso vive.
        var viewModel = new GameRunStatusViewModel();

        viewModel.ApplyLaunch(new StubProcessHandle(isRunning: true), "Counter-Strike 2");

        Assert.True(viewModel.IsRunning);
        Assert.Equal("En ejecución", viewModel.StatusText);
        Assert.Equal(GameRunStatusViewModel.RunningText, viewModel.StatusText);
    }

    [Fact]
    public void The_panel_shows_that_no_game_is_running_after_the_process_finished()
    {
        // AC2: un proceso que terminó deja de estar en ejecución, aunque el handle siga
        // en memoria.
        var viewModel = new GameRunStatusViewModel();
        var handle = new StubProcessHandle(isRunning: true);

        viewModel.ApplyLaunch(handle, "Counter-Strike 2");

        handle.IsRunning = false;

        viewModel.Refresh();

        Assert.False(viewModel.IsRunning);
        Assert.Equal(GameRunStatusViewModel.NoGameRunningText, viewModel.StatusText);
    }

    [Fact]
    public void The_panel_forgets_the_game_that_stopped()
    {
        // AC2: el panel no conserva el nombre de un juego que ya no está en ejecución.
        var viewModel = new GameRunStatusViewModel();

        viewModel.ApplyLaunch(new StubProcessHandle(isRunning: true), "Counter-Strike 2");

        Assert.Equal("Counter-Strike 2", viewModel.GameName);

        viewModel.ApplyStopped();

        Assert.Null(viewModel.GameName);
        Assert.Equal(GameRunStatusViewModel.NoGameRunningText, viewModel.StatusText);
    }

    [Fact]
    public void The_panel_publishes_its_change_when_a_game_is_launched()
    {
        // AC2: el panel se repinta cuando el estado de ejecución cambia.
        var viewModel = new GameRunStatusViewModel();

        var changes = 0;
        viewModel.Changed += () => changes++;

        viewModel.ApplyLaunch(new StubProcessHandle(isRunning: true), "Counter-Strike 2");

        Assert.Equal(1, changes);
    }

    [Fact]
    public void The_panel_publishes_its_change_when_no_game_is_running()
    {
        var viewModel = new GameRunStatusViewModel();

        var changes = 0;
        viewModel.Changed += () => changes++;

        viewModel.ApplyStopped();

        Assert.Equal(1, changes);
    }

    [Fact]
    public void The_panel_never_shows_an_empty_space()
    {
        // AC2, UX-DR-6: el panel NUNCA deja un espacio vacío.
        var viewModel = new GameRunStatusViewModel();

        Assert.False(string.IsNullOrWhiteSpace(viewModel.StatusText));

        viewModel.ApplyLaunch(new StubProcessHandle(isRunning: true), "Counter-Strike 2");

        Assert.False(string.IsNullOrWhiteSpace(viewModel.StatusText));
    }

    [Fact]
    public void The_microcopy_of_the_running_state_is_not_in_shared()
    {
        // AD-18, AR-3: la microcopia del estado de ejecución es DEL CLIENTE, así que la
        // tabla de microcopy de `Shared` NO crece con ella.
        var source = File.ReadAllText(
            Path.Combine(RepositoryLayout.Root, "src", "Shared", "Contracts", "StateDisplayMap.cs"));

        Assert.DoesNotContain(GameRunStatusViewModel.RunningText, source, StringComparison.Ordinal);
        Assert.DoesNotContain(GameRunStatusViewModel.NoGameRunningText, source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Double of the handle of a process (AD-19, AD-16).
    /// </summary>
    private sealed class StubProcessHandle : IGameProcessHandle
    {
        public StubProcessHandle(bool isRunning) => IsRunning = isRunning;

        public int ProcessId => 4242;

        public bool IsRunning { get; set; }
    }
}
