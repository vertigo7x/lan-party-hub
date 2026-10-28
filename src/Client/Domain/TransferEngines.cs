namespace LANPartyHub.Client.Domain;

/// <summary>
/// Transfer engines of the client (AD-24, AD-28).
///
/// <para>
/// The MVP has exactly one engine: the HTTP path with Range requests (AD-4, AD-20). The value
/// that a persisted operation declares names the family of the engine and not the class that
/// implements it, so a refactor of the type does not invalidate the data that is already
/// stored.
/// </para>
/// <para>
/// One engine, one format: the format of the persisted progress is deduced from the engine of
/// the operation and never from a configurable parameter. A record whose engine does not
/// exist in the active version is rejected instead of being reinterpreted.
/// </para>
/// </summary>
public static class TransferEngines
{
    /// <summary>The only engine of the MVP: HTTP with Range requests.</summary>
    public const string HttpRange = "HttpRange";

    /// <summary>
    /// Tells whether an engine exists in the active version of the client.
    /// </summary>
    /// <param name="transferEngine">Engine declared by a persisted operation.</param>
    /// <returns><c>true</c> when the engine is the active one.</returns>
    public static bool IsActive(string? transferEngine) =>
        string.Equals(transferEngine, HttpRange, StringComparison.Ordinal);
}
