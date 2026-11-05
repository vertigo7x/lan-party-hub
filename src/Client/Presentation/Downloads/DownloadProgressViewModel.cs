using LANPartyHub.Client.Application.Downloads;
using LANPartyHub.Client.Application.Integrity;
using LANPartyHub.Client.Presentation.Errors;
using LANPartyHub.Client.Presentation.Formatting;
using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Client.Presentation.Downloads;

/// <summary>
/// View model of the progress of a download operation (AC2, AC3, AC4, UX-DR-5, UX-DR-6,
/// UX-DR-13, UX-DR-22).
///
/// <para>
/// El view model toma el progreso de la operación y lo proyecta: la barra y el texto
/// salen de UNA SOLA fuente, así que nunca se contradicen (UX-DR-5, UX-DR-13).
/// </para>
/// <para>
/// La microcopia del estado sale de la tabla única de <c>Shared</c> y no de la vista
/// (AD-10, AD-25).
/// </para>
/// <para>
/// AC1, AC2: una validación fallida se presenta DIFERENCIADA: «Paquete dañado: el hash
/// no coincide» NO es «Paquete no confiable: no se pudo validar contra la raíz de
/// confianza», y el alta de la instalación queda bloqueada con su motivo VISIBLE.
/// </para>
/// </summary>
public sealed class DownloadProgressViewModel
{
    /// <summary>Text of the action that pauses a download (UX-DR-6).</summary>
    public const string PauseActionText = "Pausar";

    /// <summary>Text of the action that resumes a download (UX-DR-6, UX-DR-22).</summary>
    public const string ResumeActionText = "Reanudar";

    /// <summary>Text of the action that cancels a download (UX-DR-8, UX-DR-22).</summary>
    public const string CancelActionText = "Cancelar";

    /// <summary>
    /// Prefix of the microcopy of an operation that is being resumed after a restart
    /// (AC2, UX-DR-6, UX-DR-45).
    ///
    /// <para>
    /// «Reanudando desde » NO es un estado nuevo: <see cref="DownloadState"/> sigue
    /// cerrada y el estado de la operación sigue siendo el que la máquina declara. La
    /// frase es MICROCOPY, y se compone con el texto del progreso, así que la vista
    /// muestra «Reanudando desde 42 % · 3,2 GB de 7,6 GB» (AD-10, AD-25).
    /// </para>
    /// </summary>
    public const string ResumingPrefixText = "Reanudando desde ";

    /// <summary>
    /// Separator between the microcopy of the state and the size of the package, as in
    /// «Paquete dañado: el hash no coincide · 7,6 GB» (UX-DR-6, UX-DR-33).
    /// </summary>
    public const string SizeSeparator = " · ";

    private DownloadOperationHandle? _handle;
    private bool _resuming;

    /// <summary>Raised when the view model changes and the view has to be repainted.</summary>
    public event Action? Changed;

    /// <summary>
    /// Applies the state and the progress of an operation.
    /// </summary>
    /// <param name="handle">Handle of the operation, or <c>null</c> when there is none.</param>
    public void Apply(DownloadOperationHandle? handle)
    {
        _handle = handle;

        Changed?.Invoke();
    }

    /// <summary>The state of the operation, or <c>null</c> when there is none.</summary>
    public DownloadState? State => _handle?.StateMachine.Current;

    /// <summary>
    /// The handle of the operation, or <c>null</c> when the client has none.
    /// </summary>
    public DownloadOperationHandle? Handle => _handle;

    /// <summary>
    /// The microcopy of the state, taken from the single table of <c>Shared</c>
    /// (AD-10, AD-25).
    /// </summary>
    public string StateText => _handle is null
        ? string.Empty
        : StateDisplayMap.GetText(_handle.StateMachine.Current);

    /// <summary>
    /// True when the operation ended in a failed VALIDATION, así que el estado que se
    /// presenta no es el genérico «Error» sino el diferenciado: «Paquete dañado: el hash
    /// no coincide» o «Paquete no confiable: no se pudo validar contra la raíz de
    /// confianza» (AC1, AC2, AD-8).
    /// </summary>
    public bool HasValidationFailure => _handle?.ValidationError is not null;

    /// <summary>
    /// The microcopy of the state of a FAILED VALIDATION, taken from the single table of
    /// <c>Shared</c>, or the generic state text when the validation did not fail
    /// (AD-10, AD-25, AC1, AC2).
    /// </summary>
    public string ValidationStateText => _handle?.ValidationError is null
        ? StateText
        : StateDisplayMap.GetText(_handle.ValidationError.Code);

    /// <summary>
    /// The text of the state WITH the size of the package, as «Paquete dañado: el hash no
    /// coincide · 7,6 GB» (AC1, UX-DR-6, UX-DR-33).
    ///
    /// <para>
    /// El tamaño lo compone la <c>SizeFormatter</c> de la capa de presentación, así que el
    /// sufijo NUNCA se escribe a mano en la vista (AD-25).
    /// </para>
    /// </summary>
    public string StateTextWithSize
    {
        get
        {
            var text = ValidationStateText;

            if (string.IsNullOrWhiteSpace(text) || _handle?.TotalBytes is not { } total)
            {
                return text;
            }

            return $"{text}{SizeSeparator}{SizeFormatter.Format(total)}";
        }
    }

    /// <summary>
    /// The error of the operation, con sus TRES partes, or <c>null</c> when the operation
    /// has no error (AC3, UX-DR-36, AD-15).
    /// </summary>
    public ErrorMessageViewModel? Error =>
        _handle?.ValidationError is { } error
            ? new ErrorMessageViewModel(error)
            : null;

    /// <summary>
    /// The gate of the ALTA de la instalación: el alta queda BLOQUEADA con un motivo
    /// VISIBLE cuando la validación del paquete falló (AC1, UX-DR-33).
    ///
    /// <para>
    /// La regla vive en la capa de aplicación y la vista sólo la presenta, así que el
    /// motivo del bloqueo es el mismo texto que la interfaz muestra en el estado
    /// (AD-8, AD-25).
    /// </para>
    /// </summary>
    public PackageInstallEligibility InstallEligibility =>
        new(_handle?.ValidationError);

    /// <summary>
    /// True when the alta de la instalación is BLOCKED (AC1).
    /// </summary>
    public bool IsInstallBlocked => InstallEligibility.IsBlocked;

    /// <summary>
    /// The VISIBLE reason of the block of the alta, or <c>null</c> when the alta is not
    /// blocked (AC1, UX-DR-33).
    /// </summary>
    public string? InstallBlockedReason => InstallEligibility.BlockedReason;

    /// <summary>
    /// The progress of the operation, with its percentage and its pair of sizes.
    /// </summary>
    public DownloadProgress Progress => _handle?.Progress ?? new DownloadProgress(0, null);

    /// <summary>
    /// The text of the progress, as «&lt;n&gt; % · &lt;a&gt; GB de &lt;t&gt; GB»
    /// (UX-DR-5).
    /// </summary>
    public string ProgressText => Progress.Text;

    /// <summary>
    /// True when the operation is being RESUMED after a restart of the machine, so the
    /// interface names the offset del que la descarga continúa (AC2, UX-DR-45).
    /// </summary>
    public bool IsResuming => _resuming;

    /// <summary>
    /// The text of the progress with the microcopy of la reanudación, as «Reanudando
    /// desde 42 % · 3,2 GB de 7,6 GB» (AC2, UX-DR-6, UX-DR-45).
    ///
    /// <para>
    /// La frase es MICROCOPY y no un estado: el estado de la operación sigue siendo el que
    /// la máquina declara, y el texto del progreso sigue siendo la fuente ÚNICA del
    /// porcentaje y del par de tamaños (AD-10, AD-25, UX-DR-5).
    /// </para>
    /// </summary>
    public string ProgressTextWithResume =>
        _resuming
            ? $"{ResumingPrefixText}{Progress.Text}"
            : Progress.Text;

    /// <summary>
    /// Marks the operation as RESUMED, so the interface muestra el offset del que la
    /// descarga continúa (AC2, UX-DR-45).
    /// </summary>
    /// <param name="resuming">Whether the operation is being resumed.</param>
    public void ApplyResuming(bool resuming)
    {
        _resuming = resuming;

        Changed?.Invoke();
    }

    /// <summary>
    /// The fraction that the bar draws, the SAME that the percentage declares
    /// (UX-DR-13).
    /// </summary>
    public double BarFraction => Progress.Fraction;

    /// <summary>
    /// True when the operation is running and the action «Pausar» está disponible
    /// (UX-DR-6).
    /// </summary>
    public bool CanPause => State == DownloadState.Downloading;

    /// <summary>
    /// True when the operation is paused and the action «Reanudar» está disponible
    /// (UX-DR-6).
    /// </summary>
    public bool CanResume => State == DownloadState.Paused;

    /// <summary>
    /// True when the operation can be cancelled: una descarga en curso o pausada
    /// (AC4, UX-DR-22).
    /// </summary>
    public bool CanCancel => State is DownloadState.Downloading or DownloadState.Paused;

    /// <summary>
    /// The text of the confirmation de la cancelación, naming WHAT IS LOST
    /// (AC4, UX-DR-22).
    ///
    /// <para>
    /// El texto nombra los bytes que se van a perder: «Se borrarán los 3,2 GB
    /// descargados». Cancelar una descarga es la acción que DESTRUYE trabajo no
    /// recuperable, así que la interfaz pregunta antes (UX-DR-8, UX-DR-22).
    /// </para>
    /// </summary>
    public string CancelConfirmationText =>
        $"Se borrarán los {DownloadProgress.FormatGigabytes(Progress.BytesTransferred)} GB descargados";

    /// <summary>
    /// True when there is an operation to show.
    /// </summary>
    public bool HasOperation => _handle is not null;
}
