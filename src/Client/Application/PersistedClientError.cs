namespace LANPartyHub.Client.Application;

/// <summary>
/// An error of the client as it is persisted in the database of the client
/// (AD-11, AD-15, AD-22, AD-23, NFR-7).
///
/// <para>
/// The record carries the FOUR parts of the diagnostic —the stable code, the message, the
/// cause and the next step—, so the visor of diagnostics can present them even after the
/// client restarts (AD-15, NFR-7).
/// </para>
/// <para>
/// It carries ALSO the <c>clientId</c> and the <c>operationId</c> that CORRELATE the
/// error of the client with the log of the server: the two logs share those two
/// identifiers (AD-23, AR-20).
/// </para>
/// <para>
/// The <c>operationType</c> names the KIND of operation that produced the error —a
/// download, an installation or a launch—, así que el diagnóstico queda ASOCIADO a la
/// operación que lo produjo (AC2).
/// </para>
/// </summary>
/// <param name="ErrorId">Identifier that the client derives from the error.</param>
/// <param name="ClientId">Canonical identifier of the team that registered the error.</param>
/// <param name="OperationId">
/// Identifier of the unit of work that produced the error, empty when the error is not
/// bound to one.
/// </param>
/// <param name="OperationType">Kind of operation that produced the error.</param>
/// <param name="Code">Stable code of the error of the catalog.</param>
/// <param name="Message">What failed.</param>
/// <param name="Cause">Why it failed.</param>
/// <param name="NextStep">What to do now.</param>
/// <param name="OccurredAt">Instant of the error, in UTC.</param>
public sealed record PersistedClientError(
    string ErrorId,
    string ClientId,
    string OperationId,
    string OperationType,
    string Code,
    string Message,
    string Cause,
    string NextStep,
    DateTimeOffset OccurredAt);
