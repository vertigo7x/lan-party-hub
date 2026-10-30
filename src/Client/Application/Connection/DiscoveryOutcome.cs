using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Client.Application.Connection;

/// <summary>
/// Outcome of a search of the event server (AC1, AC3, AC4, AD-3).
///
/// <para>
/// The outcome is NOT a state of the connection machine: the result of the discovery says
/// whether the search found a server, found nothing, or was blocked by the policy of the
/// network. The connection states belong to the contract of the connection and they are
/// reached later, when the client talks to the server (AD-27).
/// </para>
/// </summary>
public enum DiscoveryOutcome
{
    /// <summary>The search found at least one server (AC1).</summary>
    Found,

    /// <summary>
    /// The search finished without finding any server (AC3).
    /// </summary>
    Empty,

    /// <summary>
    /// The policy of the network blocked the discovery, so the search could not finish
    /// (AC4).
    /// </summary>
    Blocked,
}

/// <summary>
/// Result of a search of the event server (AC1, AC3, AC4, AD-3).
/// </summary>
/// <param name="Outcome">Outcome of the search.</param>
/// <param name="Servers">
/// Servers that answered the search. It is empty when the outcome is not
/// <see cref="DiscoveryOutcome.Found"/>.
/// </param>
public sealed record DiscoveryResult(
    DiscoveryOutcome Outcome,
    IReadOnlyList<ServerEndpoint> Servers)
{
    /// <summary>
    /// Builds the result of a search that found servers (AC1).
    /// </summary>
    /// <param name="servers">Servers that answered the search.</param>
    /// <returns>The result of the search.</returns>
    public static DiscoveryResult Found(IReadOnlyList<ServerEndpoint> servers) =>
        new(DiscoveryOutcome.Found, servers ?? []);

    /// <summary>
    /// Builds the result of a search that found nothing (AC3).
    /// </summary>
    /// <returns>The result of the search.</returns>
    public static DiscoveryResult Empty() => new(DiscoveryOutcome.Empty, []);

    /// <summary>
    /// Builds the result of a search that the network blocked (AC4).
    /// </summary>
    /// <returns>The result of the search.</returns>
    public static DiscoveryResult Blocked() => new(DiscoveryOutcome.Blocked, []);
}
