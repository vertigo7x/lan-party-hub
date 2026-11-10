using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Client.Application.Ports;

/// <summary>
/// Port of the publication of the progress of an INSTALLATION operation (AD-18, AD-22).
///
/// <para>
/// El port es declarado en la capa de Application y lo implementa el adaptador que
/// publica en el hub del estado del cliente. El coordinador de las instalaciones
/// depende del port y nunca del adaptador, así que la forma del evento se puede
/// ejercitar sin una red (AD-1).
/// </para>
/// <para>
/// El payload es el CONTRATO EXACTO <see cref="InstallationProgressUpdated"/>: el
/// identificador del cliente, el de la operación, el estado de la máquina y la FASE
/// REAL. El payload NO admite porcentaje: un porcentaje sobre un total que no se conoce
/// sería una mentira, así que la fase se publica tal cual (AD-22).
/// </para>
/// <para>
/// La publicación NUNCA es una condición para que la instalación siga: un servidor que
/// no recibe el progreso no puede impedir que el manifiesto se aplique (AD-1, AD-22).
/// </para>
/// </summary>
public interface IInstallationProgressReporter
{
    /// <summary>
    /// Publishes the REAL phase of an installation operation (AD-22, FR-10).
    /// </summary>
    /// <param name="progress">Progress of the operation, with the exact shape of the contract.</param>
    /// <param name="cancellationToken">Token that cancels the publication.</param>
    /// <returns>A task that completes when the progress has been published.</returns>
    Task ReportAsync(InstallationProgressUpdated progress, CancellationToken cancellationToken);
}

/// <summary>
/// Port of the publication of the TERMINAL RESULT of an operation (AD-18, AD-33).
///
/// <para>
/// El resultado terminal viaja de cliente a servidor en el payload
/// <see cref="OperationOutcomeReported"/>, que es la forma exacta del resultado de una
/// operación terminada (AD-18, AD-22).
/// </para>
/// <para>
/// El resultado es un RESULTADO y NUNCA un error: el valor
/// <see cref="OperationOutcome.PackageWithdrawn"/> no produce
/// <see cref="ApiError"/>, no se persiste como error del cliente y no se registra en el
/// catálogo de errores (AD-33, R-MVP-8).
/// </para>
/// </summary>
public interface IOperationOutcomeReporter
{
    /// <summary>
    /// Publishes the terminal result of an operation (AD-33).
    /// </summary>
    /// <param name="reported">Result of the finished operation, with the exact shape of the contract.</param>
    /// <param name="cancellationToken">Token that cancels the publication.</param>
    /// <returns>A task that completes when the result has been published.</returns>
    Task ReportAsync(OperationOutcomeReported reported, CancellationToken cancellationToken);
}
