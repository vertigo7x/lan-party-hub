namespace LANPartyHub.Shared.Contracts;

/// <summary>
/// The single table of state microcopy of the system (AD-10, AD-25).
/// It is data, not code: it maps every machine state, every operation outcome and every
/// derived view state to its interface text. The server and the client consume this table;
/// neither of them defines its own texts.
/// </summary>
public static class StateDisplayMap
{
    /// <summary>Text of a download machine state.</summary>
    public static string GetText(DownloadState state) => state switch
    {
        DownloadState.Queued => "En cola",
        DownloadState.Downloading => "Descargando",
        DownloadState.Paused => "Pausada",
        DownloadState.Validating => "Validando",
        DownloadState.Completed => "Descargado",
        DownloadState.Cancelled => "Cancelada",
        DownloadState.Failed => "Error",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown download state."),
    };

    /// <summary>Text of an installation machine state.</summary>
    public static string GetText(InstallationState state) => state switch
    {
        InstallationState.Pending => "Pendiente",
        InstallationState.Installing => "Instalando",
        InstallationState.AwaitingConfirmation => "Esperando confirmación",
        InstallationState.ValidatingInstallation => "Validando la instalación",
        InstallationState.Installed => "Instalado",
        InstallationState.Repairable => "Reparable",
        InstallationState.Failed => "Error",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown installation state."),
    };

    /// <summary>Text of the result of a finished operation.</summary>
    public static string GetText(OperationOutcome outcome) => outcome switch
    {
        OperationOutcome.Succeeded => "Completada",
        OperationOutcome.PackageWithdrawn => "Paquete retirado del catálogo",
        OperationOutcome.Failed => "Error",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unknown operation outcome."),
    };

    /// <summary>Text of a derived catalog view state.</summary>
    public static string GetText(CatalogViewState viewState) => viewState switch
    {
        CatalogViewState.Synchronizing => "sincronizando",
        CatalogViewState.Available => "disponible",
        CatalogViewState.InstalledOutdatedVersion => "instalado con versión desactualizada",
        _ => throw new ArgumentOutOfRangeException(nameof(viewState), viewState, "Unknown catalog view state."),
    };

    /// <summary>
    /// Text of a connection state. The unverified anchor is a security state, not a network
    /// error, and its text lives here because the client shows it (AD-27, AD-25).
    /// </summary>
    public static string GetText(ConnectionState connectionState) => connectionState switch
    {
        ConnectionState.AnchorVerified => "Ancla verificada",
        ConnectionState.AnchorUnverified => "Ancla sin verificar",
        ConnectionState.Disconnected => "Desconectado",
        _ => throw new ArgumentOutOfRangeException(nameof(connectionState), connectionState, "Unknown connection state."),
    };
}
