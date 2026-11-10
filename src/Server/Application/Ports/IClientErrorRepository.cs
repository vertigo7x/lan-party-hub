namespace LANPartyHub.Server.Application.Ports;

/// <summary>
/// Port of the registry of the ERRORS that the clients reported (AD-15, AD-22).
///
/// <para>
/// El registro de errores es del SERVIDOR y existe para que la vista de organización
/// muestre lo que ya se sabe de cada equipo. Esta historia los MUESTRA: la que los
/// PRODUCE es otra (AD-15, AD-22).
/// </para>
/// <para>
/// El puerto declara SOLO la lectura: el resultado terminal de una operación retirada
/// del catálogo NO se registra como error, así que esta historia no escribe aquí
/// (AD-33, R-MVP-8).
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
