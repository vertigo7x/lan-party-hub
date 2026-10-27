namespace LANPartyHub.Shared.Contracts;

/// <summary>
/// Connection state of a client (AD-27). It has three values, not two: a client whose
/// trust anchor is not verified is connected but cannot download or install anything.
/// </summary>
public enum ConnectionState
{
    /// <summary>The trust anchor was fixed out of band and the fingerprint matched.</summary>
    AnchorVerified,

    /// <summary>The client is connected but the trust anchor is not verified yet: it cannot download or install.</summary>
    AnchorUnverified,

    /// <summary>The client is not connected to the server.</summary>
    Disconnected,
}
