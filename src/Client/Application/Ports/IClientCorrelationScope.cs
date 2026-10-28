namespace LANPartyHub.Client.Application.Ports;

/// <summary>
/// Correlation scope of the client (AD-15, AD-23).
///
/// <para>
/// The port is declared in the Application layer and it is implemented by the adapter that
/// owns the log of the client. The scope carries the technical identity of the team and
/// the identifier of the unit of work that is running, so every entry of the log can be
/// tied to the client and to the operation that produced it.
/// </para>
/// <para>
/// The scope lives as long as the session: the composition root owns it and closes it
/// when the client ends, so the entries that the client writes after the startup keep
/// carrying the identifiers.
/// </para>
/// </summary>
public interface IClientCorrelationScope : IClientRequestHeaders, IDisposable
{
    /// <summary>
    /// Opens the scope of the client with its identity and the unit of work in progress.
    /// The scope that was open is closed, so there is never more than one.
    /// </summary>
    /// <param name="clientId">Canonical identifier of the team (AD-23).</param>
    /// <param name="operationId">
    /// Identifier of the unit of work that is running, or <c>null</c> when the client is
    /// not running one.
    /// </param>
    void Open(string clientId, string? operationId = null);
}
