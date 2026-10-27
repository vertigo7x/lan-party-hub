using LANPartyHub.Client.Domain.StateMachines;
using LANPartyHub.Shared.Contracts;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// Transitions of the installation state machine (AD-10). The client is the only executor,
/// so this table is declared once and every invalid transition is rejected without changing
/// the state of the operation.
/// </summary>
public sealed class InstallationStateMachineTests
{
    [Fact]
    public void A_new_operation_starts_in_pending()
    {
        var machine = new InstallationStateMachine();

        Assert.Equal(InstallationState.Pending, machine.Current);
        Assert.False(machine.IsTerminal);
    }

    [Theory]
    [InlineData(InstallationState.Pending, InstallationState.Installing)]
    [InlineData(InstallationState.Installing, InstallationState.ValidatingInstallation)]
    [InlineData(InstallationState.Installing, InstallationState.AwaitingConfirmation)]
    [InlineData(InstallationState.Installing, InstallationState.Failed)]
    [InlineData(InstallationState.AwaitingConfirmation, InstallationState.Installing)]
    [InlineData(InstallationState.ValidatingInstallation, InstallationState.Installed)]
    [InlineData(InstallationState.ValidatingInstallation, InstallationState.Repairable)]
    [InlineData(InstallationState.ValidatingInstallation, InstallationState.Failed)]
    public void A_declared_transition_is_accepted(InstallationState from, InstallationState to)
    {
        var machine = new InstallationStateMachine(from);

        machine.TransitionTo(to);

        Assert.Equal(to, machine.Current);
    }

    [Fact]
    public void The_valid_transition_of_the_matrix_is_accepted()
    {
        var machine = new InstallationStateMachine(InstallationState.Pending);

        var accepted = machine.TryTransitionTo(InstallationState.Installing);

        Assert.True(accepted);
        Assert.Equal(InstallationState.Installing, machine.Current);
    }

    [Fact]
    public void An_invalid_transition_is_rejected_and_the_state_does_not_change()
    {
        var machine = new InstallationStateMachine(InstallationState.Pending);

        var exception = Assert.Throws<InvalidStateTransitionException>(
            () => machine.TransitionTo(InstallationState.Installed));

        Assert.Equal(InstallationState.Pending, machine.Current);
        Assert.Equal(nameof(InstallationStateMachine), exception.StateMachineName);
    }

    [Theory]
    [InlineData(InstallationState.Installed)]
    [InlineData(InstallationState.Repairable)]
    [InlineData(InstallationState.Failed)]
    public void A_terminal_state_rejects_every_transition(InstallationState terminalState)
    {
        var machine = new InstallationStateMachine(terminalState);

        Assert.True(machine.IsTerminal);
        Assert.True(InstallationStateMachine.IsTerminalState(terminalState));

        foreach (var candidate in Enum.GetValues<InstallationState>())
        {
            var accepted = machine.TryTransitionTo(candidate);

            Assert.False(accepted);
            Assert.Equal(terminalState, machine.Current);
        }
    }

    [Fact]
    public void A_privileged_operation_asks_for_confirmation_and_resumes_from_it()
    {
        var machine = new InstallationStateMachine(InstallationState.Pending);

        machine.TransitionTo(InstallationState.Installing);
        machine.TransitionTo(InstallationState.AwaitingConfirmation);
        machine.TransitionTo(InstallationState.Installing);
        machine.TransitionTo(InstallationState.ValidatingInstallation);
        machine.TransitionTo(InstallationState.Installed);

        Assert.Equal(InstallationState.Installed, machine.Current);
        Assert.True(machine.IsTerminal);
    }

    [Fact]
    public void An_incomplete_installation_is_repairable_and_not_installed()
    {
        var machine = new InstallationStateMachine(InstallationState.Pending);

        machine.TransitionTo(InstallationState.Installing);
        machine.TransitionTo(InstallationState.ValidatingInstallation);
        machine.TransitionTo(InstallationState.Repairable);

        Assert.Equal(InstallationState.Repairable, machine.Current);
        Assert.True(machine.IsTerminal);
    }
}
