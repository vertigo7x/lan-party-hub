namespace LANPartyHub.Client.Host;

/// <summary>
/// Failure of the startup of the client that the player can act on (AD-15).
///
/// <para>
/// The startup of the client is a one shot operation that happens before the window appears,
/// so a failure is reported with a single actionable line and never with a stack trace.
/// </para>
/// </summary>
public sealed class ClientStartupException : Exception
{
    /// <summary>Creates the failure with its message.</summary>
    /// <param name="message">Message that explains what failed and what to do now.</param>
    public ClientStartupException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the failure with its message and the failure that caused it.</summary>
    /// <param name="message">Message that explains what failed and what to do now.</param>
    /// <param name="innerException">Failure that caused this one.</param>
    public ClientStartupException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
