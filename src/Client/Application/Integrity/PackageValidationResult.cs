using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Client.Application.Integrity;

/// <summary>
/// The three ways in which the validation of a package can fail (AD-8, AC6).
///
/// <para>
/// La distinción es OBLIGATORIA: «el ancla no es la esperada» es un ataque o una
/// suplantación, y «la firma no valida contra el ancla correcta» es un manifiesto inválido.
/// Un constructor que colapsa los dos en un solo código de error pierde la información que
/// distingue un ataque de un archivo corrupto (AD-15, AD-25, AD-27).
/// </para>
/// </summary>
public enum PackageValidationOutcome
{
    /// <summary>
    /// The signature and the schema version are valid and the hash of the bytes matches
    /// the one of the signed manifest (AD-2, AD-8).
    /// </summary>
    Valid,

    /// <summary>
    /// The bytes do NOT match the hash of the signed manifest: the package is CORRUPTED
    /// (AD-8, AD-27).
    /// </summary>
    Corrupted,

    /// <summary>
    /// The manifest does NOT validate against the fixed trust anchor: the package is NOT
    /// TRUSTWORTHY (AD-2, AD-27).
    /// </summary>
    Untrusted,

    /// <summary>
    /// The manifest declares a schema version that the client does not support (AD-8).
    /// </summary>
    UnsupportedSchema,
}

/// <summary>
/// Result of the validation of a package (AD-8, AC6).
///
/// <para>
/// El resultado está DISPONIBLE PARA DIAGNÓSTICO e incluye el fallo y su causa, y
/// distingue explícitamente la corrupción de la falta de autenticidad (AC6, FR-7).
/// </para>
/// </summary>
/// <param name="Outcome">How the validation ended.</param>
/// <param name="Error">
/// Error of the catalog that explains the failure, or <c>null</c> when the package is
/// valid.
/// </param>
/// <param name="ExpectedSha256">
/// Hash that the signed manifest declares, or <c>null</c> when the failure happened before
/// the hash could be compared.
/// </param>
/// <param name="ActualSha256">
/// Hash that was computed over the bytes, or <c>null</c> when the failure happened before
/// the hash could be computed.
/// </param>
public sealed record PackageValidationResult(
    PackageValidationOutcome Outcome,
    ApiError? Error,
    string? ExpectedSha256 = null,
    string? ActualSha256 = null)
{
    /// <summary>
    /// Whether the package passed the validation (AD-8).
    /// </summary>
    public bool IsValid => Outcome == PackageValidationOutcome.Valid;

    /// <summary>
    /// Whether the bytes of the package are corrupted (AD-8, AC6).
    /// </summary>
    public bool IsCorrupted => Outcome == PackageValidationOutcome.Corrupted;

    /// <summary>
    /// Whether the package could not be validated against the root of trust (AD-2, AC3).
    /// </summary>
    public bool IsUntrusted => Outcome == PackageValidationOutcome.Untrusted;

    /// <summary>
    /// Whether the manifest declares a schema version that the client does not support
    /// (AD-8, AC5).
    /// </summary>
    public bool HasUnsupportedSchema => Outcome == PackageValidationOutcome.UnsupportedSchema;
}
