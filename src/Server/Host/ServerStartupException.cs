namespace LANPartyHub.Server.Host;

/// <summary>
/// Raised when the server cannot start: an invalid command line, an invalid configuration
/// value or a host without a private address to listen on. The message is the one the
/// organizer reads on the console, so it states what failed and what to do now.
/// </summary>
public sealed class ServerStartupException : Exception
{
    /// <summary>Creates the exception with the message shown to the organizer.</summary>
    /// <param name="message">Message that explains what failed.</param>
    public ServerStartupException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with the message shown to the organizer and its cause.</summary>
    /// <param name="message">Message that explains what failed.</param>
    /// <param name="innerException">Cause of the failure.</param>
    public ServerStartupException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
