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

    /// <summary>
    /// The request of the client does not carry the mandatory identifier of the client
    /// (AD-15, AD-23).
    /// </summary>
    public const string ClientIdMissingCode = "CLIENT_ID_MISSING";

    /// <summary>
    /// The request of the client carries an identifier of the client that is not in its
    /// canonical form (AD-15, AD-23).
    /// </summary>
    public const string ClientIdInvalidCode = "CLIENT_ID_INVALID";

    /// <summary>
    /// The registration of a videogame carries a mandatory metadata of FR-1 that is empty
    /// or absent (AD-1, AD-25). The code names the field that is missing.
    /// </summary>
    public const string GameMetadataMissingCode = "GAME_METADATA_MISSING";

    /// <summary>
    /// The body of the registration of a videogame does not carry the <c>schemaVersion</c>
    /// that every mutation of the API carries (AD-5).
    /// </summary>
    public const string SchemaVersionMissingCode = "SCHEMA_VERSION_MISSING";

    /// <summary>
    /// The body of the registration of a videogame carries a <c>schemaVersion</c> that the
    /// server does not support (AD-5, AD-8).
    /// </summary>
    public const string SchemaVersionNotSupportedCode = "SCHEMA_VERSION_NOT_SUPPORTED";

    /// <summary>
    /// The modification of a videogame carries a <c>gameId</c> that is not in the catalog
    /// (AD-22).
    /// </summary>
    public const string GameNotFoundCode = "GAME_NOT_FOUND";

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
            ClientIdMissingCode,
            "La petición no lleva el identificador del cliente",
            "La cabecera X-Client-Id no está en la petición o llega vacía.",
            "Añade la cabecera X-Client-Id con el identificador del equipo y vuelve a intentarlo."),
        new(
            ClientIdInvalidCode,
            "El identificador del cliente no es válido",
            "La cabecera X-Client-Id no lleva los 32 dígitos hexadecimales en minúsculas del identificador del equipo.",
            "Envía el identificador del equipo en su forma canónica y vuelve a intentarlo."),
        new(
            GameMetadataMissingCode,
            "Falta un metadato obligatorio del videojuego",
            "El registro del videojuego no lleva alguno de los metadatos que la administración del catálogo exige.",
            "Rellena el campo que falta y vuelve a guardar el videojuego."),
        new(
            SchemaVersionMissingCode,
            "La petición no lleva la versión del esquema",
            "El cuerpo de la mutación no lleva el campo schemaVersion que la API exige en toda mutación.",
            "Añade schemaVersion al cuerpo de la petición y vuelve a intentarlo."),
        new(
            SchemaVersionNotSupportedCode,
            "La versión del esquema no está soportada",
            "El campo schemaVersion del cuerpo lleva un valor que este servidor no entiende.",
            "Usa una versión del esquema que el servidor soporte y vuelve a intentarlo."),
        new(
            GameNotFoundCode,
            "El videojuego no está en el catálogo",
            "El identificador de la modificación no corresponde a ningún videojuego registrado.",
            "Revisa el identificador del videojuego y vuelve a intentarlo."),
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
