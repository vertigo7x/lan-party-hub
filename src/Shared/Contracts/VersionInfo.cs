namespace LANPartyHub.Shared.Contracts;

/// <summary>
/// Versions of the system (AD-18, AD-6). It is the origin of the version headers exchanged
/// by the client and the server.
/// </summary>
/// <param name="ClientVersion">Version of the client binary.</param>
/// <param name="ServerVersion">Version of the server binary.</param>
/// <param name="ProtocolVersion">Protocol version spoken by both sides.</param>
public sealed record VersionInfo(string ClientVersion, string ServerVersion, int ProtocolVersion)
{
    /// <summary>
    /// The protocol version that the current build speaks (AD-6).
    ///
    /// <para>
    /// It is the SINGLE origin of the version of the protocol of the system: the client
    /// declares it in every request and the server compares the declared version against
    /// the range that it supports. It is a constant of this contract and NOT a type, so
    /// the inventory of the contracts of <c>Shared</c> does not change (AD-18, AD-33).
    /// </para>
    /// </summary>
    public const int CurrentProtocolVersion = 1;
}
