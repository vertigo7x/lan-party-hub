namespace LANPartyHub.Client.Application.Install;

/// <summary>
/// The phase of an installation operation (AD-10, AD-22, AC4).
///
/// <para>
/// La fase es MICROCOPY de la operación y NO es un estado de la máquina: el estado sigue
/// siendo el que declara <see cref="Shared.Contracts.InstallationState"/>, y la fase
/// nombra EN QUÉ PASO va la operación (AD-10, AD-22).
/// </para>
/// </summary>
public enum InstallPhase
{
    /// <summary>The operation has not started yet.</summary>
    Idle,

    /// <summary>The package is being extracted into the authorized directory (AC1).</summary>
    Extracting,

    /// <summary>The typed operations of the manifest are being applied (AC4).</summary>
    ApplyingManifest,

    /// <summary>The operation is waiting for the confirmation of a privileged operation (AC2).</summary>
    AwaitingConfirmation,

    /// <summary>The installed tree is being verified (AC4).</summary>
    Validating,

    /// <summary>The operation finished and the tree is valid (AC4).</summary>
    Completed,

    /// <summary>The operation failed and its error is registered (AC5).</summary>
    Failed,
}

/// <summary>
/// Progress of an installation operation, and the SINGLE source of the phase and of the
/// percentage that the interface shows (AD-10, AD-22, AC4, UX-DR-13, UX-DR-34).
///
/// <para>
/// El porcentaje es un ENTERO derivado de los BYTES REALES escritos sobre los bytes
/// totales de la operación, con SUELO en cero y techo en cien, igual que el porcentaje de
/// la descarga: una barra que dibuja el 43 % y un texto que dice 42 % serían una
/// contradicción (UX-DR-13).
/// </para>
/// <para>
/// La fase nombra el paso («Extrayendo», «Aplicando manifiesto») y el texto la compone
/// la capa de presentación, así que la vista no escribe la fase por su cuenta (AD-1,
/// AD-25).
/// </para>
/// </summary>
public sealed class InstallProgress
{
    /// <summary>
    /// Creates the progress of an installation operation.
    /// </summary>
    /// <param name="phase">Phase the operation is in.</param>
    /// <param name="bytesWritten">Bytes that have really been written so far.</param>
    /// <param name="totalBytes">
    /// Total bytes that the operation expects to write, or <c>null</c> when they are not
    /// known yet. While the total is unknown there is NO percentage: un porcentaje sobre
    /// un total que no se conoce sería una mentira (UX-DR-13).
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The written bytes or the total bytes are negative.
    /// </exception>
    public InstallProgress(InstallPhase phase, long bytesWritten, long? totalBytes)
    {
        if (bytesWritten < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bytesWritten),
                "Los bytes escritos no pueden ser negativos.");
        }

        if (totalBytes is { } total && total < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(totalBytes),
                "El total de bytes no puede ser negativo.");
        }

        Phase = phase;
        BytesWritten = bytesWritten;
        TotalBytes = totalBytes;
    }

    /// <summary>Creates the progress of an operation that has not started yet.</summary>
    public static InstallProgress Idle => new(InstallPhase.Idle, 0, null);

    /// <summary>Phase the operation is in (AD-10, AC4).</summary>
    public InstallPhase Phase { get; }

    /// <summary>Bytes that have really been written so far (AC4).</summary>
    public long BytesWritten { get; }

    /// <summary>
    /// Total bytes that the operation expects to write, or <c>null</c> when they are not
    /// known yet (AC4).
    /// </summary>
    public long? TotalBytes { get; }

    /// <summary>
    /// The whole percentage of the installation, as an integer between zero and a hundred
    /// (AC4, UX-DR-13).
    ///
    /// <para>
    /// El porcentaje es NUNCA un decimal: sale de los bytes REALES escritos, con el
    /// SUELO en cero y el TECHO en cien, y la barra dibuja la MISMA fracción que este
    /// valor declara (UX-DR-13).
    /// </para>
    /// </summary>
    public int Percentage => TotalBytes is { } total && total > 0
        ? (int)Math.Clamp(BytesWritten * 100 / total, 0, 100)
        : 0;

    /// <summary>
    /// The fraction that the bar draws, between zero and one (AC4, UX-DR-13).
    /// </summary>
    public double Fraction => Percentage / 100d;

    /// <summary>
    /// Tells whether the operation FAILED, así que la barra conserva su longitud REAL y la
    /// interfaz ofrece el reintento (AC5, UX-DR-13, UX-DR-34).
    /// </summary>
    public bool IsFailed => Phase == InstallPhase.Failed;

    /// <summary>
    /// Tells whether the operation FINISHED, así que la barra conserva su longitud REAL
    /// (AC4, UX-DR-13).
    /// </summary>
    public bool IsCompleted => Phase == InstallPhase.Completed;
}
