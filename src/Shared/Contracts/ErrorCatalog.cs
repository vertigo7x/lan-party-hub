namespace LANPartyHub.Shared.Contracts;

/// <summary>
/// The single, stable error catalog of the system (AD-15, AD-25).
/// It is data, not code: it holds the entries and the lookup that turns a stable
/// <c>code</c> into the three part message (what failed, why, what to do now).
/// The entries are <see cref="ApiError"/> values, the very shape that travels in the API
/// responses and in the <see cref="ClientErrorReported"/> payload.
/// </summary>
public static class ErrorCatalog
{
    /// <summary>Generic fallback code used when an unknown code reaches the client (AD-15).</summary>
    public const string UnknownErrorCode = "UNKNOWN_ERROR";

    /// <summary>The trust anchor fingerprint does not match the expected one (AD-27).</summary>
    public const string AnchorFingerprintMismatchCode = "ANCHOR_FINGERPRINT_MISMATCH";

    /// <summary>The signed manifest does not validate against the fixed trust anchor (AD-8, AD-27).</summary>
    public const string ManifestSignatureInvalidCode = "MANIFEST_SIGNATURE_INVALID";

    /// <summary>The sha256 of the downloaded package does not match the signed manifest (AD-8).</summary>
    public const string PackageIntegrityMismatchCode = "PACKAGE_INTEGRITY_MISMATCH";

    /// <summary>The proposed team name does not satisfy the shape rules of the server (AD-30).</summary>
    public const string MachineNameInvalidCode = "MACHINE_NAME_INVALID";

    private static readonly ApiError[] CatalogEntries =
    [
        new(
            AnchorFingerprintMismatchCode,
            "El fingerprint del ancla de confianza no coincide",
            "La clave pública que presenta el servidor no corresponde al ancla fijada fuera de banda.",
            "Contrasta el fingerprint con el organizador y vuelve a emparejar el cliente: no se ha escrito nada en disco."),
        new(
            ManifestSignatureInvalidCode,
            "El manifiesto de instalación no es confiable",
            "La firma del manifiesto no valida contra el ancla de confianza ya fijada.",
            "Vuelve a sincronizar el catálogo y descarga el paquete de nuevo: no se ha escrito nada en disco."),
        new(
            PackageIntegrityMismatchCode,
            "El paquete está dañado: el hash no coincide",
            "El sha256 calculado del paquete no coincide con el del manifiesto firmado.",
            "Descarta el paquete y descárgalo de nuevo: la operación no se puede reanudar."),
        new(
            MachineNameInvalidCode,
            "El nombre del equipo no es válido",
            "El nombre propuesto no cumple las reglas de forma del servidor.",
            "Elige otro nombre de equipo y vuelve a registrar el cliente."),
        new(
            UnknownErrorCode,
            "Se produjo un error no identificado",
            "El catálogo de errores no tiene una entrada para este caso.",
            "Repite la operación y consulta el registro de diagnóstico del cliente."),
    ];

    private static readonly IReadOnlyDictionary<string, ApiError> EntriesByCode =
        CatalogEntries.ToDictionary(entry => entry.Code, StringComparer.Ordinal);

    private static readonly IReadOnlyList<ApiError> ReadOnlyEntries =
        Array.AsReadOnly((ApiError[])CatalogEntries.Clone());

    private static readonly IReadOnlyList<string> ReadOnlyCodes =
        Array.AsReadOnly(CatalogEntries.Select(entry => entry.Code).ToArray());

    /// <summary>Every entry of the catalog, as a read-only copy.</summary>
    public static IReadOnlyList<ApiError> Entries => ReadOnlyEntries;

    /// <summary>Every stable code of the catalog, as a read-only copy.</summary>
    public static IReadOnlyList<string> Codes => ReadOnlyCodes;

    /// <summary>
    /// Looks up the entry of a code. It follows the .NET convention: when the code is not in
    /// the catalog the method returns false and the <paramref name="error"/> is the default
    /// value, never an invented entry.
    /// </summary>
    /// <param name="code">Stable code to look up.</param>
    /// <param name="error">The entry of the code, or the default value when the code is unknown.</param>
    public static bool TryGet(string code, out ApiError? error)
    {
        if (code is not null && EntriesByCode.TryGetValue(code, out var found))
        {
            error = found;
            return true;
        }

        error = null;
        return false;
    }

    /// <summary>
    /// Resolves a code into a usable error. A known code resolves into its own entry; an
    /// unknown code keeps its own code (so it reaches the log and the per code metric) and
    /// takes the message, the cause and the next step from the generic entry of the catalog,
    /// because the unknown code is never the primary text (AD-15).
    /// </summary>
    /// <param name="code">Stable code to resolve.</param>
    public static ApiError Resolve(string code)
    {
        if (TryGet(code, out var found) && found is not null)
        {
            return found;
        }

        var generic = EntriesByCode[UnknownErrorCode];
        return new ApiError(code ?? string.Empty, generic.Message, generic.Cause, generic.NextStep);
    }
}
