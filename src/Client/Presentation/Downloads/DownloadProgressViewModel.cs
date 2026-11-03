using LANPartyHub.Client.Application.Downloads;
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
/// </summary>
public sealed class DownloadProgressViewModel
{
    /// <summary>Text of the action that pauses a download (UX-DR-6).</summary>
    public const string PauseActionText = "Pausar";

    /// <summary>Text of the action that resumes a download (UX-DR-6, UX-DR-22).</summary>
    public const string ResumeActionText = "Reanudar";

    /// <summary>Text of the action that cancels a download (UX-DR-8, UX-DR-22).</summary>
    public const string CancelActionText = "Cancelar";

    private DownloadOperationHandle? _handle;

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
    /// The progress of the operation, with its percentage and its pair of sizes.
    /// </summary>
    public DownloadProgress Progress => _handle?.Progress ?? new DownloadProgress(0, null);

    /// <summary>
    /// The text of the progress, as «&lt;n&gt; % · &lt;a&gt; GB de &lt;t&gt; GB»
    /// (UX-DR-5).
    /// </summary>
    public string ProgressText => Progress.Text;

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
