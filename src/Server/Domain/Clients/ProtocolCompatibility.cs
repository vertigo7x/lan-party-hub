namespace LANPartyHub.Server.Domain.Clients;

/// <summary>
/// Rule of compatibility of the versions of the clients (AD-6, FR-4).
///
/// <para>
/// The matrix of compatibility lives ONLY in the server: the client declares the version
/// of the protocol that it speaks and the server decides whether that version is inside
/// the range that it supports. The two units can NEVER negotiate differently, because
/// only one of them holds the rule (AD-6).
/// </para>
/// <para>
/// The range is <c>[mínimo, actual]</c>: a client whose protocol version is BELOW the
/// minimum that the server supports is too old, and a client whose protocol version is
/// ABOVE the version that the server speaks is too new. Both are incompatible, and the
/// two rejections are the same one: the client has to be updated (AD-6, AD-17).
/// </para>
/// <para>
/// The rule is pure: it depends on the versions that it receives and never on the
/// transport, so it can be exercised without a server and without a network (AD-1).
/// </para>
/// </summary>
public static class ProtocolCompatibility
{
    /// <summary>
    /// Tells whether the protocol version that the client declares is inside the range
    /// that the server supports.
    /// </summary>
    /// <param name="clientProtocolVersion">Protocol version that the client declares.</param>
    /// <param name="minimumProtocolVersion">Oldest protocol version that the server supports.</param>
    /// <param name="currentProtocolVersion">Protocol version that the server speaks.</param>
    /// <returns><c>true</c> when the client is compatible with the server.</returns>
    public static bool IsCompatible(
        int clientProtocolVersion,
        int minimumProtocolVersion,
        int currentProtocolVersion) =>
        clientProtocolVersion >= minimumProtocolVersion
        && clientProtocolVersion <= currentProtocolVersion;
}
