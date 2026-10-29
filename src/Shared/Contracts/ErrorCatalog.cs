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
    /// A mandatory metadata of FR-1 is empty or absent and the catalog has no code that
    /// names the concrete field (AD-15, AD-25). It is the generic fallback of the rejection
    /// of a missing metadata; the codes that name a field are the ones that the
    /// administration of the catalog answers (AD-34).
    /// </summary>
    public const string GameMetadataMissingCode = "GAME_METADATA_MISSING";

    /// <summary>
    /// The body of the mutation does not carry the metadata of the videogame at all
    /// (AD-5). It is a different absence from the one of the <c>schemaVersion</c>, and it
    /// is counted apart so the metric of errors per code does not mix the two.
    /// </summary>
    public const string GamePayloadMissingCode = "GAME_PAYLOAD_MISSING";

    /// <summary>
    /// The body of the mutation does not carry the <c>schemaVersion</c> that every mutation
    /// of the API carries (AD-5).
    /// </summary>
    public const string SchemaVersionMissingCode = "SCHEMA_VERSION_MISSING";

    /// <summary>
    /// The body of the mutation carries a <c>schemaVersion</c> that the server does not
    /// support (AD-5, AD-8).
    /// </summary>
    public const string SchemaVersionNotSupportedCode = "SCHEMA_VERSION_NOT_SUPPORTED";

    /// <summary>
    /// The modification of a videogame carries a <c>gameId</c> that is not in the catalog
    /// (AD-22).
    /// </summary>
    public const string GameNotFoundCode = "GAME_NOT_FOUND";

    /// <summary>
    /// The range of players of the videogame is not coherent: the minimum is greater than
    /// the maximum (AD-1, FR-1).
    /// </summary>
    public const string GamePlayerRangeInvalidCode = "GAME_PLAYER_RANGE_INVALID";

    /// <summary>
    /// The videogame does not carry its identifier (AD-1, AD-34).
    /// </summary>
    public const string GameIdMissingCode = "GAME_ID_MISSING";

    /// <summary>The videogame does not carry its name (AD-1, FR-1).</summary>
    public const string GameNameMissingCode = "GAME_NAME_MISSING";

    /// <summary>The videogame does not carry its description (AD-1, FR-1).</summary>
    public const string GameDescriptionMissingCode = "GAME_DESCRIPTION_MISSING";

    /// <summary>The videogame does not carry its categories (AD-1, FR-1).</summary>
    public const string GameCategoriesMissingCode = "GAME_CATEGORIES_MISSING";

    /// <summary>The videogame does not carry its tags (AD-1, FR-1).</summary>
    public const string GameTagsMissingCode = "GAME_TAGS_MISSING";

    /// <summary>The videogame does not carry its cover image (AD-1, FR-1).</summary>
    public const string GameCoverImageMissingCode = "GAME_COVER_IMAGE_MISSING";

    /// <summary>The videogame does not carry its images (AD-1, FR-1).</summary>
    public const string GameImagesMissingCode = "GAME_IMAGES_MISSING";

    /// <summary>The videogame does not carry its version (AD-1, FR-1).</summary>
    public const string GameVersionMissingCode = "GAME_VERSION_MISSING";

    /// <summary>The videogame does not carry its size in exact bytes (AD-1, AD-7).</summary>
    public const string GameSizeBytesMissingCode = "GAME_SIZE_BYTES_MISSING";

    /// <summary>The videogame does not carry its minimum number of players (AD-1, FR-1).</summary>
    public const string GameMinPlayersMissingCode = "GAME_MIN_PLAYERS_MISSING";

    /// <summary>The videogame does not carry its maximum number of players (AD-1, FR-1).</summary>
    public const string GameMaxPlayersMissingCode = "GAME_MAX_PLAYERS_MISSING";

    /// <summary>The videogame does not carry its operating system (AD-1, FR-1).</summary>
    public const string GameOperatingSystemMissingCode = "GAME_OPERATING_SYSTEM_MISSING";

    /// <summary>The videogame does not carry its distribution type (AD-1, FR-1).</summary>
    public const string GameDistributionTypeMissingCode = "GAME_DISTRIBUTION_TYPE_MISSING";

    /// <summary>The videogame does not carry its installation type (AD-1, FR-1).</summary>
    public const string GameInstallationTypeMissingCode = "GAME_INSTALLATION_TYPE_MISSING";

    /// <summary>The videogame does not carry its launch configuration (AD-1, FR-1).</summary>
    public const string GameLaunchConfigurationMissingCode = "GAME_LAUNCH_CONFIGURATION_MISSING";

    /// <summary>The videogame does not carry its dependencies (AD-1, FR-1).</summary>
    public const string GameDependenciesMissingCode = "GAME_DEPENDENCIES_MISSING";

    /// <summary>The videogame does not carry its license (AD-1, FR-1, NFR-8).</summary>
    public const string GameLicenseMissingCode = "GAME_LICENSE_MISSING";

    /// <summary>The videogame does not carry its distribution restrictions (AD-1, FR-1).</summary>
    public const string GameDistributionRestrictionsMissingCode = "GAME_DISTRIBUTION_RESTRICTIONS_MISSING";

    /// <summary>
    /// The body of the publication does not carry the data of the package or its
    /// installation manifest (AD-5).
    /// </summary>
    public const string PackagePayloadMissingCode = "PACKAGE_PAYLOAD_MISSING";

    /// <summary>
    /// The identity that the installation manifest declares does not match the identity
    /// that the descriptor of the package declares (AD-2, AD-7).
    ///
    /// <para>
    /// The manifest y el descriptor son dos formas de la misma publicación, así que su
    /// identificador, su versión y su tamaño tienen que coincidir: si no coinciden, la
    /// correspondencia entre el manifiesto y el paquete no está firmada y la publicación
    /// se rechaza antes de escribir nada (AC7).
    /// </para>
    /// </summary>
    public const string PackageManifestMismatchCode = "PACKAGE_MANIFEST_MISMATCH";

    /// <summary>
    /// The videogame whose package is being published does not carry the license that the
    /// catalog demands before publishing (AD-7, FR-1, NFR-8).
    /// </summary>
    public const string PackageLicenseMissingCode = "PACKAGE_LICENSE_MISSING";

    /// <summary>
    /// The videogame whose package is being published does not carry the restrictions of
    /// distribution, and the catalog forbids publishing a package without them
    /// (AD-7, FR-1, NFR-8).
    /// </summary>
    public const string PackageDistributionRestrictionsMissingCode = "PACKAGE_DISTRIBUTION_RESTRICTIONS_MISSING";

    /// <summary>
    /// The publication names a videogame that is not in the catalog of the event (AD-22).
    /// </summary>
    public const string PackageGameNotFoundCode = "PACKAGE_GAME_NOT_FOUND";

    /// <summary>
    /// The publication names a package that is already published: a published package is
    /// immutable, and changing its content requires publishing a new version (AD-7).
    /// </summary>
    public const string PackageAlreadyPublishedCode = "PACKAGE_ALREADY_PUBLISHED";

    /// <summary>
    /// The manifest of the package declares an operation that is outside the closed set of
    /// the operations of AD-7. There is no operation that runs arbitrary commands.
    /// </summary>
    public const string ManifestOperationNotAllowedCode = "MANIFEST_OPERATION_NOT_ALLOWED";

    /// <summary>
    /// The bytes of the package are missing, or their size does not match the size that
    /// the manifest declares (AD-2, AD-7).
    /// </summary>
    public const string PackageContentMismatchCode = "PACKAGE_CONTENT_MISMATCH";

    /// <summary>
    /// The body of the publication is not a well formed JSON document, or one of its
    /// fields does not have the shape that the contract declares (AD-5).
    ///
    /// <para>
    /// It is a different absence from the one of the operation that is not allowed: a body
    /// that cannot be read is not a manifest with an operation outside the closed set, and
    /// the metric of errors per code does not mix the two (AD-15, AD-25).
    /// </para>
    /// </summary>
    public const string PackageBodyMalformedCode = "PACKAGE_BODY_MALFORMED";

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
            GamePayloadMissingCode,
            "La petición no lleva los metadatos del videojuego",
            "El cuerpo de la mutación no lleva el bloque de metadatos del videojuego que la administración del catálogo espera.",
            "Añade el bloque de metadatos del videojuego al cuerpo de la petición y vuelve a intentarlo."),
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
            GamePlayerRangeInvalidCode,
            "El número de jugadores no es un rango válido",
            "El mínimo de jugadores del videogame es mayor que su máximo.",
            "Corrige el mínimo y el máximo de jugadores y vuelve a guardar el videojuego."),
        new(
            GameIdMissingCode,
            "Falta el identificador del videojuego",
            "El registro del videojuego no lleva el identificador que el catálogo exige.",
            "Vuelve a guardar el videojuego con su identificador."),
        new(
            GameNameMissingCode,
            "Falta el nombre del videojuego",
            "El registro del videojuego no lleva el nombre que el catálogo exige.",
            "Rellena el nombre del videojuego y vuelve a guardarlo."),
        new(
            GameDescriptionMissingCode,
            "Falta la descripción del videojuego",
            "El registro del videojuego no lleva la descripción que el catálogo exige.",
            "Rellena la descripción del videojuego y vuelve a guardarlo."),
        new(
            GameCategoriesMissingCode,
            "Faltan las categorías del videojuego",
            "El registro del videojuego no lleva las categorías que el catálogo exige.",
            "Rellena las categorías del videojuego y vuelve a guardarlo."),
        new(
            GameTagsMissingCode,
            "Faltan las etiquetas del videojuego",
            "El registro del videojuego no lleva las etiquetas que el catálogo exige.",
            "Rellena las etiquetas del videojuego y vuelve a guardarlo."),
        new(
            GameCoverImageMissingCode,
            "Falta la carátula del videojuego",
            "El registro del videojuego no lleva la carátula que el catálogo exige.",
            "Rellena la carátula del videojuego y vuelve a guardarlo."),
        new(
            GameImagesMissingCode,
            "Faltan las imágenes del videojuego",
            "El registro del videojuego no lleva las imágenes que el catálogo exige.",
            "Rellena las imágenes del videojuego y vuelve a guardarlo."),
        new(
            GameVersionMissingCode,
            "Falta la versión del videojuego",
            "El registro del videojuego no lleva la versión que el catálogo exige.",
            "Rellena la versión del videojuego y vuelve a guardarlo."),
        new(
            GameSizeBytesMissingCode,
            "Falta el tamaño del videojuego",
            "El registro del videojuego no lleva el tamaño en bytes exactos que el catálogo exige.",
            "Rellena el tamaño del videogame en bytes exactos y vuelve a guardarlo."),
        new(
            GameMinPlayersMissingCode,
            "Falta el número mínimo de jugadores",
            "El registro del videojuego no lleva el mínimo de jugadores que el catálogo exige.",
            "Rellena el mínimo de jugadores del videojuego y vuelve a guardarlo."),
        new(
            GameMaxPlayersMissingCode,
            "Falta el número máximo de jugadores",
            "El registro del videojuego no lleva el máximo de jugadores que el catálogo exige.",
            "Rellena el máximo de jugadores del videojuego y vuelve a guardarlo."),
        new(
            GameOperatingSystemMissingCode,
            "Falta el sistema operativo del videojuego",
            "El registro del videojuego no lleva el sistema operativo que el catálogo exige.",
            "Rellena el sistema operativo del videojuego y vuelve a guardarlo."),
        new(
            GameDistributionTypeMissingCode,
            "Falta el tipo de distribución del videojuego",
            "El registro del videojuego no lleva el tipo de distribución que el catálogo exige.",
            "Rellena el tipo de distribución del videojuego y vuelve a guardarlo."),
        new(
            GameInstallationTypeMissingCode,
            "Falta el tipo de instalación del videojuego",
            "El registro del videojuego no lleva el tipo de instalación que el catálogo exige.",
            "Rellena el tipo de instalación del videojuego y vuelve a guardarlo."),
        new(
            GameLaunchConfigurationMissingCode,
            "Falta la configuración de lanzamiento del videojuego",
            "El registro del videojuego no lleva la configuración de lanzamiento que el catálogo exige.",
            "Rellena la ruta, los argumentos y el directorio de trabajo del lanzamiento y vuelve a guardarlo."),
        new(
            GameDependenciesMissingCode,
            "Faltan las dependencias del videojuego",
            "El registro del videojuego no lleva las dependencias que el catálogo exige.",
            "Rellena las dependencias del videojuego y vuelve a guardarlo."),
        new(
            GameLicenseMissingCode,
            "Falta la licencia del videojuego",
            "El registro del videojuego no lleva la licencia que el catálogo exige.",
            "Rellena la licencia del videojuego y vuelve a guardarlo."),
        new(
            GameDistributionRestrictionsMissingCode,
            "Faltan las restricciones de distribución del videojuego",
            "El registro del videojuego no lleva las restricciones de distribución que el catálogo exige.",
            "Rellena las restricciones de distribución del videojuego y vuelve a guardarlo."),
        new(
            PackagePayloadMissingCode,
            "La petición no lleva los datos del paquete",
            "El cuerpo de la publicación no lleva el descriptor del paquete ni su manifiesto de instalación.",
            "Añade el descriptor del paquete y su manifiesto al cuerpo de la petición y vuelve a intentarlo."),
        new(
            PackageManifestMismatchCode,
            "El manifiesto no coincide con el descriptor del paquete",
            "El manifiesto declara un identificador, una versión o un tamaño que no son los que declara el descriptor del paquete.",
            "Haz que el manifiesto y el descriptor declaren la misma publicación y vuelve a intentarlo: no se ha escrito ninguna fila."),
        new(
            PackageLicenseMissingCode,
            "Falta la licencia del videojuego del paquete",
            "El registro del videojuego no lleva la licencia que el catálogo exige para publicar su paquete.",
            "Rellena la licencia del videojuego y vuelve a publicar el paquete: no se ha escrito ninguna fila."),
        new(
            PackageDistributionRestrictionsMissingCode,
            "Faltan las restricciones de distribución del videojuego del paquete",
            "El registro del videojuego no lleva las restricciones de distribución que el catálogo exige para publicar su paquete.",
            "Rellena las restricciones de distribución del videojuego y vuelve a publicar el paquete: no se ha escrito ninguna fila."),
        new(
            PackageGameNotFoundCode,
            "El videojuego del paquete no está en el catálogo",
            "El identificador del videojuego de la publicación no corresponde a ningún videogame registrado.",
            "Revisa el identificador del videojuego y vuelve a intentarlo: no se ha escrito ninguna fila."),
        new(
            PackageAlreadyPublishedCode,
            "El paquete ya está publicado",
            "Ya existe un paquete publicado con ese identificador, y un paquete publicado es inmutable.",
            "Publica el cambio como una versión nueva del paquete: la fila existente no se ha modificado."),
        new(
            ManifestOperationNotAllowedCode,
            "El manifiesto declara una operación que no está permitida",
            "El manifiesto declara una operación fuera del conjunto cerrado de extract, createDirectory, copyFile, writeConfig, setLaunchConfig y verify.",
            "Corrige el manifiesto y usa solo las operaciones permitidas: no se ha escrito ninguna fila."),
        new(
            PackageContentMismatchCode,
            "El contenido del paquete no coincide con lo declarado",
            "Los bytes del paquete no existen o su tamaño no coincide con el tamaño que declara el manifiesto.",
            "Revisa la ruta del paquete y su tamaño en bytes exactos, y vuelve a publicarlo: no se ha escrito ninguna fila."),
        new(
            PackageBodyMalformedCode,
            "El cuerpo de la publicación no se puede leer",
            "El cuerpo de la petición no es un JSON válido o alguno de sus campos no tiene la forma que el contrato declara.",
            "Revisa el JSON del cuerpo de la publicación y los campos que el contrato declara, y vuelve a intentarlo."),
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
