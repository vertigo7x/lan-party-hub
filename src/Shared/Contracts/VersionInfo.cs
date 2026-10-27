namespace LANPartyHub.Shared.Contracts;

/// <summary>
/// Versions of the system (AD-18, AD-6). It is the origin of the version headers exchanged
/// by the client and the server.
/// </summary>
/// <param name="ClientVersion">Version of the client binary.</param>
/// <param name="ServerVersion">Version of the server binary.</param>
/// <param name="ProtocolVersion">Protocol version spoken by both sides.</param>
public sealed record VersionInfo(string ClientVersion, string ServerVersion, int ProtocolVersion);
