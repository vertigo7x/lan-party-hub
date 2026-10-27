using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Client.Domain.StateMachines;

/// <summary>
/// State machine of an installation operation (AD-10).
///
/// The client is the only executor of an installation operation, so the transition table is
/// declared here, once. The server only projects the resulting state (AD-10, AD-22).
/// Every transition that is not in the table is rejected and leaves the state unchanged.
/// </summary>
public sealed class InstallationStateMachine
{
    /// <summary>State every installation operation starts in.</summary>
    public const InstallationState InitialState = InstallationState.Pending;

    private static readonly IReadOnlyDictionary<InstallationState, IReadOnlyList<InstallationState>> AllowedTransitions =
        new Dictionary<InstallationState, IReadOnlyList<InstallationState>>
        {
            [InstallationState.Pending] = [InstallationState.Installing],
            [InstallationState.Installing] =
            [
                InstallationState.ValidatingInstallation,
                InstallationState.AwaitingConfirmation,
                InstallationState.Failed,
            ],
            [InstallationState.AwaitingConfirmation] = [InstallationState.Installing],
            [InstallationState.ValidatingInstallation] =
            [
                InstallationState.Installed,
                InstallationState.Repairable,
                InstallationState.Failed,
            ],
            [InstallationState.Installed] = [],
            [InstallationState.Repairable] = [],
            [InstallationState.Failed] = [],
        };

    /// <summary>Creates the state machine in the given state.</summary>
    /// <param name="initialState">State the operation starts in.</param>
    public InstallationStateMachine(InstallationState initialState = InitialState) => Current = initialState;

    /// <summary>Current state of the operation.</summary>
    public InstallationState Current { get; private set; }

    /// <summary>True when the operation reached a terminal state.</summary>
    public bool IsTerminal => IsTerminalState(Current);

    /// <summary>Tells whether the transition is declared in the transition table.</summary>
    /// <param name="nextState">Requested state.</param>
    public bool CanTransitionTo(InstallationState nextState) => AllowedTransitions[Current].Contains(nextState);

    /// <summary>
    /// Attempts the transition. When the transition is not declared the state is not changed
    /// and the method returns false.
    /// </summary>
    /// <param name="nextState">Requested state.</param>
    public bool TryTransitionTo(InstallationState nextState)
    {
        if (!CanTransitionTo(nextState))
        {
            return false;
        }

        Current = nextState;
        return true;
    }

    /// <summary>
    /// Performs the transition. An invalid transition is rejected with a domain error and
    /// the state of the operation does not change.
    /// </summary>
    /// <param name="nextState">Requested state.</param>
    public void TransitionTo(InstallationState nextState)
    {
        if (!TryTransitionTo(nextState))
        {
            throw new InvalidStateTransitionException(nameof(InstallationStateMachine), Current.ToString(), nextState.ToString());
        }
    }

    /// <summary>Tells whether a state is terminal for an installation operation.</summary>
    /// <param name="state">State to inspect.</param>
    public static bool IsTerminalState(InstallationState state) => AllowedTransitions[state].Count == 0;
}
