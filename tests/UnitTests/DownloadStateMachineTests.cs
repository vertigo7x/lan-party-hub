using LANPartyHub.Client.Domain.StateMachines;
using LANPartyHub.Shared.Contracts;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// Transitions of the download state machine (AD-10). The client is the only executor, so
/// this table is declared once and every invalid transition is rejected without changing
/// the state of the operation.
/// </summary>
public sealed class DownloadStateMachineTests
{
    [Fact]
    public void A_new_operation_starts_in_queued()
    {
        var machine = new DownloadStateMachine();

        Assert.Equal(DownloadState.Queued, machine.Current);
        Assert.False(machine.IsTerminal);
    }

    [Theory]
    [InlineData(DownloadState.Queued, DownloadState.Downloading)]
    [InlineData(DownloadState.Downloading, DownloadState.Paused)]
    [InlineData(DownloadState.Downloading, DownloadState.Validating)]
    [InlineData(DownloadState.Downloading, DownloadState.Failed)]
    [InlineData(DownloadState.Paused, DownloadState.Downloading)]
    [InlineData(DownloadState.Paused, DownloadState.Cancelled)]
    [InlineData(DownloadState.Validating, DownloadState.Completed)]
    [InlineData(DownloadState.Validating, DownloadState.Failed)]
    public void A_declared_transition_is_accepted(DownloadState from, DownloadState to)
    {
        var machine = new DownloadStateMachine(from);

        machine.TransitionTo(to);

        Assert.Equal(to, machine.Current);
    }

    [Fact]
    public void The_valid_transition_of_the_matrix_is_accepted()
    {
        var machine = new DownloadStateMachine(DownloadState.Queued);

        var accepted = machine.TryTransitionTo(DownloadState.Downloading);

        Assert.True(accepted);
        Assert.Equal(DownloadState.Downloading, machine.Current);
    }

    [Fact]
    public void The_invalid_transition_of_the_matrix_is_rejected_and_the_state_does_not_change()
    {
        var machine = new DownloadStateMachine(DownloadState.Queued);

        var exception = Assert.Throws<InvalidStateTransitionException>(
            () => machine.TransitionTo(DownloadState.Completed));

        Assert.Equal(DownloadState.Queued, machine.Current);
        Assert.Equal(nameof(DownloadStateMachine), exception.StateMachineName);
        Assert.Equal(nameof(DownloadState.Queued), exception.CurrentState);
        Assert.Equal(nameof(DownloadState.Completed), exception.NextState);
    }

    [Fact]
    public void A_rejected_transition_leaves_the_state_untouched()
    {
        var machine = new DownloadStateMachine(DownloadState.Queued);

        var accepted = machine.TryTransitionTo(DownloadState.Completed);

        Assert.False(accepted);
        Assert.Equal(DownloadState.Queued, machine.Current);
    }

    [Theory]
    [InlineData(DownloadState.Completed)]
    [InlineData(DownloadState.Cancelled)]
    [InlineData(DownloadState.Failed)]
    public void A_terminal_state_rejects_every_transition(DownloadState terminalState)
    {
        var machine = new DownloadStateMachine(terminalState);

        Assert.True(machine.IsTerminal);
        Assert.True(DownloadStateMachine.IsTerminalState(terminalState));

        foreach (var candidate in Enum.GetValues<DownloadState>())
        {
            var accepted = machine.TryTransitionTo(candidate);

            Assert.False(accepted);
            Assert.Equal(terminalState, machine.Current);
        }
    }

    [Fact]
    public void Completed_is_terminal_for_a_download_operation()
    {
        var machine = new DownloadStateMachine(DownloadState.Completed);

        var accepted = machine.TryTransitionTo(DownloadState.Downloading);

        Assert.False(accepted);
        Assert.Equal(DownloadState.Completed, machine.Current);
    }

    [Theory]
    [InlineData(DownloadState.Queued)]
    [InlineData(DownloadState.Downloading)]
    [InlineData(DownloadState.Paused)]
    [InlineData(DownloadState.Validating)]
    public void A_non_terminal_state_is_not_terminal(DownloadState state)
    {
        Assert.False(DownloadStateMachine.IsTerminalState(state));
    }

    [Fact]
    public void The_full_download_happy_path_ends_in_completed()
    {
        var machine = new DownloadStateMachine();

        machine.TransitionTo(DownloadState.Downloading);
        machine.TransitionTo(DownloadState.Paused);
        machine.TransitionTo(DownloadState.Downloading);
        machine.TransitionTo(DownloadState.Validating);
        machine.TransitionTo(DownloadState.Completed);

        Assert.Equal(DownloadState.Completed, machine.Current);
        Assert.True(machine.IsTerminal);
    }
}
