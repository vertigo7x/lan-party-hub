namespace LANPartyHub.Client.Application.Ports;

/// <summary>
/// Closed registry of the transfer engines of the client (AD-4, AD-24, AD-28).
///
/// <para>
/// The MVP has EXACTLY one engine: <c>HttpRangeTransferEngine</c>. The value that a
/// persisted operation declares in its column <c>transferEngine</c> is exactly the
/// identifier of the unique implementation of the port, and it is a CLOSED value: the
/// client admits no other one, it does not deduce it from a configurable parameter and it
/// does not reinterpret an unknown value.
/// </para>
/// <para>
/// The registry lives in the Application layer and not in the Domain layer, because the
/// Domain layer never names a transport mechanism: the domain knows that the bytes come
/// through the port and nothing more (AD-4).
/// </para>
/// </summary>
public static class TransferEngineRegistry
{
    /// <summary>
    /// The only engine of the MVP: HTTP with <c>Range</c> requests. It is the exact value
    /// that the column <c>transferEngine</c> of <c>DOWNLOAD_OPERATION</c> holds (AC4).
    /// </summary>
    public const string HttpRangeTransferEngine = "HttpRangeTransferEngine";

    /// <summary>
    /// Every engine that exists in the active version of the client.
    /// </summary>
    public static IReadOnlyList<string> Known { get; } = Array.AsReadOnly(
        [HttpRangeTransferEngine]);

    /// <summary>
    /// Tells whether an engine exists in the active version of the client.
    /// </summary>
    /// <param name="transferEngine">Engine declared by a persisted operation.</param>
    /// <returns><c>true</c> when the engine is the active one.</returns>
    public static bool IsActive(string? transferEngine) =>
        string.Equals(transferEngine, HttpRangeTransferEngine, StringComparison.Ordinal);
}
