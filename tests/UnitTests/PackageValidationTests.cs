using System.Text;
using LANPartyHub.Client.Application.Integrity;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Domain.Trust;
using LANPartyHub.Server.Infrastructure.Signing;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The validation of a package: its authenticity and its integrity (AD-2, AD-8, AC1..AC6).
///
/// <para>
/// El caso de uso se ejercita con dobles de los PUERTOS, así que el orden de rechazo y la
/// AUSENCIA de escrituras son observables sin un servidor y sin un disco (AD-1, AD-16).
/// </para>
/// <para>
/// El nivel mínimo donde «ningún byte se escribe antes de validar» es observable es
/// justamente este (AD-16).
/// </para>
/// </summary>
public sealed class PackageValidationTests : IDisposable
{
    private const string PackageId = "paquete-1";

    private readonly List<string> _temporaryFolders = [];

    /// <summary>
    /// A manifest that is signed by the server validates, y su manifiesto queda cacheado
    /// (AD-2, AD-8).
    /// </summary>
    [Fact]
    public async Task A_manifest_signed_by_the_server_validates()
    {
        var signer = new Ed25519ManifestSigner();
        var manifest = BuildManifest(signer, "contenido del paquete");
        var canonical = BuildCanonical(manifest);

        var cache = new RecordingManifestCache();
        var manifests = new FixedManifestClient(new InstallationManifest.SignedEnvelope(
            canonical,
            signer.Sign(canonical),
            signer.GetKeyId()));

        var validation = CreateValidation(manifests, cache, signer);

        var result = await validation.ValidateManifestAsync(PackageId, TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
        Assert.Null(result.Error);

        // El manifiesto de fiar SE cachea: es lo que la fase de la integridad va a leer.
        Assert.Equal(canonical, Assert.Single(cache.StoredManifests));
    }

    /// <summary>
    /// A manifest whose signature does NOT validate against the fixed anchor is NOT
    /// TRUSTWORTHY, y NO deja ninguna escritura (AD-2, AD-8, AC4).
    /// </summary>
    [Fact]
    public async Task A_manifest_with_an_invalid_signature_is_not_trustworthy_and_writes_nothing()
    {
        var signer = new Ed25519ManifestSigner();
        var manifest = BuildManifest(signer, "contenido del paquete");
        var canonical = BuildCanonical(manifest);

        // La firma la produce OTRA clave: la del servidor de otro equipo.
        var otherSigner = new Ed25519ManifestSigner("otro-equipo-del-evento");

        var cache = new RecordingManifestCache();
        var manifests = new FixedManifestClient(new InstallationManifest.SignedEnvelope(
            canonical,
            otherSigner.Sign(canonical),
            otherSigner.GetKeyId()));

        var validation = CreateValidation(manifests, cache, signer);

        var result = await validation.ValidateManifestAsync(PackageId, TestContext.Current.CancellationToken);

        Assert.True(result.IsUntrusted);
        Assert.False(result.IsCorrupted);
        Assert.Equal(ErrorCatalog.ManifestSignatureInvalidCode, result.Error!.Code);

        // NINGUNA escritura en disco: ni el manifiesto en la caché, ni el parcial, ni la
        // operación registrada (AD-2, AD-8, AD-27).
        Assert.Empty(cache.StoredManifests);
    }

    /// <summary>
    /// A manifest signed by ANOTHER key is not trustworthy: el ancla fijada NO se invalida
    /// y el fallo es de autenticidad, no de corrupción (AD-27, AC6).
    /// </summary>
    [Fact]
    public async Task A_manifest_signed_by_another_key_is_not_trustworthy()
    {
        var anchorSigner = new Ed25519ManifestSigner();
        var otherSigner = new Ed25519ManifestSigner("otro-equipo-del-evento");

        var manifest = BuildManifest(otherSigner, "contenido del paquete");
        var canonical = BuildCanonical(manifest);

        var cache = new RecordingManifestCache();
        var manifests = new FixedManifestClient(new InstallationManifest.SignedEnvelope(
            canonical,
            otherSigner.Sign(canonical),
            otherSigner.GetKeyId()));

        var validation = CreateValidation(manifests, cache, anchorSigner);

        var result = await validation.ValidateManifestAsync(PackageId, TestContext.Current.CancellationToken);

        Assert.True(result.IsUntrusted);
        Assert.Equal(ErrorCatalog.ManifestSignatureInvalidCode, result.Error!.Code);
        Assert.Empty(cache.StoredManifests);
    }

    /// <summary>
    /// A manifest whose schema version the client does NOT support is rejected, and it
    /// writes NOTHING (AD-8, AC5).
    /// </summary>
    [Fact]
    public async Task A_manifest_with_an_unsupported_schema_version_is_rejected_and_writes_nothing()
    {
        var signer = new Ed25519ManifestSigner();
        var manifest = BuildManifest(signer, "contenido del paquete") with
        {
            SchemaVersion = ValidatePackage.SupportedManifestSchemaVersion + 1,
        };

        var canonical = BuildCanonical(manifest);

        var cache = new RecordingManifestCache();
        var manifests = new FixedManifestClient(new InstallationManifest.SignedEnvelope(
            canonical,
            signer.Sign(canonical),
            signer.GetKeyId()));

        var validation = CreateValidation(manifests, cache, signer);

        var result = await validation.ValidateManifestAsync(PackageId, TestContext.Current.CancellationToken);

        Assert.True(result.HasUnsupportedSchema);
        Assert.Equal(ErrorCatalog.SchemaVersionNotSupportedCode, result.Error!.Code);

        // El manifiesto con un esquema desconocido NO se cachea (AD-8).
        Assert.Empty(cache.StoredManifests);
    }

    /// <summary>
    /// A client that has NO fixed anchor cannot validate anything, and it writes nothing
    /// (AD-27).
    /// </summary>
    [Fact]
    public async Task A_client_without_a_fixed_anchor_cannot_validate_and_writes_nothing()
    {
        var signer = new Ed25519ManifestSigner();
        var manifest = BuildManifest(signer, "contenido del paquete");
        var canonical = BuildCanonical(manifest);

        var cache = new RecordingManifestCache();
        var manifests = new FixedManifestClient(new InstallationManifest.SignedEnvelope(
            canonical,
            signer.Sign(canonical),
            signer.GetKeyId()));

        var validation = CreateValidationWithoutAnchor(manifests, cache);

        var result = await validation.ValidateManifestAsync(PackageId, TestContext.Current.CancellationToken);

        Assert.True(result.IsUntrusted);
        Assert.Equal(ErrorCatalog.AnchorFingerprintMismatchCode, result.Error!.Code);
        Assert.Empty(cache.StoredManifests);
    }

    /// <summary>
    /// A package whose bytes DO match the hash of the signed manifest is valid (AD-2,
    /// AC1).
    /// </summary>
    [Fact]
    public async Task A_package_whose_hash_matches_the_signed_manifest_is_valid()
    {
        var content = Encoding.UTF8.GetBytes("contenido del paquete");
        var signer = new Ed25519ManifestSigner();
        var manifest = BuildManifest(signer, content);

        var cache = new RecordingManifestCache();
        cache.StoredManifests.Add(BuildCanonical(manifest));

        var validation = CreateValidation(
            new FixedManifestClient(null),
            cache,
            signer);

        await using var stream = new MemoryStream(content);

        var result = await validation.ValidateIntegrityAsync(PackageId, stream, TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
        Assert.Null(result.Error);
        Assert.Equal(manifest.Sha256, result.ActualSha256);
    }

    /// <summary>
    /// A package whose bytes were ALTERED during the transfer is CORRUPTED, and it is not
    /// the same failure as the lack of authenticity (AD-8, AC2, AC6).
    /// </summary>
    [Fact]
    public async Task A_package_whose_hash_does_not_match_is_corrupted()
    {
        var signed = Encoding.UTF8.GetBytes("contenido del paquete");
        var altered = Encoding.UTF8.GetBytes("contenido alterado");

        var signer = new Ed25519ManifestSigner();
        var manifest = BuildManifest(signer, signed);

        var cache = new RecordingManifestCache();
        cache.StoredManifests.Add(BuildCanonical(manifest));

        var validation = CreateValidation(new FixedManifestClient(null), cache, signer);

        await using var stream = new MemoryStream(altered);

        var result = await validation.ValidateIntegrityAsync(PackageId, stream, TestContext.Current.CancellationToken);

        // La corrupción y la falta de autenticidad son DOS fallos distintos, con dos
        // códigos estables distintos (AD-15, AD-25, AD-27).
        Assert.True(result.IsCorrupted);
        Assert.False(result.IsUntrusted);
        Assert.Equal(ErrorCatalog.PackageIntegrityMismatchCode, result.Error!.Code);

        // El hash esperado es el del manifiesto FIRMADO y el calculado es el de los bytes
        // alterados: el diagnóstico dice los dos (AC6).
        Assert.Equal(manifest.Sha256, result.ExpectedSha256);
        Assert.NotEqual(result.ExpectedSha256, result.ActualSha256);
    }

    /// <summary>
    /// The three failures are DISTINGUISHED by their code (AD-15, AD-25, AC6).
    /// </summary>
    [Fact]
    public void The_three_failures_are_distinguished_by_their_code()
    {
        Assert.NotEqual(
            ErrorCatalog.PackageIntegrityMismatchCode,
            ErrorCatalog.ManifestSignatureInvalidCode);

        Assert.NotEqual(
            ErrorCatalog.ManifestSignatureInvalidCode,
            ErrorCatalog.SchemaVersionNotSupportedCode);

        Assert.NotEqual(
            ErrorCatalog.PackageIntegrityMismatchCode,
            ErrorCatalog.SchemaVersionNotSupportedCode);
    }

    /// <summary>
    /// A package with NO signed manifest in the cache cannot have its integrity verified,
    /// and the result is not a valid one (AD-2).
    /// </summary>
    [Fact]
    public async Task A_package_without_a_cached_manifest_cannot_have_its_integrity_verified()
    {
        var signer = new Ed25519ManifestSigner();

        var cache = new RecordingManifestCache();
        var validation = CreateValidation(new FixedManifestClient(null), cache, signer);

        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("contenido"));

        var result = await validation.ValidateIntegrityAsync(PackageId, stream, TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
        Assert.Equal(ErrorCatalog.ManifestSignatureInvalidCode, result.Error!.Code);
    }

    /// <summary>
    /// The error of the failure is the one of the CATALOG, with its three parts: qué falló,
    /// por qué y qué hacer ahora (AD-15, AD-25).
    /// </summary>
    [Fact]
    public async Task The_error_of_the_failure_is_the_one_of_the_catalog()
    {
        var signer = new Ed25519ManifestSigner();
        var manifest = BuildManifest(signer, "contenido del paquete");
        var canonical = BuildCanonical(manifest);

        var otherSigner = new Ed25519ManifestSigner("otro-equipo-del-evento");

        var cache = new RecordingManifestCache();
        var manifests = new FixedManifestClient(new InstallationManifest.SignedEnvelope(
            canonical,
            otherSigner.Sign(canonical),
            otherSigner.GetKeyId()));

        var validation = CreateValidation(manifests, cache, signer);

        var result = await validation.ValidateManifestAsync(PackageId, TestContext.Current.CancellationToken);

        var expected = ErrorCatalog.Resolve(ErrorCatalog.ManifestSignatureInvalidCode);

        Assert.Equal(expected.Message, result.Error!.Message);
        Assert.Equal(expected.Cause, result.Error.Cause);
        Assert.Equal(expected.NextStep, result.Error.NextStep);
    }

    /// <summary>
    /// The REAL cache of the client writes the canonical manifest in the folder
    /// <c>manifests/</c> and reads it back (AD-9, AD-20).
    /// </summary>
    [Fact]
    public async Task The_cache_of_the_client_stores_the_canonical_manifest_in_its_folder()
    {
        var folder = CreateTemporaryFolder();

        var layout = LANPartyHub.Client.Infrastructure.Persistence.ClientDataLayout.Create(
            folder,
            Path.Combine(folder, "install"));

        layout.EnsureCreated();

        var cache = new LANPartyHub.Client.Infrastructure.Persistence.ManifestCache(layout);

        const string canonical = "{\"packageId\":\"paquete-1\",\"schemaVersion\":1}";

        await cache.StoreAsync(PackageId, canonical, TestContext.Current.CancellationToken);

        // El manifiesto vive en la carpeta de la caché y NO en la de las descargas ni en
        // la raíz de instalación (AD-9).
        var expected = Path.Combine(
            layout.ManifestsDirectory,
            PackageId + LANPartyHub.Client.Infrastructure.Persistence.ManifestCache.ManifestFileExtension);

        Assert.True(File.Exists(expected));
        Assert.Equal(canonical, await cache.LoadAsync(PackageId, TestContext.Current.CancellationToken));

        // Y el archivo temporal de la escritura atómica NO se queda atrás (AD-2).
        Assert.False(File.Exists(expected + ".tmp"));
    }

    /// <summary>
    /// The cache of the client reads <c>null</c> when it does not hold a manifest for the
    /// package (AD-2).
    /// </summary>
    [Fact]
    public async Task The_cache_of_the_client_reads_null_when_it_holds_no_manifest()
    {
        var folder = CreateTemporaryFolder();

        var layout = LANPartyHub.Client.Infrastructure.Persistence.ClientDataLayout.Create(
            folder,
            Path.Combine(folder, "install"));

        layout.EnsureCreated();

        var cache = new LANPartyHub.Client.Infrastructure.Persistence.ManifestCache(layout);

        Assert.Null(await cache.LoadAsync(PackageId, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Builds the use case of the validation over the doubles of the ports.
    /// </summary>
    /// <param name="manifests">Port of the read of the signed manifest.</param>
    /// <param name="cache">Port of the cache of the signed manifests.</param>
    /// <param name="signer">Signer that produces the anchor key of the client.</param>
    /// <param name="anchor">Fixed anchor, or <c>null</c> when the client has none.</param>
    /// <returns>The use case of the validation.</returns>
    private static ValidatePackage CreateValidation(
        ISignedManifestClient manifests,
        IManifestCache cache,
        Ed25519ManifestSigner signer,
        PinnedTrustAnchor? anchor = null)
    {
        // Un ancla NULA significa que el cliente NO tiene ninguna fijada, así que la
        // llamada que la pasa en nulo tiene que ser explícita y no el valor por defecto
        // del helper (AD-27).
        var effectiveAnchor = anchor ?? DefaultAnchor(signer);

        return new ValidatePackage(
            manifests,
            new LANPartyHub.Client.Infrastructure.Trust.Ed25519ManifestVerifier(),
            cache,
            new FixedTrustAnchorReader(effectiveAnchor),
            NullLogger<ValidatePackage>.Instance);
    }

    /// <summary>
    /// Builds a use case of the validation whose client has NO fixed anchor (AD-27).
    /// </summary>
    /// <param name="manifests">Port of the read of the signed manifest.</param>
    /// <param name="cache">Port of the cache of the signed manifests.</param>
    /// <returns>The use case of the validation.</returns>
    private static ValidatePackage CreateValidationWithoutAnchor(
        ISignedManifestClient manifests,
        IManifestCache cache) =>
        new(
            manifests,
            new LANPartyHub.Client.Infrastructure.Trust.Ed25519ManifestVerifier(),
            cache,
            new FixedTrustAnchorReader(null),
            NullLogger<ValidatePackage>.Instance);

    /// <summary>
    /// Builds the ancla de confianza que el cliente tiene fijada, con la clave del
    /// servidor del evento (AD-27).
    /// </summary>
    /// <param name="signer">Signer of the event.</param>
    /// <returns>The fixed anchor.</returns>
    private static PinnedTrustAnchor DefaultAnchor(Ed25519ManifestSigner signer) =>
        new(
            Convert.ToBase64String(Ed25519ManifestSigner.ComputePublicKey(Environment.MachineName)),
            "fingerprint",
            "2026-10-26T00:00:00.0000000Z");

    /// <summary>
    /// Builds a manifest that declares the hash of the given content.
    /// </summary>
    /// <param name="signer">Signer of the event.</param>
    /// <param name="content">Content that the manifest describes.</param>
    /// <returns>The manifest.</returns>
    private static InstallationManifest BuildManifest(Ed25519ManifestSigner signer, string content) =>
        BuildManifest(signer, Encoding.UTF8.GetBytes(content));

    /// <summary>
    /// Builds a manifest that declares the hash of the given bytes.
    /// </summary>
    /// <param name="signer">Signer of the event.</param>
    /// <param name="content">Bytes that the manifest describes.</param>
    /// <returns>The manifest.</returns>
    private static InstallationManifest BuildManifest(Ed25519ManifestSigner signer, byte[] content) =>
        new(
            ValidatePackage.SupportedManifestSchemaVersion,
            PackageId,
            "1.0.0",
            content.LongLength,
            LANPartyHub.Client.Domain.Integrity.PackageIntegrity.ComputeSha256(content),
            new InstallationManifest.LaunchSettings("g.exe", ["-novid"], "g"),
            []);

    /// <summary>
    /// Builds the canonical text of a manifest, in the same shape that the server signs
    /// (AD-2).
    /// </summary>
    /// <param name="manifest">Manifest to canonicalize.</param>
    /// <returns>The canonical JSON text of the manifest.</returns>
    private static string BuildCanonical(InstallationManifest manifest) =>
        LANPartyHub.Server.Domain.Packages.ManifestCanonicalizer.Canonicalize(manifest);

    /// <summary>
    /// Creates a temporary folder for the test.
    /// </summary>
    /// <returns>The absolute path of the folder.</returns>
    private string CreateTemporaryFolder()
    {
        var folder = Directory.CreateTempSubdirectory("lan-party-hub-validation-");

        _temporaryFolders.Add(folder.FullName);

        return folder.FullName;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var folder in _temporaryFolders)
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
                // A file that the operating system still holds does not fail the test.
            }
        }
    }

    /// <summary>
    /// Port of the read of the signed manifest that always answers the same envelope
    /// (AD-16).
    /// </summary>
    private sealed class FixedManifestClient : ISignedManifestClient
    {
        private readonly InstallationManifest.SignedEnvelope? _envelope;

        public FixedManifestClient(InstallationManifest.SignedEnvelope? envelope)
        {
            _envelope = envelope;
        }

        public Task<InstallationManifest.SignedEnvelope?> GetAsync(string packageId, CancellationToken cancellationToken) =>
            Task.FromResult(_envelope);
    }

    /// <summary>
    /// Port of the cache of the signed manifests that RECUERDA lo que se le escribió, para
    /// que la ausencia de escrituras sea observable (AD-16).
    /// </summary>
    private sealed class RecordingManifestCache : IManifestCache
    {
        public List<string> StoredManifests { get; } = [];

        public Task StoreAsync(
            string packageId,
            string canonicalManifest,
            CancellationToken cancellationToken)
        {
            StoredManifests.Add(canonicalManifest);

            return Task.CompletedTask;
        }

        public Task<string?> LoadAsync(string packageId, CancellationToken cancellationToken)
        {
            if (StoredManifests.Count == 0)
            {
                return Task.FromResult<string?>(null);
            }

            return Task.FromResult<string?>(StoredManifests[^1]);
        }
    }

    /// <summary>
    /// Port of the read of the ancla de confianza que siempre devuelve el mismo valor
    /// (AD-27).
    /// </summary>
    private sealed class FixedTrustAnchorReader : ITrustAnchorReader
    {
        private readonly PinnedTrustAnchor? _anchor;

        public FixedTrustAnchorReader(PinnedTrustAnchor? anchor)
        {
            _anchor = anchor;
        }

        public Task<PinnedTrustAnchor?> LoadTrustAnchorAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_anchor);
    }
}
