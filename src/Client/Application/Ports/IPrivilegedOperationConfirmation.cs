using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Client.Application.Ports;

/// <summary>
/// Port of the CONFIRMATION of a privileged operation of the installation manifest
/// (AD-7, AC2, AC3).
///
/// <para>
/// El port existe para que el caso de uso de la instalación pueda EXIGIR la confirmación
/// sin conocer la interfaz: el caso de uso decide CUÁNDO se pregunta y el adaptador
/// decide CÓMO se pregunta (AD-1, AD-16).
/// </para>
/// <para>
/// La confirmación vive SÓLO EN MEMORIA: el puerto la pide en el MOMENTO y no la
/// persiste como concedida, así que al reanudar una operación tras un reinicio del
/// cliente se vuelve a pedir (AC3).
/// </para>
/// </summary>
public interface IPrivilegedOperationConfirmation
{
    /// <summary>
    /// Asks the player for the EXPLICIT confirmation of a privileged operation (AC2).
    ///
    /// <para>
    /// Cancelar NO es una operación destructiva: la operación no se ejecuta y el
    /// instalador queda en un estado terminal con su error registrado (AC2, AC5).
    /// </para>
    /// </summary>
    /// <param name="operation">
    /// Operation of the manifest that is waiting for the confirmation, as the
    /// discriminator del contrato escribe su nombre.
    /// </param>
    /// <param name="detail">
    /// Description of what the operation writes, so the dialog NAMES what the player is
    /// approving.
    /// </param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>
    /// <c>true</c> when the player confirmed, and <c>false</c> when the player declined
    /// or dismissed the confirmation (AC2).
    /// </returns>
    Task<bool> ConfirmAsync(
        string operation,
        string detail,
        CancellationToken cancellationToken);
}

/// <summary>
/// The confirmation of a privileged operation was DECLINED by the player (AD-7, AC2).
///
/// <para>
/// El decline es un estado TERMINAL de la operación: la instalación queda en
/// <c>Failed</c> con su error registrado, y «Reintentar» es una operación NUEVA sobre el
/// paquete validado (AC2, AC5).
/// </para>
/// </summary>
public sealed class PrivilegedOperationDeclinedException : Exception
{
    /// <summary>Creates the exception of a declined confirmation.</summary>
    /// <param name="operation">Operation of the manifest that was declined.</param>
    public PrivilegedOperationDeclinedException(string operation)
        : base($"El jugador canceló la confirmación de la operación privilegiada '{operation}'.")
    {
        Operation = operation;
    }

    /// <summary>Operation of the manifest that was declined.</summary>
    public string Operation { get; }
}

/// <summary>
/// The confirmation of a privileged operation could NOT be obtained, because the player
/// dismissed the confirmation or because the client could not ask for it (AD-7, AC2).
///
/// <para>
/// El fallo de la PREGUNTA es un fallo de la operación, y no un silencio: la instalación
/// queda en <c>Failed</c> con su error registrado (AC2, AC5).
/// </para>
/// </summary>
public sealed class PrivilegedOperationConfirmationFailedException : Exception
{
    /// <summary>Creates the exception of a confirmation that could not be obtained.</summary>
    /// <param name="operation">Operation of the manifest that was waiting for the confirmation.</param>
    /// <param name="innerException">Failure that prevented the confirmation.</param>
    public PrivilegedOperationConfirmationFailedException(
        string operation,
        Exception? innerException = null)
        : base($"No se pudo pedir la confirmación de la operación privilegiada '{operation}'.", innerException)
    {
        Operation = operation;
    }

    /// <summary>Operation of the manifest that was waiting for the confirmation.</summary>
    public string Operation { get; }
}

/// <summary>
/// The error of the catalog of a declined confirmation of a privileged operation
/// (AD-7, AC2, AC5).
/// </summary>
public static class PrivilegedOperationErrors
{
    /// <summary>
    /// The error that a declined confirmation leaves, taken from the single catalog of the
    /// system (AD-15, AD-25).
    /// </summary>
    public static ApiError Declined => ErrorCatalog.Resolve(
        ErrorCatalog.InstallPrivilegedConfirmationDeclinedCode);
}
