using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Client.Application.Integrity;

/// <summary>
/// Gate of the ALTA de la instalación de un paquete (AD-8, AC1, FR-6).
///
/// <para>
/// La regla vive AQUÍ, en la capa de aplicación, y no en la vista: la vista sólo la
/// presenta. Un paquete que no ha superado la validación NO se puede instalar, así que el
/// alta queda BLOQUEADA y el motivo es VISIBLE para el jugador, no un renglón oculto en
/// el registro (AC1, UX-DR-33).
/// </para>
/// <para>
/// El motivo del bloqueo es el TEXTO del catálogo de errores con sus tres partes: qué
/// falló, por qué y qué hacer ahora. El código estable es un identificador interno y
/// NUNCA es el texto principal (AC3, AC4, AD-15).
/// </para>
/// </summary>
public sealed class PackageInstallEligibility
{
    private readonly ApiError? _error;

    /// <summary>
    /// Creates the gate of the alta of the installation of a package.
    /// </summary>
    /// <param name="error">
    /// Error del catálogo que explica por qué el paquete no se puede instalar, o
    /// <c>null</c> cuando el paquete es válido.
    /// </param>
    public PackageInstallEligibility(ApiError? error)
    {
        _error = error;
    }

    /// <summary>
    /// Creates the gate of the alta of a package whose validation FAILED.
    /// </summary>
    /// <param name="result">Result of the validation that rejected the package.</param>
    public PackageInstallEligibility(PackageValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        _error = result.Error;
    }

    /// <summary>
    /// Whether the alta de la instalación is BLOCKED (AD-8, AC1).
    /// </summary>
    public bool IsBlocked => _error is not null;

    /// <summary>
    /// The VISIBLE reason of the block, or <c>null</c> when the alta is not blocked
    /// (AC1, UX-DR-33).
    ///
    /// <para>
    /// El motivo es el mensaje del catálogo de errores, así que el jugador lee POR QUÉ no
    /// se puede instalar: el código estable no llega nunca como texto (AC3, AC4).
    /// </para>
    /// </summary>
    public string? BlockedReason => _error?.Message;

    /// <summary>
    /// The error of the catalog that explains the block, or <c>null</c> when the alta is not
    /// blocked (AC3, AD-15).
    /// </summary>
    public ApiError? Error => _error;
}
