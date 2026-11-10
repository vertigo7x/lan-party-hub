namespace LANPartyHub.Shared.Contracts;

/// <summary>
/// Terminal result of an operation, reported by the client to the server (AD-18, AD-22,
/// AD-33).
///
/// <para>
/// El resultado viaja de cliente a servidor en el ÚNICO canal de push del sistema, y es
/// la forma EXACTA del resultado de una operación terminada: el valor de la enumeración
/// cerrada <see cref="OperationOutcome"/> y NADA más (AD-18, AD-33).
/// </para>
/// <para>
/// El valor <see cref="OperationOutcome.PackageWithdrawn"/> es un RESULTADO distinto del
/// fallo: no produce <see cref="ApiError"/>, no se persiste como <c>ClientError</c> y no
/// se registra en el <see cref="ErrorCatalog"/> (AD-33, R-MVP-8).
/// </para>
/// </summary>
/// <param name="ClientId">Opaque UUID v7 identifier of the client that reports the result.</param>
/// <param name="OperationId">Opaque UUID v7 identifier of the operation that finished.</param>
/// <param name="Outcome">Result of the finished operation.</param>
public sealed record OperationOutcomeReported(
    string ClientId,
    string OperationId,
    OperationOutcome Outcome);
