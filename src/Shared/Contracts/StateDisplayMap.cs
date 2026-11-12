using System.Globalization;

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

    /// <summary>
    /// Text of the state that the failure of the validation of a package leaves (AD-8,
    /// AD-10, AD-25).
    ///
    /// <para>
    /// La corrupción y la falta de autenticidad son DOS fallos distintos, así que tienen
    /// DOS textos distintos: el jugador tiene que poder saber si el problema es la red o
    /// la confianza (AC1, AC2).
    /// </para>
    /// <para>
    /// Un código que NO está en la tabla NO se inventa: el texto es el genérico del
    /// catálogo de errores, y el código NUNCA es el texto principal (AC4, AD-15).
    /// </para>
    /// </summary>
    /// <param name="code">Stable code of the error of the catalog.</param>
    /// <returns>The text of the state that the failure leaves.</returns>
    public static string GetText(string code) =>
        code switch
        {
            ErrorCatalog.PackageIntegrityMismatchCode => CorruptedPackageText,
            ErrorCatalog.ManifestSignatureInvalidCode => UntrustedPackageText,
            _ => ErrorCatalog.Resolve(code).Message,
        };

    /// <summary>
    /// Text of the badge of a game that the client ALREADY HAS INSTALLED (UX-DR-6, AC3).
    ///
    /// <para>
    /// La insignia del catálogo dice «Ya instalado» y la de la Biblioteca dice
    /// «Instalado»: son DOS textos del CONTRATO y no uno inventado por la vista, así que
    /// viven aquí, en la ÚNICA tabla de microcopia (AD-25, AR-8, R-MVP-7).
    /// </para>
    /// </summary>
    public const string AlreadyInstalledText = "Ya instalado";

    /// <summary>
    /// Separator between the text of the state and the version that is installed
    /// (UX-DR-6, AC3).
    ///
    /// <para>
    /// El separador es el punto medio del contrato («Instalado · v1.2»), y no el punto y
    /// coma de una línea de estado: la insignia compone el texto del estado con la
    /// versión EXACTA, y los literales no se redondean ni se traducen (UX-DR-6).
    /// </para>
    /// </summary>
    public const string InstalledVersionSeparator = " · ";

    /// <summary>
    /// Composes the badge of an installation with the text of its state and the version
    /// that is installed (UX-DR-6, AC3, AR-8).
    ///
    /// <para>
    /// La composición vive AQUÍ, en la tabla de microcopia, así que la vista NO escribe
    /// ningún texto de estado ni ningún separador por su cuenta (AD-25, R-MVP-7).
    /// </para>
    /// </summary>
    /// <param name="state">State of the installation.</param>
    /// <param name="installedVersion">Version that is installed, in its EXACT value.</param>
    /// <returns>The badge of the installation.</returns>
    public static string GetInstalledBadge(InstallationState state, string installedVersion) =>
        $"{GetText(state)}{InstalledVersionSeparator}{installedVersion}";

    /// <summary>
    /// Text of the state of a package whose bytes do NOT match the signed manifest: the
    /// package is DAMAGED (AD-8, AC1).
    /// </summary>
    /// <summary>Prefix of the text of a download that RESUMES (UX-DR-6).</summary>
    public const string ResumingPrefix = "Reanudando desde ";

    /// <summary>Format of a time that went by: «hace 2 min» (UX-DR-6, NFR-2).</summary>
    public const string MinutesAgoFormat = "hace {0} min";

    /// <summary>
    /// The text of a download that RESUMES, as «Reanudando desde 42 % · 3,2 GB de 7,6 GB»
    /// (UX-DR-6).
    /// </summary>
    /// <param name="progressText">Text of the progress that the download resumes.</param>
    /// <returns>The text of the resumed download.</returns>
    public static string GetResumingText(string progressText) => $"{ResumingPrefix}{progressText}";

    /// <summary>
    /// The text of a time that went by, as «hace 2 min» (UX-DR-6, NFR-2).
    /// </summary>
    /// <para>
    /// El tiempo se dice SIEMPRE en minutos y NUNCA en segundos: la velocidad de la
    /// descarga no está validada, así que un «en 30 segundos» sería una promesa que el
    /// cliente no puede cumplir (NFR-2).
    /// </para>
    /// </summary>
    /// <param name="elapsed">Time that has gone by.</param>
    /// <returns>The text of the elapsed time.</returns>
    public static string GetTimeAgoText(TimeSpan elapsed)
    {
        var minutes = (int)Math.Max(0, Math.Round(elapsed.TotalMinutes, MidpointRounding.AwayFromZero));

        return string.Format(CultureInfo.InvariantCulture, MinutesAgoFormat, minutes);
    }

    public const string CorruptedPackageText = "Paquete dañado: el hash no coincide";

    /// <summary>
    /// Text of the state of a package that could NOT be validated against the root of
    /// trust: the package is NOT TRUSTWORTHY (AD-2, AD-8, AC2).
    /// </summary>
    public const string UntrustedPackageText = "Paquete no confiable: no se pudo validar contra la raíz de confianza";
}
