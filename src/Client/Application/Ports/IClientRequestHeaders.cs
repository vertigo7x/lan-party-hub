namespace LANPartyHub.Client.Application.Ports;

/// <summary>
/// Headers of correlation that every request to the surface of the client of the event
/// server carries (AD-15, AD-23).
///
/// <para>
/// The port is declared in the Application layer and it is implemented by the correlation
/// scope of the client. The adapter that sends the requests reads the identifiers from it
/// and stamps them on every request, so the client never builds a request without them.
/// </para>
/// <para>
/// The identifier of the client is the key of correlation of the logs and it is never a
/// credential: it authenticates nothing (AD-30).
/// </para>
/// </summary>
public interface IClientRequestHeaders
{
    /// <summary>
    /// Canonical identifier of the team, or <c>null</c> when the client does not know it
    /// yet.
    /// </summary>
    string? ClientId { get; }

    /// <summary>
    /// Identifier of the unit of work in progress, or <c>null</c> when the client is not
    /// running one.
    /// </summary>
    string? OperationId { get; }
}
