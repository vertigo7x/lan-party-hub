using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Client.Domain.StateMachines;

/// <summary>
/// State machine of a download operation (AD-10).
///
/// The client is the only executor of a download operation, so the transition table is
/// declared here, once. The server only projects the resulting state (AD-10, AD-22).
/// Every transition that is not in the table is rejected and leaves the state unchanged.
/// </summary>
public sealed class DownloadStateMachine
{
    /// <summary>State every download operation starts in.</summary>
    public const DownloadState InitialState = DownloadState.Queued;

    private static readonly IReadOnlyDictionary<DownloadState, IReadOnlyList<DownloadState>> AllowedTransitions =
        new Dictionary<DownloadState, IReadOnlyList<DownloadState>>
        {
            [DownloadState.Queued] = [DownloadState.Downloading],
            [DownloadState.Downloading] = [DownloadState.Paused, DownloadState.Validating, DownloadState.Failed],
            [DownloadState.Paused] = [DownloadState.Downloading, DownloadState.Cancelled],
            [DownloadState.Validating] = [DownloadState.Completed, DownloadState.Failed],
            [DownloadState.Completed] = [],
            [DownloadState.Cancelled] = [],
            [DownloadState.Failed] = [],
        };

    /// <summary>Creates the state machine in the given state.</summary>
    /// <param name="initialState">State the operation starts in.</param>
    public DownloadStateMachine(DownloadState initialState = InitialState) => Current = initialState;

    /// <summary>Current state of the operation.</summary>
    public DownloadState Current { get; private set; }

    /// <summary>True when the operation reached a terminal state.</summary>
    public bool IsTerminal => IsTerminalState(Current);

    /// <summary>Tells whether the transition is declared in the transition table.</summary>
    /// <param name="nextState">Requested state.</param>
    public bool CanTransitionTo(DownloadState nextState) => AllowedTransitions[Current].Contains(nextState);

    /// <summary>
    /// Attempts the transition. When the transition is not declared the state is not changed
    /// and the method returns false.
    /// </summary>
    /// <param name="nextState">Requested state.</param>
    public bool TryTransitionTo(DownloadState nextState)
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
    public void TransitionTo(DownloadState nextState)
    {
        if (!TryTransitionTo(nextState))
        {
            throw new InvalidStateTransitionException(nameof(DownloadStateMachine), Current.ToString(), nextState.ToString());
        }
    }

    /// <summary>Tells whether a state is terminal for a download operation.</summary>
    /// <param name="state">State to inspect.</param>
    public static bool IsTerminalState(DownloadState state) => AllowedTransitions[state].Count == 0;
}
