namespace LANPartyHub.Server.Infrastructure.Persistence.Entities;

/// <summary>
/// Row of the <c>CLIENT_ERROR</c> table (AD-22, AD-15).
///
/// The registered error keeps the shape of the single error type of the system: the stable
/// code of the catalog, the message, the cause and the next step.
/// </summary>
public sealed class ClientErrorEntity
{
    /// <summary>Opaque identifier of the registered error.</summary>
    public string ErrorId { get; set; } = string.Empty;

    /// <summary>Opaque UUID v7 identifier of the client that reported the error.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Opaque identifier of the operation the error belongs to.</summary>
    public string OperationId { get; set; } = string.Empty;

    /// <summary>Stable code of the error catalog.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>What failed.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Why it failed.</summary>
    public string Cause { get; set; } = string.Empty;

    /// <summary>What to do now.</summary>
    public string NextStep { get; set; } = string.Empty;

    /// <summary>Instant of the error, in UTC ISO-8601.</summary>
    public string OccurredAt { get; set; } = string.Empty;
}
