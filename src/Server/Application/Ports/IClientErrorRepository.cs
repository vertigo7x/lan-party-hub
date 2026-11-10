namespace LANPartyHub.Server.Application.Ports;

/// <summary>
/// Port of the registry of the ERRORS that the clients reported (AD-15, AD-22).
///
/// <para>
/// El registro de errores es del SERVIDOR y existe para que la vista de organización
/// muestre lo que ya se sabe de cada equipo: esta historia lo PERSISTE cuando el cliente
/// reporta un error, y lo MUESTRA en la fila del equipo (AD-15, AD-22, FR-10).
/// </para>
/// <para>
/// El error que se persiste es SIEMPRE de la única forma de error del sistema:
/// <c>ApiError</c>, con el código estable del catálogo, el mensaje, la causa y el
/// siguiente paso (AD-15, AR-4).
/// </para>
/// <para>
/// El resultado terminal de una operación retirada del catálogo NO se registra como
/// error: es un RESULTADO y no un fallo (AD-33, R-MVP-8).
/// </para>
/// </summary>
public interface IClientErrorRepository
{
    /// <summary>
    /// Reads the errors that are registered for a client, ordered by instant.
    /// </summary>
    /// <param name="clientId">Canonical identifier of the client.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The errors that are registered for the client.</returns>
    Task<IReadOnlyList<ClientErrorRow>> ListByClientAsync(
        string clientId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Registers an error that a client reported, con TODAS sus partes: el
    /// identificador del error, el del cliente, el de la operación, el código estable
    /// del catálogo, el mensaje, la causa, el siguiente paso y el instante (AD-15).
    /// </summary>
    /// <remarks>
    /// La escritura es IDEMPOTENTE por <c>errorId</c>: el mismo error reportado dos
    /// veces NO crea una segunda fila, así que una republicación del cliente no duplica
    /// el diagnóstico (AD-15, AD-22).
    /// </remarks>
    /// <param name="error">Error that the client reported.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the error has been registered.</returns>
    Task SaveAsync(ClientErrorWrite error, CancellationToken cancellationToken);
}

/// <summary>
/// An error that is registered for a client, as the use case sees it (AD-15, AD-22).
///
/// <para>
/// El error conserva la forma de la única forma de error del sistema: el código
/// estable del catálogo, el mensaje, la causa y el siguiente paso (AD-15).
/// </para>
/// </summary>
/// <param name="ErrorId">Opaque identifier of the registered error.</param>
/// <param name="OperationId">
/// Opaque identifier of the operation the error belongs to, empty when the error is not
/// bound to one.
/// </param>
/// <param name="Code">Stable code of the error of the catalog.</param>
/// <param name="Message">What failed.</param>
/// <param name="Cause">Why it failed.</param>
/// <param name="NextStep">What to do now.</param>
/// <param name="OccurredAt">Instant of the error, in UTC.</param>
public sealed record ClientErrorRow(
    string ErrorId,
    string OperationId,
    string Code,
    string Message,
    string Cause,
    string NextStep,
    DateTimeOffset OccurredAt);

/// <summary>
/// An error that a client reported, ready to be REGISTERED (AD-15, AD-22).
///
/// <para>
/// La escritura lleva el <c>clientId</c> que la vista de organización necesita para
/// asociar el error a la fila del equipo, y el <c>errorId</c> que hace la escritura
/// idempotente (AD-15, AD-22).
/// </para>
/// </summary>
/// <param name="ErrorId">Opaque identifier of the registered error.</param>
/// <param name="ClientId">Canonical identifier of the client that reported the error.</param>
/// <param name="OperationId">
/// Opaque identifier of the operation the error belongs to, empty when the error is not
/// bound to one.
/// </param>
/// <param name="Code">Stable code of the error of the catalog.</param>
/// <param name="Message">What failed.</param>
/// <param name="Cause">Why it failed.</param>
/// <param name="NextStep">What to do now.</param>
/// <param name="OccurredAt">Instant of the error, in UTC.</param>
public sealed record ClientErrorWrite(
    string ErrorId,
    string ClientId,
    string OperationId,
    string Code,
    string Message,
    string Cause,
    string NextStep,
    DateTimeOffset OccurredAt);
