using LANPartyHub.Client.Application.Install;
using LANPartyHub.Client.Presentation.Errors;
using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Client.Presentation.Install;

/// <summary>
/// View model of the progress of an installation operation (AC4, AC5, UX-DR-13, UX-DR-34).
///
/// <para>
/// La barra y el texto salen de UNA SOLA fuente, así que la barra dibuja EXACTAMENTE la
/// fracción que el porcentaje del texto declara: nunca se contradicen (UX-DR-5,
/// UX-DR-13).
/// </para>
/// <para>
/// La FASE nombra el paso («Extrayendo», «Aplicando manifiesto») y la regla de lo que es
/// privilegiado NO se rederiva aquí: la vista sólo presenta (AD-1, AD-7).
/// </para>
/// <para>
/// El error se presenta con sus TRES partes, y la acción «Reintentar» aparece cuando la
/// operación quedó FALLIDA, porque la operación es reintentable sin reinstalar el
/// cliente (AC5, NFR-9, UX-DR-34).
/// </para>
/// </summary>
public sealed class InstallProgressViewModel
{
    /// <summary>Text of the action that retries a failed installation (AC5, UX-DR-34).</summary>
    public const string RetryActionText = "Reintentar";

    /// <summary>Separator between the phase and the percentage (AC4).</summary>
    public const string PhaseSeparator = " · ";

    private InstallProgress _progress = InstallProgress.Idle;
    private string? _errorCode;
    private bool _canRetry;

    /// <summary>Raised when the view model changes and the view has to be repainted.</summary>
    public event Action? Changed;

    /// <summary>
    /// Applies the progress of an installation operation.
    /// </summary>
    /// <param name="progress">Progress that the operation published.</param>
    public void Apply(InstallProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);

        _progress = progress;

        // AC5: la operación FALLIDA es REINTENTABLE, así que la acción «Reintentar» está
        // disponible sin reinstalar el cliente (NFR-9, UX-DR-34).
        _canRetry = progress.IsFailed;

        Changed?.Invoke();
    }

    /// <summary>
    /// Applies the ERROR of a failed installation, con su código estable (AC5).
    /// </summary>
    /// <param name="errorCode">
    /// Stable code of the error of the catalog, or <c>null</c> when the operation has no
    /// error.
    /// </param>
    public void ApplyError(string? errorCode)
    {
        _errorCode = errorCode;

        Changed?.Invoke();
    }

    /// <summary>The progress of the operation.</summary>
    public InstallProgress Progress => _progress;

    /// <summary>The phase of the operation, as its MICROCOPY (AC4).</summary>
    public string PhaseText => PhaseTextOf(_progress.Phase);

    /// <summary>
    /// The text of the progress, as «&lt;fase&gt; · &lt;n&gt; %» (AC4, UX-DR-34).
    ///
    /// <para>
    /// El porcentaje es el ENTERO que declara el progreso, así que la barra y el texto
    /// nunca se contradicen (UX-DR-13).
    /// </para>
    /// </summary>
    public string ProgressText => $"{PhaseText}{PhaseSeparator}{_progress.Percentage} %";

    /// <summary>
    /// The fraction that the bar draws, the SAME that the percentage declares
    /// (UX-DR-13).
    /// </summary>
    public double BarFraction => _progress.Fraction;

    /// <summary>
    /// The error of the operation, con sus TRES partes, or <c>null</c> when the operation
    /// has no error (AC5, UX-DR-36).
    /// </summary>
    public ErrorMessageViewModel? Error =>
        _errorCode is null
            ? null
            : new ErrorMessageViewModel(_errorCode);

    /// <summary>
    /// True when the operation FAILED and it can be retried (AC5, NFR-9, UX-DR-34).
    /// </summary>
    public bool CanRetry => _canRetry;

    /// <summary>
    /// The text of the phase, taken from the single rule of the application, para que la
    /// vista no escriba la fase por su cuenta (AD-1, AD-25).
    /// </summary>
    /// <param name="phase">Phase of the operation.</param>
    /// <returns>The text of the phase.</returns>
    public static string PhaseTextOf(InstallPhase phase) => phase switch
    {
        InstallPhase.Idle => "Preparando la instalación",
        InstallPhase.Extracting => "Extrayendo",
        InstallPhase.ApplyingManifest => "Aplicando manifiesto",
        InstallPhase.AwaitingConfirmation => "Esperando confirmación",
        InstallPhase.Validating => "Validando la instalación",
        InstallPhase.Completed => "Instalación completada",
        InstallPhase.Failed => "La instalación falló",
        _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, "Unknown install phase."),
    };
}
