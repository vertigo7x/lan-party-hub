namespace LANPartyHub.Client.Domain.StateMachines;

/// <summary>
/// Raised when a state machine is asked to perform a transition that is not declared in its
/// transition table. The state of the operation is not changed when this exception is raised.
/// </summary>
public sealed class InvalidStateTransitionException : InvalidOperationException
{
    /// <summary>Creates the exception for a rejected transition.</summary>
    /// <param name="stateMachineName">Name of the state machine that rejected the transition.</param>
    /// <param name="currentState">State the operation was in.</param>
    /// <param name="nextState">State that was requested.</param>
    public InvalidStateTransitionException(string stateMachineName, string currentState, string nextState)
        : base($"Invalid transition in '{stateMachineName}': {currentState} -> {nextState}.")
    {
        StateMachineName = stateMachineName;
        CurrentState = currentState;
        NextState = nextState;
    }

    /// <summary>Name of the state machine that rejected the transition.</summary>
    public string StateMachineName { get; }

    /// <summary>State the operation was in.</summary>
    public string CurrentState { get; }

    /// <summary>State that was requested.</summary>
    public string NextState { get; }
}
