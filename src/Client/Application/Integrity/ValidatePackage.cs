using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Domain.Integrity;
using LANPartyHub.Client.Domain.Trust;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Client.Application.Integrity;

/// <summary>
/// Use case of the validation of a downloaded package (AD-2, AD-8, AD-20, AC1..AC6).
///
/// <para>
/// El orden es NORMATIVO y es el de AD-8: primero la firma, después la versión del
/// esquema, y SOLO después el hash de los bytes. La comprobación de la firma precede a
/// cualquier escritura en disco: un manifiesto que no valida contra el ancla fijada no se
/// persiste en la caché <c>manifests/</c>, no se crea archivo parcial y la operación no se
/// registra como iniciada (AD-2, AD-8, AD-27).
/// </para>
/// <para>
/// La validación de la AUTENTICIDAD y la de la INTEGRIDAD son dos comprobaciones
/// DISTINTAS, con dos códigos estables distintos: la firma es Ed25519 sobre el texto
/// canónico del manifiesto, y el <c>sha256</c> verifica los bytes contra el hash que ese
/// mismo manifiesto firmado declara (AD-2).
/// </para>
/// <para>
/// El caso de uso depende de los PUERTOS y nunca de un adaptador, así que el orden de
/// rechazo y la ausencia de escrituras son observables sin un servidor y sin un disco
/// (AD-1, AD-16).
/// </para>
/// </summary>
public sealed class ValidatePackage
{
    /// <summary>
    /// Version of the schema of the installation manifest that this client supports
    /// (AD-7, AD-8).
    ///
    /// <para>
    /// Un manifiesto que declara otra versión NO se instala: la operación se rechaza, no
    /// se instala y el error se persiste para diagnóstico (AD-8, AC5).
    /// </para>
    /// </summary>
    public const int SupportedManifestSchemaVersion = 1;

    private readonly ISignedManifestClient _manifests;
    private readonly IManifestSignatureVerifier _verifier;
    private readonly IManifestCache _cache;
    private readonly ITrustAnchorReader _anchorReader;
    private readonly ILogger<ValidatePackage> _logger;

    /// <summary>Creates the use case of the validation of a package.</summary>
    /// <param name="manifests">Port of the read of the signed manifest of a package.</param>
    /// <param name="verifier">Port of the verification of the signature of the manifest.</param>
    /// <param name="cache">Port of the cache of the signed manifests.</param>
    /// <param name="anchorReader">Port of the read of the ancla de confianza ya fijada.</param>
    /// <param name="logger">Log of the use case.</param>
    public ValidatePackage(
        ISignedManifestClient manifests,
        IManifestSignatureVerifier verifier,
        IManifestCache cache,
        ITrustAnchorReader anchorReader,
        ILogger<ValidatePackage> logger)
    {
        ArgumentNullException.ThrowIfNull(manifests);
        ArgumentNullException.ThrowIfNull(verifier);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(anchorReader);
        ArgumentNullException.ThrowIfNull(logger);

        _manifests = manifests;
        _verifier = verifier;
        _cache = cache;
        _anchorReader = anchorReader;
        _logger = logger;
    }

    /// <summary>
    /// Validates the AUTHENTICITY of the manifest of a package: its signature against the
    /// ancla de confianza ya fijada and its schema version against the one that the client
    /// supports (AD-2, AD-8).
    ///
    /// <para>
    /// AD-2, AD-8: el manifiesto se pide al servidor, su firma se valida contra el ancla
    /// YA FIJADA y su versión de esquema se comprueba contra la que el cliente soporta.
    /// Sólo cuando las dos comprobaciones pasan el manifiesto se cachea: un manifiesto que
    /// no es de fiar no deja NINGUNA escritura en disco.
    /// </para>
    /// </summary>
    /// <param name="packageId">Opaque identifier of the package that is validated.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The result of the validation.</returns>
    public async Task<PackageValidationResult> ValidateManifestAsync(
        string packageId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        // AD-27: la validación se hace contra el ancla YA FIJADA. Sin ancla fijada no hay
        // contra qué validar, así que la operación no se puede lanzar y no se escribe nada.
        var anchor = await _anchorReader
            .LoadTrustAnchorAsync(cancellationToken)
            .ConfigureAwait(false);

        if (anchor is null)
        {
            _logger.LogWarning(
                "El cliente no tiene un ancla de confianza fijada: no puede validar el paquete {PackageId} y no escribe nada en disco.",
                packageId);

            return new PackageValidationResult(
                PackageValidationOutcome.Untrusted,
                ErrorCatalog.Resolve(ErrorCatalog.AnchorFingerprintMismatchCode));
        }

        var envelope = await _manifests
            .GetAsync(packageId, cancellationToken)
            .ConfigureAwait(false);

        if (envelope is null)
        {
            _logger.LogWarning(
                "El servidor no sirvió un manifiesto firmado para el paquete {PackageId}: el cliente no escribe nada en disco.",
                packageId);

            return new PackageValidationResult(
                PackageValidationOutcome.Untrusted,
                ErrorCatalog.Resolve(ErrorCatalog.ManifestSignatureInvalidCode));
        }

        // AD-2, AD-8: la firma se valida contra el ancla fijada ANTES de cualquier
        // escritura. Los bytes que se verifican son EXACTAMENTE los que el servidor
        // firmó: el texto canónico viaja tal cual y no se recompone aquí.
        byte[] publicKey;

        try
        {
            publicKey = Convert.FromBase64String(anchor.PublicKey);
        }
        catch (FormatException exception)
        {
            _logger.LogWarning(
                exception,
                "La clave pública del ancla de confianza fijada no es válida: el cliente no escribe nada en disco.");

            return new PackageValidationResult(
                PackageValidationOutcome.Untrusted,
                ErrorCatalog.Resolve(ErrorCatalog.ManifestSignatureInvalidCode));
        }

        if (!_verifier.Verify(envelope.Manifest, envelope.Signature, publicKey))
        {
            // AD-2, AD-8, AD-27: la firma NO valida contra el ancla fijada. El manifiesto
            // NO se persiste en la caché, no se crea archivo parcial y la operación no se
            // registra. El ancla fijada NO se invalida: esto es un manifiesto inválido, no
            // una ancla sustituida.
            _logger.LogWarning(
                "La firma del manifiesto del paquete {PackageId} NO valida contra el ancla de confianza fijada: el cliente no escribe nada en disco.",
                packageId);

            return new PackageValidationResult(
                PackageValidationOutcome.Untrusted,
                ErrorCatalog.Resolve(ErrorCatalog.ManifestSignatureInvalidCode));
        }

        // AD-8: la versión del esquema que declara el manifiesto tiene que ser una que el
        // cliente entienda. Un esquema desconocido se rechaza ANTES de cachear el
        // manifiesto y ANTES de escribir nada.
        if (!TryReadManifest(envelope.Manifest, out var manifest, out var schemaVersion))
        {
            _logger.LogWarning(
                "El manifiesto firmado del paquete {PackageId} no se puede leer: el cliente no escribe nada en disco.",
                packageId);

            return new PackageValidationResult(
                PackageValidationOutcome.Untrusted,
                ErrorCatalog.Resolve(ErrorCatalog.ManifestSignatureInvalidCode));
        }

        if (schemaVersion != SupportedManifestSchemaVersion)
        {
            _logger.LogWarning(
                "El manifiesto del paquete {PackageId} declara la versión de esquema {SchemaVersion}, que este cliente no soporta: el cliente no escribe nada en disco.",
                packageId,
                schemaVersion);

            return new PackageValidationResult(
                PackageValidationOutcome.UnsupportedSchema,
                ErrorCatalog.Resolve(ErrorCatalog.SchemaVersionNotSupportedCode));
        }

        // AD-2, AD-20: el manifiesto es de fiar, así que su texto canónico se cachea. El
        // manifiesto firmado es lo que la fase de validación de la integridad va a leer.
        await _cache
            .StoreAsync(packageId, envelope.Manifest, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "El manifiesto firmado del paquete {PackageId} es de fiar y su versión de esquema {SchemaVersion} está soportada.",
            packageId,
            schemaVersion);

        return new PackageValidationResult(
            PackageValidationOutcome.Valid,
            null,
            manifest!.Sha256,
            null);
    }

    /// <summary>
    /// Validates the INTEGRITY of the bytes of a package against the hash that the SIGNED
    /// manifest declares (AD-2, AD-8).
    ///
    /// <para>
    /// El hash esperado sale del manifiesto que la caché guarda, que es el texto que la
    /// firma cubrió: la correspondencia entre el manifiesto y el paquete está FIRMADA y no
    /// supuesta (AD-2).
    /// </para>
    /// </summary>
    /// <param name="packageId">Opaque identifier of the package that is validated.</param>
    /// <param name="content">Stream that holds the bytes of the package.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The result of the validation.</returns>
    public async Task<PackageValidationResult> ValidateIntegrityAsync(
        string packageId,
        Stream content,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentNullException.ThrowIfNull(content);

        var canonical = await _cache
            .LoadAsync(packageId, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(canonical))
        {
            // El manifiesto firmado no está en la caché, así que no hay contra qué
            // comparar el hash: la integridad NO se puede afirmar.
            _logger.LogWarning(
                "No hay manifiesto firmado en la caché del paquete {PackageId}: el cliente no puede verificar la integridad.",
                packageId);

            return new PackageValidationResult(
                PackageValidationOutcome.Untrusted,
                ErrorCatalog.Resolve(ErrorCatalog.ManifestSignatureInvalidCode));
        }

        if (!TryReadManifest(canonical, out var manifest, out _))
        {
            _logger.LogWarning(
                "El manifiesto firmado del paquete {PackageId} no se puede leer: el cliente no puede verificar la integridad.",
                packageId);

            return new PackageValidationResult(
                PackageValidationOutcome.Untrusted,
                ErrorCatalog.Resolve(ErrorCatalog.ManifestSignatureInvalidCode));
        }

        // AD-8: la INTEGRIDAD es una comprobación DISTINTA de la autenticidad. El hash se
        // calcula sobre los bytes reales y se compara contra el que declara el manifiesto
        // FIRMADO.
        var actual = await PackageIntegrity
            .ComputeSha256Async(content, cancellationToken)
            .ConfigureAwait(false);

        if (!PackageIntegrity.Matches(manifest!.Sha256, actual))
        {
            // AD-8, AD-27: la divergencia de integridad NO invalida el ancla ni el
            // manifiesto firmado: el paquete está DAÑADO. El parcial lo descarta el
            // coordinador y la operación no queda reparable por reanudación.
            _logger.LogWarning(
                "El sha256 calculado del paquete {PackageId} ({ActualSha256}) NO coincide con el del manifiesto firmado ({ExpectedSha256}): el paquete está dañado.",
                packageId,
                actual,
                manifest.Sha256);

            return new PackageValidationResult(
                PackageValidationOutcome.Corrupted,
                ErrorCatalog.Resolve(ErrorCatalog.PackageIntegrityMismatchCode),
                manifest.Sha256,
                actual);
        }

        _logger.LogInformation(
            "El paquete {PackageId} es íntegro: su sha256 coincide con el del manifiesto firmado.",
            packageId);

        return new PackageValidationResult(
            PackageValidationOutcome.Valid,
            null,
            manifest.Sha256,
            actual);
    }

    /// <summary>
    /// Reads the manifest that the signed canonical text carries.
    ///
    /// <para>
    /// El texto canónico se lee con el deserializador del contrato, así que la versión del
    /// esquema que se comprueba es la que el manifiesto DECLARA y no la que el servidor
    /// dice (AD-8).
    /// </para>
    /// </summary>
    /// <param name="canonicalManifest">Canonical JSON text of the manifest.</param>
    /// <param name="manifest">The manifest that was read.</param>
    /// <param name="schemaVersion">Version of the schema that the manifest declares.</param>
    /// <returns><c>true</c> when the manifest could be read.</returns>
    private static bool TryReadManifest(
        string canonicalManifest,
        out InstallationManifest? manifest,
        out int schemaVersion)
    {
        manifest = null;
        schemaVersion = 0;

        try
        {
            manifest = System.Text.Json.JsonSerializer.Deserialize<InstallationManifest>(
                canonicalManifest,
                ManifestJsonOptions);

            if (manifest is null)
            {
                return false;
            }

            schemaVersion = manifest.SchemaVersion;

            return true;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Options of the reading of the canonical manifest. The property names of the
    /// canonical text are in camel case, and the deserializer does not distinguish
    /// mayúsculas de minúsculas en el nombre de las propiedades (AD-2).
    /// </summary>
    private static readonly System.Text.Json.JsonSerializerOptions ManifestJsonOptions =
        new(System.Text.Json.JsonSerializerDefaults.Web);
}
