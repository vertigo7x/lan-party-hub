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
        Assert.False(machine.IsTerminal);
    }

    /// <summary>
    /// AC1, AD-10: «Repairable» NO es terminal, porque una instalación reparable se
    /// REABRE para la reparación: la máquina vuelve a pasar por «Installing» y termina en
    /// «Installed» o en «Failed».
    /// </summary>
    [Theory]
    [InlineData(InstallationState.Installing)]
    [InlineData(InstallationState.Failed)]
    public void A_repairable_installation_reopens_into_the_repair(InstallationState nextState)
    {
        var machine = new InstallationStateMachine(InstallationState.Repairable);

        Assert.False(machine.IsTerminal);
        Assert.False(InstallationStateMachine.IsTerminalState(InstallationState.Repairable));

        machine.TransitionTo(nextState);

        Assert.Equal(nextState, machine.Current);
    }

    /// <summary>
    /// AC1, AD-10: la REPARACIÓN recorre la misma ruta que la instalación —«Installing» y
    /// «ValidatingInstallation»— y termina en «Installed» cuando la certificación pasa.
    /// </summary>
    [Fact]
    public void The_repair_walks_the_same_path_than_the_installation()
    {
        var machine = new InstallationStateMachine(InstallationState.Repairable);

        machine.TransitionTo(InstallationState.Installing);
        machine.TransitionTo(InstallationState.ValidatingInstallation);
        machine.TransitionTo(InstallationState.Installed);

        Assert.Equal(InstallationState.Installed, machine.Current);
        Assert.True(machine.IsTerminal);
    }

    /// <summary>
    /// AC1, AD-10: la reparación puede terminar en «Failed», que sí es terminal.
    /// </summary>
    [Fact]
    public void A_failed_repair_ends_in_the_terminal_failed_state()
    {
        var machine = new InstallationStateMachine(InstallationState.Repairable);

        machine.TransitionTo(InstallationState.Installing);
        machine.TransitionTo(InstallationState.Failed);

        Assert.Equal(InstallationState.Failed, machine.Current);
        Assert.True(machine.IsTerminal);
    }

    /// <summary>
    /// AC1, AD-10: la reparación NO vuelve a «Repairable», porque una instalación cuyo
    /// paquete validado ya no conserva su hash deja de ser reparable.
    /// </summary>
    [Fact]
    public void The_repair_does_not_go_back_to_repairable()
    {
        var machine = new InstallationStateMachine(InstallationState.Repairable);

        machine.TransitionTo(InstallationState.Installing);

        Assert.False(machine.TryTransitionTo(InstallationState.Repairable));
        Assert.Equal(InstallationState.Installing, machine.Current);
    }
}
