using LANPartyHub.Server.Domain.Catalog;

namespace LANPartyHub.Server.Domain.Packages;

/// <summary>
/// Base type of the exceptions of the domain of the distribution of packages (AD-1, AD-15).
///
/// <para>
/// The exceptions of the domain are typed and they live in this layer. The adapter that
/// serves the request translates them into the single error shape of the system,
/// <c>ApiError</c>, with the stable code of the catalog of errors, and never the other way
/// round (AD-15, AD-25).
/// </para>
/// </summary>
public abstract class PackageDomainException : CatalogDomainException
{
    /// <summary>Creates the exception of the domain of the distribution of packages.</summary>
    /// <param name="message">Message that names the concrete field that fails.</param>
    protected PackageDomainException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception of the domain of the distribution of packages.</summary>
    /// <param name="message">Message that names the concrete field that fails.</param>
    /// <param name="innerException">Exception that caused this one.</param>
    protected PackageDomainException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// The body of the publication does not carry the data of the package or the manifest
/// (AD-5).
///
/// <para>
/// It is a different absence from the one of the <c>schemaVersion</c>, and it is counted
/// apart so the metric of errors per code does not mix the two (AD-15).
/// </para>
/// </summary>
public sealed class PackagePayloadMissingException : PackageDomainException
{
    /// <summary>Creates the exception of a body without the data of the publication.</summary>
    public PackagePayloadMissingException()
        : base("El cuerpo de la petición no lleva los datos del paquete ni su manifiesto de instalación.")
    {
    }
}

/// <summary>
/// The videogame whose package is being published does not carry the license, and the
/// catalog forbids publishing a package without it (AD-7, FR-1, NFR-8).
///
/// <para>
/// The exception names the concrete field that is missing, so the organizer knows which
/// one to fill in without having to guess (AD-25).
/// </para>
/// </summary>
public sealed class PackageLicenseMissingException : PackageDomainException
{
    /// <summary>Creates the exception of a videogame without license.</summary>
    /// <param name="fieldName">Name of the field that is missing or empty.</param>
    public PackageLicenseMissingException(string fieldName)
        : base($"Falta la licencia del videogame '{fieldName}' que el catálogo exige para publicar su paquete.")
    {
        FieldName = fieldName;
    }

    /// <summary>Name of the field that is missing or empty.</summary>
    public string FieldName { get; }
}

/// <summary>
/// The videogame whose package is being published does not carry the restrictions of
/// distribution, and the catalog forbids publishing a package without them
/// (AD-7, FR-1, NFR-8).
///
/// <para>
/// The exception names the concrete field that is missing, so the organizer knows which
/// one to fill in without having to guess (AD-25).
/// </para>
/// </summary>
public sealed class PackageDistributionRestrictionsMissingException : PackageDomainException
{
    /// <summary>Creates the exception of a videogame without restrictions of distribution.</summary>
    /// <param name="fieldName">Name of the field that is missing or empty.</param>
    public PackageDistributionRestrictionsMissingException(string fieldName)
        : base($"Faltan las restricciones de distribución del videogame '{fieldName}' que el catálogo exige para publicar su paquete.")
    {
        FieldName = fieldName;
    }

    /// <summary>Name of the field that is missing or empty.</summary>
    public string FieldName { get; }
}

/// <summary>
/// The publication names a videogame that is not in the catalog of the event (AD-22).
/// </summary>
public sealed class PackageNotFoundException : PackageDomainException
{
    /// <summary>Creates the exception of a videogame that is not in the catalog.</summary>
    /// <param name="gameId">Identifier of the videogame that was not found.</param>
    public PackageNotFoundException(string gameId)
        : base($"El videogame '{gameId}' no está en el catálogo, así que su paquete no se puede publicar.")
    {
        GameId = gameId;
    }

    /// <summary>Identifier of the videogame that was not found.</summary>
    public string GameId { get; }
}

/// <summary>
/// The publication names a package that is already published. A published package is
/// immutable, and changing its content requires publishing a new version (AD-7).
/// </summary>
public sealed class PackageAlreadyPublishedException : PackageDomainException
{
    /// <summary>Creates the exception of a package that is already published.</summary>
    /// <param name="packageId">Identifier of the package that is already published.</param>
    public PackageAlreadyPublishedException(string packageId)
        : base($"El paquete '{packageId}' ya está publicado y un paquete publicado es inmutable.")
    {
        PackageId = packageId;
    }

    /// <summary>Identifier of the package that is already published.</summary>
    public string PackageId { get; }
}

/// <summary>
/// The bytes of the package are missing, or their size does not match the size that the
/// manifest declares (AD-2, AD-7).
/// </summary>
public sealed class PackageContentMismatchException : PackageDomainException
{
    /// <summary>Creates the exception of a package whose content does not match.</summary>
    /// <param name="expectedSizeBytes">Size in exact bytes that the manifest declares.</param>
    /// <param name="actualSizeBytes">Size in exact bytes of the bytes that were found, or <c>null</c> when they are absent.</param>
    public PackageContentMismatchException(long expectedSizeBytes, long? actualSizeBytes)
        : base(actualSizeBytes is null
            ? $"No existen los bytes del paquete, que deberían ocupar {expectedSizeBytes} bytes."
            : $"Los bytes del paquete ocupan {actualSizeBytes.Value} bytes y el manifiesto declara {expectedSizeBytes} bytes.")
    {
        ExpectedSizeBytes = expectedSizeBytes;
        ActualSizeBytes = actualSizeBytes;
    }

    /// <summary>Size in exact bytes that the manifest declares.</summary>
    public long ExpectedSizeBytes { get; }

    /// <summary>
    /// Size in exact bytes of the bytes that were found, or <c>null</c> when they are
    /// absent.
    /// </summary>
    public long? ActualSizeBytes { get; }
}

/// <summary>
/// The identity that the installation manifest declares does not match the identity that
/// the descriptor of the package declares (AD-2, AD-7).
///
/// <para>
/// The manifest and the descriptor are two shapes of the same publication, so their
/// identifier, their version and their size have to be the same. When they are not, the
/// correspondence between the manifest and the package is not signed, and the
/// publication is rejected before anything is written (AC7).
/// </para>
/// </summary>
public sealed class PackageManifestMismatchException : PackageDomainException
{
    /// <summary>
    /// Creates the exception of a manifest that does not match the descriptor of the
    /// package.
    /// </summary>
    /// <param name="fieldName">Name of the field that does not match.</param>
    /// <param name="expected">Value that the descriptor declares.</param>
    /// <param name="actual">Value that the manifest declares.</param>
    public PackageManifestMismatchException(string fieldName, string expected, string actual)
        : base($"El manifiesto declara '{fieldName}' con el valor '{actual}' y el descriptor del paquete declara '{expected}'.")
    {
        FieldName = fieldName;
        Expected = expected;
        Actual = actual;
    }

    /// <summary>Name of the field that does not match.</summary>
    public string FieldName { get; }

    /// <summary>Value that the descriptor declares.</summary>
    public string Expected { get; }

    /// <summary>Value that the manifest declares.</summary>
    public string Actual { get; }
}

/// <summary>
/// The manifest of the package declares an operation that is outside the closed set of
/// the operations of AD-7. There is no operation that runs arbitrary commands (AD-7, AD-8).
/// </summary>
public sealed class ManifestOperationNotAllowedException : PackageDomainException
{
    /// <summary>Creates the exception of a manifest with an operation that is not allowed.</summary>
    /// <param name="operationName">Name of the operation that is not allowed.</param>
    public ManifestOperationNotAllowedException(string operationName)
        : base($"La operación de manifiesto '{operationName}' no pertenece al conjunto cerrado de las operaciones permitidas.")
    {
        OperationName = operationName;
    }

    /// <summary>Name of the operation that is not allowed.</summary>
    public string OperationName { get; }
}
