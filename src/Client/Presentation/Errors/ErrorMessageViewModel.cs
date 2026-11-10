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
/// <para>
/// AC5: el aviso de error presenta la CAUSA, la ACCIÓN INTENTADA y el SIGUIENTE PASO,
/// así que el jugador sabe qué se intentó hacer, por qué no salió y qué hacer ahora. La
/// acción intentada es el texto del catálogo mientras nadie declare otra cosa, así que
/// el aviso NUNCA dice menos de lo que dice el catálogo (AD-15, UX-DR-36).
/// </para>
/// </summary>
public sealed class ErrorMessageViewModel
{
    private readonly ApiError _error;
    private string _attemptedAction;

    /// <summary>
    /// Creates the view model of the error of an operation.
    /// </summary>
    /// <param name="error">
    /// Error del catálogo de errores, o <c>null</c> cuando no hay error.
    /// </param>
    public ErrorMessageViewModel(ApiError? error)
    {
        _error = error ?? ErrorCatalog.Resolve(ErrorCatalog.UnknownErrorCode);
        _attemptedAction = _error.Message;
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
        _attemptedAction = _error.Message;
    }

    /// <summary>
    /// Creates the view model of the error of an operation with the ACTION that was
    /// attempted, así que el aviso dice qué se intentó hacer (AC5, UX-DR-36).
    /// </summary>
    /// <param name="error">Error del catálogo de errores.</param>
    /// <param name="attemptedAction">
    /// Acción que el cliente intentó hacer. When it is empty the text of the catalog is
    /// used, así que el aviso NUNCA dice menos de lo que dice el catálogo (AD-15).
    /// </param>
    public ErrorMessageViewModel(ApiError? error, string? attemptedAction)
        : this(error)
    {
        if (!string.IsNullOrWhiteSpace(attemptedAction))
        {
            _attemptedAction = attemptedAction;
        }
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
    /// The ACTION that was attempted, la que el aviso presenta junto a la causa y al
    /// siguiente paso (AC5, UX-DR-36).
    ///
    /// <para>
    /// Es el texto del catálogo mientras nadie declare otra cosa, así que el aviso
    /// NUNCA pierde la información del catálogo (AD-15).
    /// </para>
    /// </summary>
    public string AttemptedAction => _attemptedAction;

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
