namespace LANPartyHub.Client.Infrastructure.Persistence.Entities;

/// <summary>
/// Row of the <c>CLIENT_ERROR_REGISTER</c> table (AD-11, AD-15, AD-22, AD-23, NFR-7).
///
/// <para>
/// The row is the DURABLE record of an error that the client registered: it carries the
/// stable code of the catalog, the message, the cause and the next step, so the visor of
/// diagnostics can present the four parts even after the client restarts (AD-15, NFR-7).
/// </para>
/// <para>
/// The row carries ALSO the <c>clientId</c> and the <c>operationId</c>, so the error of
/// the client can be JOINED with the log of the server: the two logs share those two
/// identifiers (AD-23, AR-20).
/// </para>
/// <para>
/// The <c>operationType</c> names the KIND of operation that produced the error —a
/// download, an installation or a launch—, so the diagnostic is ASSOCIATED with the
/// operation that caused it (AC2). It is the stable name of the value and never a free
/// text (AD-25).
/// </para>
/// <para>
/// The table belongs to the client and it never replicates a table of the server: the
/// server owns its own register of errors —la tabla <c>CLIENT_ERROR</c> del servidor— y
/// el cliente tiene la SUYA, así que las dos bases de datos no replican ninguna tabla la
/// una de la otra (AD-22).
/// </para>
/// </summary>
public sealed class ClientErrorEntity
{
    /// <summary>
    /// Key of the row: the identifier that the client DERIVES from the error, so the same
    /// error registered twice produces the same row and the write is idempotent (AD-22).
    /// </summary>
    public string ErrorId { get; set; } = string.Empty;

    /// <summary>Canonical identifier of the team that registered the error (AD-23).</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Identifier of the unit of work that produced the error, empty when the error is not
    /// bound to one (AD-23).
    /// </summary>
    public string OperationId { get; set; } = string.Empty;

    /// <summary>
    /// Kind of operation that produced the error: a download, an installation or a launch
    /// (AC2).
    /// </summary>
    public string OperationType { get; set; } = string.Empty;

    /// <summary>Stable code of the error of the catalog (AD-15).</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>What failed (AD-15).</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Why it failed (AD-15).</summary>
    public string Cause { get; set; } = string.Empty;

    /// <summary>What to do now (AD-15).</summary>
    public string NextStep { get; set; } = string.Empty;

    /// <summary>Instant of the error, in UTC ISO-8601.</summary>
    public string OccurredAt { get; set; } = string.Empty;
}
