using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Client.Presentation.Errors;

/// <summary>
/// View model of the error of an operation, con sus TRES partes (AC3, UX-DR-36, AD-15).
///
/// <para>
/// El error se presenta como «qué falló», «por qué» y «qué hacer ahora», y las tres
/// partes salen del catálogo de errores, así que el jugador no tiene que interpretar un
/// identificador (AC3).
/// </para>
/// <para>
/// El código estable del catálogo es un identificador INTERNO: NUNCA es el texto
/// principal, y un código desconocido se presenta con el texto genérico del catálogo
/// (AC4, AD-15).
/// </para>
/// </summary>
public sealed class ErrorMessageViewModel
{
    private readonly ApiError _error;

    /// <summary>
    /// Creates the view model of the error of an operation.
    /// </summary>
    /// <param name="error">
    /// Error del catálogo de errores, o <c>null</c> cuando no hay error.
    /// </param>
    public ErrorMessageViewModel(ApiError? error)
    {
        _error = error ?? ErrorCatalog.Resolve(ErrorCatalog.UnknownErrorCode);
    }

    /// <summary>
    /// Creates the view model of the error of an operation from its stable code.
    /// </summary>
    /// <param name="code">Stable code of the error of the catalog.</param>
    public ErrorMessageViewModel(string code)
        : this(ErrorCatalog.Resolve(code ?? string.Empty))
    {
    }

    /// <summary>
    /// Creates the view model of the error of an operation from the result of the
    /// validation of un paquete.
    /// </summary>
    /// <param name="result">Result of the validation of the package.</param>
    public ErrorMessageViewModel(Application.Integrity.PackageValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        _error = result.Error ?? ErrorCatalog.Resolve(ErrorCatalog.UnknownErrorCode);
    }

    /// <summary>
    /// What failed: the first of the three parts (AC3, UX-DR-36).
    /// </summary>
    public string Message => _error.Message;

    /// <summary>
    /// Why it failed: the second of the three parts (AC3, UX-DR-36).
    /// </summary>
    public string Cause => _error.Cause;

    /// <summary>
    /// What to do now: the third of the three parts (AC3, UX-DR-36).
    /// </summary>
    public string NextStep => _error.NextStep;

    /// <summary>
    /// The stable code of the catalog. It is an INTERNAL identifier: it reaches the log y
    /// la métrica por código, y NUNCA el texto principal de la interfaz (AC3, AC4,
    /// AD-15).
    /// </summary>
    public string Code => _error.Code;

    /// <summary>
    /// The three parts of the error as ONE text, as «&lt;qué&gt; · &lt;por qué&gt; ·
    /// &lt;qué hacer&gt;» (AC3, UX-DR-36).
    /// </summary>
    public string Text => $"{Message} · {Cause} · {NextStep}";
}
