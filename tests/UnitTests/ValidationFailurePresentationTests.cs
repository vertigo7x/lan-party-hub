using System.Text;
using Avalonia.Media;
using LANPartyHub.Client.Application;
using LANPartyHub.Client.Application.Downloads;
using LANPartyHub.Client.Application.Integrity;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Domain;
using LANPartyHub.Client.Domain.StateMachines;
using LANPartyHub.Client.Domain.Trust;
using LANPartyHub.Client.Presentation.Design;
using LANPartyHub.Client.Presentation.Downloads;
using LANPartyHub.Client.Presentation.Errors;
using LANPartyHub.Server.Domain.Packages;
using LANPartyHub.Server.Infrastructure.Signing;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The presentation of a FAILED VALIDATION (AC1, AC2, AC3, AC4, UX-DR-6, UX-DR-33,
/// UX-DR-36, AD-8, AD-15, AD-25).
///
/// <para>
/// La corrupción y la falta de autenticidad son DOS fallos distintos, así que la interfaz
/// muestra DOS microcopias distintas: «Paquete dañado: el hash no coincide» NO es «Paquete
/// no confiable: no se pudo validar contra la raíz de confianza», y el alta de la
/// instalación queda BLOQUEADA con un motivo VISIBLE (AC1, AC2).
/// </para>
/// <para>
/// El error se presenta con sus TRES partes: qué falló, por qué y qué hacer ahora. El
/// código estable del catálogo es un identificador interno y NUNCA es el texto principal,
/// y un código desconocido se presenta con el texto genérico del catálogo (AC3, AC4).
/// </para>
/// </summary>
public sealed class ValidationFailurePresentationTests : IDisposable
{
    private const string PackageId = "paquete-1";

    private const string ContentUri = "http://192.168.1.10:5000/api/v1/packages/paquete-1/content";

    private readonly List<string> _temporaryFolders = [];

    /// <summary>
    /// A package whose bytes do NOT match the signed manifest is shown as DAMAGED, and the
    /// text is the literal of the experience spine (AC1, UX-DR-6).
    /// </summary>
    [Fact]
    public async Task A_corrupted_package_is_shown_as_damaged()
    {
        var handle = await DownloadAsync(CorruptedContent());

        await WaitUntilAsync(() => handle.StateMachine.Current == DownloadState.Failed);

        var viewModel = new DownloadProgressViewModel();

        viewModel.Apply(handle);

        Assert.True(viewModel.HasValidationFailure);
        Assert.Equal(
            "Paquete dañado: el hash no coincide",
            viewModel.ValidationStateText);
    }

    /// <summary>
    /// The state of the corrupted package carries the size of the package, as «Paquete
    /// dañado: el hash no coincide · 7,6 GB» (AC1, UX-DR-6, UX-DR-33).
    /// </summary>
    [Fact]
    public async Task The_state_of_the_corrupted_package_carries_the_size()
    {
        var handle = await DownloadAsync(CorruptedContent(), totalBytes: 7_600_000_000);

        await WaitUntilAsync(() => handle.StateMachine.Current == DownloadState.Failed);

        var viewModel = new DownloadProgressViewModel();

        viewModel.Apply(handle);

        Assert.Equal(
            "Paquete dañado: el hash no coincide · 7,6 GB",
            viewModel.StateTextWithSize);
    }

    /// <summary>
    /// A package that could NOT be validated against the root of trust is shown as NOT
    /// TRUSTWORTHY, and the text is the literal of the experience spine (AC2, UX-DR-6).
    /// </summary>
    [Fact]
    public async Task A_package_that_is_not_trustworthy_is_shown_as_untrusted()
    {
        var handle = await DownloadAsync(UntrustedContent(), mode: ValidationMode.Untrusted);

        await WaitUntilAsync(() => handle.StateMachine.Current == DownloadState.Failed);

        var viewModel = new DownloadProgressViewModel();

        viewModel.Apply(handle);

        Assert.True(viewModel.HasValidationFailure);
        Assert.Equal(
            "Paquete no confiable: no se pudo validar contra la raíz de confianza",
            viewModel.ValidationStateText);
    }

    /// <summary>
    /// The state of the untrusted package carries the size of the package, as «Paquete no
    /// confiable: no se pudo validar contra la raíz de confianza · 7,6 GB» (AC2, UX-DR-6).
    /// </summary>
    [Fact]
    public async Task The_state_of_the_untrusted_package_carries_the_size()
    {
        var handle = await DownloadAsync(UntrustedContent(), totalBytes: 7_600_000_000, mode: ValidationMode.Untrusted);

        await WaitUntilAsync(() => handle.StateMachine.Current == DownloadState.Failed);

        var viewModel = new DownloadProgressViewModel();

        viewModel.Apply(handle);

        Assert.Equal(
            "Paquete no confiable: no se pudo validar contra la raíz de confianza · 7,6 GB",
            viewModel.StateTextWithSize);
    }

    /// <summary>
    /// The two failures have DIFFERENT texts and DIFFERENT causes: el jugador tiene que
    /// poder saber si el problema es la red o la confianza (AC1, AC2).
    /// </summary>
    [Fact]
    public async Task The_two_failures_have_different_texts_and_different_causes()
    {
        var corrupted = await DownloadAsync(CorruptedContent());
        var untrusted = await DownloadAsync(UntrustedContent(), mode: ValidationMode.Untrusted);

        await WaitUntilAsync(() => corrupted.StateMachine.Current == DownloadState.Failed);
        await WaitUntilAsync(() => untrusted.StateMachine.Current == DownloadState.Failed);

        var corruptedViewModel = new DownloadProgressViewModel();
        var untrustedViewModel = new DownloadProgressViewModel();

        corruptedViewModel.Apply(corrupted);
        untrustedViewModel.Apply(untrusted);

        Assert.NotEqual(corruptedViewModel.ValidationStateText, untrustedViewModel.ValidationStateText);

        var corruptedError = corruptedViewModel.Error!;
        var untrustedError = untrustedViewModel.Error!;

        Assert.NotEqual(corruptedError.Message, untrustedError.Message);
        Assert.NotEqual(corruptedError.Cause, untrustedError.Cause);
    }

    /// <summary>
    /// The alta de la instalación is BLOCKED when the validation failed, and the reason is
    /// VISIBLE, not hidden in the log (AC1, UX-DR-33).
    /// </summary>
    [Fact]
    public async Task The_installation_registration_is_blocked_with_a_visible_reason()
    {
        var handle = await DownloadAsync(CorruptedContent());

        await WaitUntilAsync(() => handle.StateMachine.Current == DownloadState.Failed);

        var viewModel = new DownloadProgressViewModel();

        viewModel.Apply(handle);

        Assert.True(viewModel.IsInstallBlocked);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.InstallBlockedReason));
        Assert.Equal(
            ErrorCatalog.Resolve(ErrorCatalog.PackageIntegrityMismatchCode).Message,
            viewModel.InstallBlockedReason);
    }

    /// <summary>
    /// The gate of the alta lives in the APPLICATION layer, así que la vista no inventa la
    /// regla: la puerta dice lo mismo que la interfaz muestra (AC1, AD-8).
    /// </summary>
    [Fact]
    public void The_install_eligibility_blocks_the_registration_with_its_reason()
    {
        var error = ErrorCatalog.Resolve(ErrorCatalog.ManifestSignatureInvalidCode);

        var eligibility = new PackageInstallEligibility(error);

        Assert.True(eligibility.IsBlocked);
        Assert.Equal(error.Message, eligibility.BlockedReason);
        Assert.Equal(error, eligibility.Error);
    }

    /// <summary>
    /// The alta de la instalación is NOT blocked when the validation passed (AC1).
    /// </summary>
    [Fact]
    public void The_install_eligibility_does_not_block_a_valid_package()
    {
        var eligibility = new PackageInstallEligibility((ApiError?)null);

        Assert.False(eligibility.IsBlocked);
        Assert.Null(eligibility.BlockedReason);
        Assert.Null(eligibility.Error);
    }

    /// <summary>
    /// The gate of the alta is built from the result of the validation, así que el motivo
    /// del bloqueo es el del catálogo (AC1, AC3).
    /// </summary>
    [Fact]
    public void The_install_eligibility_is_built_from_the_result_of_the_validation()
    {
        var error = ErrorCatalog.Resolve(ErrorCatalog.PackageIntegrityMismatchCode);

        var eligibility = new PackageInstallEligibility(
            new PackageValidationResult(PackageValidationOutcome.Corrupted, error));

        Assert.True(eligibility.IsBlocked);
        Assert.Equal(error.Message, eligibility.BlockedReason);
    }

    /// <summary>
    /// The error of the validation is presented with its THREE parts: qué falló, por qué y
    /// qué hacer ahora (AC3, UX-DR-36).
    /// </summary>
    [Fact]
    public async Task The_error_of_the_validation_has_three_parts()
    {
        var handle = await DownloadAsync(CorruptedContent());

        await WaitUntilAsync(() => handle.StateMachine.Current == DownloadState.Failed);

        var viewModel = new DownloadProgressViewModel();

        viewModel.Apply(handle);

        var error = viewModel.Error!;

        Assert.False(string.IsNullOrWhiteSpace(error.Message));
        Assert.False(string.IsNullOrWhiteSpace(error.Cause));
        Assert.False(string.IsNullOrWhiteSpace(error.NextStep));

        // Las tres partes salen del catálogo de errores, así que no se reescriben (AD-15).
        var catalog = ErrorCatalog.Resolve(ErrorCatalog.PackageIntegrityMismatchCode);

        Assert.Equal(catalog.Message, error.Message);
        Assert.Equal(catalog.Cause, error.Cause);
        Assert.Equal(catalog.NextStep, error.NextStep);
    }

    /// <summary>
    /// The code of the catalog is an INTERNAL identifier: NUNCA es el texto principal
    /// (AC3, AC4, AD-15).
    /// </summary>
    [Fact]
    public async Task The_code_of_the_catalog_is_never_the_primary_text()
    {
        var handle = await DownloadAsync(CorruptedContent());

        await WaitUntilAsync(() => handle.StateMachine.Current == DownloadState.Failed);

        var viewModel = new DownloadProgressViewModel();

        viewModel.Apply(handle);

        Assert.Equal(ErrorCatalog.PackageIntegrityMismatchCode, viewModel.Error!.Code);

        Assert.DoesNotContain(
            ErrorCatalog.PackageIntegrityMismatchCode,
            viewModel.Error.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            ErrorCatalog.PackageIntegrityMismatchCode,
            viewModel.ValidationStateText,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// An UNKNOWN code is presented with the generic text of the catalog, and the code
    /// NEVER reaches the interface as the primary text (AC4, AD-15).
    /// </summary>
    [Fact]
    public void An_unknown_code_is_presented_with_the_generic_text_of_the_catalog()
    {
        const string unknownCode = "CODE_THAT_THE_CATALOG_DOES_NOT_KNOW";

        var viewModel = new ErrorMessageViewModel(unknownCode);

        Assert.Equal(ErrorCatalog.Resolve(unknownCode).Message, viewModel.Message);
        Assert.DoesNotContain(unknownCode, viewModel.Message, StringComparison.Ordinal);
        Assert.Equal(unknownCode, viewModel.Code);
    }

    /// <summary>
    /// The notice of error shows the three parts of the error, así que el jugador sabe
    /// qué falló, por qué y qué hacer ahora (AC3, UX-DR-36).
    /// </summary>
    [Fact]
    public void The_error_notice_shows_the_three_parts()
    {
        var error = ErrorCatalog.Resolve(ErrorCatalog.PackageIntegrityMismatchCode);

        var toast = new ErrorToast(new ErrorMessageViewModel(error));

        toast.Show(new ErrorMessageViewModel(error));

        Assert.Equal(error.Message, toast.MessageText.Text);
        Assert.Equal(error.Cause, toast.CauseText.Text);
        Assert.Equal(error.NextStep, toast.NextStepText.Text);

        // El aviso lleva la superficie del peligro y la tinta de la superficie base
        // (DESIGN).
        Assert.Equal(DesignTokens.StateDanger, Assert.IsType<SolidColorBrush>(toast.Background).Color);
        Assert.True(toast.IsVisible);
    }

    /// <summary>
    /// The state of a failed validation is painted with the color of the danger (AC1,
    /// AC2, UX-DR-33).
    /// </summary>
    [Fact]
    public async Task The_state_of_the_failed_validation_uses_the_color_of_the_danger()
    {
        var handle = await DownloadAsync(CorruptedContent(), totalBytes: 7_600_000_000);

        await WaitUntilAsync(() => handle.StateMachine.Current == DownloadState.Failed);

        var viewModel = new DownloadProgressViewModel();

        viewModel.Apply(handle);

        var control = new DownloadProgressControl(viewModel);

        // El estado de la validación fallida es el DIFERENCIADO, con el tamaño del
        // paquete al final (AC1, UX-DR-6, UX-DR-33).
        Assert.Equal(
            "Paquete dañado: el hash no coincide · 7,6 GB",
            control.StateText.Text);

        Assert.Equal(
            DesignTokens.StateDanger,
            Assert.IsType<SolidColorBrush>(control.StateText.Foreground).Color);
    }

    /// <summary>
    /// The notice of error of the control carries the three parts, and the reason of the
    /// block del alta es VISIBLE (AC1, AC3, UX-DR-33).
    /// </summary>
    [Fact]
    public async Task The_control_shows_the_error_and_the_blocked_reason()
    {
        var handle = await DownloadAsync(CorruptedContent());

        await WaitUntilAsync(() => handle.StateMachine.Current == DownloadState.Failed);

        var viewModel = new DownloadProgressViewModel();

        viewModel.Apply(handle);

        var control = new DownloadProgressControl(viewModel);

        var error = viewModel.Error!;

        Assert.True(control.ErrorText.IsVisible);

        // El aviso de error lleva las TRES partes: qué falló, por qué y qué hacer ahora
        // (AC3, UX-DR-36).
        Assert.Contains(error.Message, control.ErrorText.Text, StringComparison.Ordinal);
        Assert.Contains(error.Cause, control.ErrorText.Text, StringComparison.Ordinal);
        Assert.Contains(error.NextStep, control.ErrorText.Text, StringComparison.Ordinal);

        Assert.True(control.BlockedReasonText.IsVisible);
        Assert.False(string.IsNullOrWhiteSpace(control.BlockedReasonText.Text));
    }

    /// <summary>
    /// An operation that did NOT fail its validation shows NO error and NO block del alta
    /// (AC1, AC3).
    /// </summary>
    [Fact]
    public async Task A_valid_operation_shows_no_error_and_no_block()
    {
        var handle = await DownloadAsync(ValidContent(), mode: ValidationMode.Valid);

        await WaitUntilAsync(() => handle.StateMachine.Current == DownloadState.Completed);

        var viewModel = new DownloadProgressViewModel();

        viewModel.Apply(handle);

        var control = new DownloadProgressControl(viewModel);

        Assert.False(viewModel.HasValidationFailure);
        Assert.Null(viewModel.Error);
        Assert.False(viewModel.IsInstallBlocked);
        Assert.False(control.ErrorText.IsVisible);
        Assert.False(control.BlockedReasonText.IsVisible);
    }

    /// <summary>
    /// Runs a download whose validation FAILS, and returns its handle (AD-8).
    /// </summary>
    /// <param name="content">Content that the bytes of the download carry.</param>
    /// <param name="totalBytes">Total size that the transfer declares.</param>
    /// <param name="mode">How the validation has to end.</param>
    /// <returns>The handle of the operation.</returns>
    private async Task<DownloadOperationHandle> DownloadAsync(
        string content,
        long totalBytes = 1_000,
        ValidationMode mode = ValidationMode.Corrupted)
    {
        var bytes = Encoding.UTF8.GetBytes(content);

        var coordinator = CreateCoordinator(
            new RecordingStateStore(),
            new RecordingTransferEngine(totalBytes, bytes),
            CreateValidation(content, mode));

        var handle = await coordinator.StartAsync(
            PackageId,
            new Uri(ContentUri),
            TestContext.Current.CancellationToken);

        return handle;
    }

    /// <summary>
    /// The content whose bytes do NOT match the hash of the signed manifest, así que el
    /// paquete está DAÑADO (AD-8).
    /// </summary>
    private static string CorruptedContent() => "el contenido alterado del paquete";

    /// <summary>
    /// The content whose bytes DO match the hash of the signed manifest, pero la
    /// autenticidad NO valida contra el ancla fijada, así que el paquete NO es de fiar
    /// (AD-2, AD-8).
    /// </summary>
    private static string UntrustedContent() => "el contenido del paquete";

    /// <summary>
    /// The content whose bytes DO match the hash of the signed manifest, así que el
    /// paquete es íntegro y de fiar (AD-8).
    /// </summary>
    private static string ValidContent() => "el contenido íntegro del paquete";

    /// <summary>
    /// How the validation of the package ends (AD-8).
    /// </summary>
    private enum ValidationMode
    {
        /// <summary>
        /// El manifiesto firmado NO llega a la caché, así que no hay contra qué validar la
        /// integridad y el paquete NO es de fiar (AD-2, AD-8).
        /// </summary>
        Untrusted,

        /// <summary>La firma valida y el hash NO coincide (AD-8).</summary>
        Corrupted,

        /// <summary>La firma valida y el hash coincide (AD-8).</summary>
        Valid,
    }

    /// <summary>
    /// Builds the use case of the validation over the doubles of the ports, con un
    /// manifiesto firmado por la clave del ancla fijada (AD-2, AD-8).
    /// </summary>
    /// <param name="content">Content whose bytes the download carries.</param>
    /// <param name="mode">How the validation has to end.</param>
    /// <returns>The use case of the validation.</returns>
    private static ValidatePackage CreateValidation(string content, ValidationMode mode)
    {
        var signer = new Ed25519ManifestSigner();

        var bytes = Encoding.UTF8.GetBytes(content);

        // El hash declarado por el manifiesto firmado es el del contenido REAL en el
        // paquete válido, y OTRO en el corrupto: así el hash no coincide y el paquete
        // está dañado (AD-8).
        var declaredSha256 = mode == ValidationMode.Corrupted
            ? new string('0', 64)
            : LANPartyHub.Client.Domain.Integrity.PackageIntegrity.ComputeSha256(bytes);

        var manifest = new InstallationManifest(
            ValidatePackage.SupportedManifestSchemaVersion,
            PackageId,
            "1.0.0",
            bytes.LongLength,
            declaredSha256,
            new InstallationManifest.LaunchSettings("g.exe", ["-novid"], "g"),
            []);

        var canonical = ManifestCanonicalizer.Canonicalize(manifest);

        // El manifiesto se firma con la clave del ancla fijada, así que la autenticidad
        // VALIDA (AD-2).
        var envelope = new InstallationManifest.SignedEnvelope(
            canonical,
            signer.Sign(canonical),
            signer.GetKeyId());

        var anchor = new PinnedTrustAnchor(
            Convert.ToBase64String(Ed25519ManifestSigner.ComputePublicKey(Environment.MachineName)),
            "fingerprint",
            "2026-10-26T00:00:00.0000000Z");

        return new ValidatePackage(
            new FixedManifestClient(envelope),
            new LANPartyHub.Client.Infrastructure.Trust.Ed25519ManifestVerifier(),
            new FixedManifestCache(canonical, keepOnLoad: mode != ValidationMode.Untrusted),
            new FixedTrustAnchorReader(anchor),
            NullLogger<ValidatePackage>.Instance);
    }

    /// <summary>
    /// Builds the coordinator of the downloads over the doubles of the ports (AD-1, AD-16).
    /// </summary>
    /// <param name="store">Port of the persistence of the state.</param>
    /// <param name="engine">Port of the transfer of the bytes.</param>
    /// <param name="validation">Use case of the validation of the package.</param>
    /// <returns>The coordinator of the downloads.</returns>
    private DownloadCoordinator CreateCoordinator(
        IClientStateStore store,
        ITransferEngine engine,
        ValidatePackage validation) =>
        new(
            engine,
            new RecordingIntentClient(),
            store,
            new TemporaryDataLayout(_temporaryFolders),
            NullLogger<DownloadCoordinator>.Instance,
            validation);

    /// <summary>
    /// Waits until the condition holds, so the test no tiene que dormir un tiempo fijo
    /// (AD-16).
    /// </summary>
    /// <param name="condition">Condition that has to become true.</param>
    /// <returns>A task that completes when the condition holds.</returns>
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);

        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("La condición no se cumplió dentro del plazo.");
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
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
    /// Engine que ESCRIBE los bytes del contenido en el destino (AD-16).
    /// </summary>
    private sealed class RecordingTransferEngine : ITransferEngine
    {
        private readonly long _totalBytes;
        private readonly byte[] _content;

        public RecordingTransferEngine(long totalBytes, byte[] content)
        {
            _totalBytes = totalBytes;
            _content = content;
        }

        public async Task<TransferOutcome> TransferAsync(
            Uri contentUri,
            long startInclusive,
            long endInclusive,
            Stream destination,
            CancellationToken cancellationToken)
        {
            await destination.WriteAsync(_content.AsMemory(), cancellationToken).ConfigureAwait(false);

            return new TransferOutcome(_content.LongLength, _totalBytes, RangeHonoured: true);
        }
    }

    /// <summary>
    /// Adapter of the registration of the intention que devuelve un identificador fijo
    /// (AC1).
    /// </summary>
    private sealed class RecordingIntentClient : IDownloadIntentClient
    {
        public Task<string> RegisterAsync(string packageId, CancellationToken cancellationToken) =>
            Task.FromResult("operacion-1");
    }

    /// <summary>
    /// Port of the cache of the signed manifests (AD-2, AD-16).
    ///
    /// <para>
    /// La caché puede NO devolver el manifiesto firmado, así que la fase de la integridad
    /// no tiene contra qué validar el hash y el paquete NO es de fiar (AD-2, AD-8).
    /// </para>
    /// </summary>
    private sealed class FixedManifestCache : IManifestCache
    {
        private readonly string _canonical;
        private readonly bool _keepOnLoad;

        public FixedManifestCache(string canonical, bool keepOnLoad)
        {
            _canonical = canonical;
            _keepOnLoad = keepOnLoad;
        }

        public Task StoreAsync(
            string packageId,
            string canonicalManifest,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<string?> LoadAsync(string packageId, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(_keepOnLoad ? _canonical : null);
    }

    /// <summary>
    /// Port of the read of the signed manifest (AD-16).
    /// </summary>
    private sealed class FixedManifestClient : ISignedManifestClient
    {
        private readonly InstallationManifest.SignedEnvelope _envelope;

        public FixedManifestClient(InstallationManifest.SignedEnvelope envelope) => _envelope = envelope;

        public Task<InstallationManifest.SignedEnvelope?> GetAsync(string packageId, CancellationToken cancellationToken) =>
            Task.FromResult<InstallationManifest.SignedEnvelope?>(_envelope);
    }

    /// <summary>
    /// Port of the read of la ancla de confianza que siempre devuelve el mismo valor
    /// (AD-27).
    /// </summary>
    private sealed class FixedTrustAnchorReader : ITrustAnchorReader
    {
        private readonly PinnedTrustAnchor _anchor;

        public FixedTrustAnchorReader(PinnedTrustAnchor anchor) => _anchor = anchor;

        public Task<PinnedTrustAnchor?> LoadTrustAnchorAsync(CancellationToken cancellationToken) =>
            Task.FromResult<PinnedTrustAnchor?>(_anchor);
    }

    /// <summary>
    /// Store of the client que NO escribe nada, porque a esta historia no le importa la
    /// persistencia (AD-16).
    /// </summary>
    private sealed class RecordingStateStore : IClientStateStore
    {
        public Task MigrateAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<ClientIdentity?> LoadIdentityAsync(CancellationToken cancellationToken) =>
            Task.FromResult<ClientIdentity?>(ClientIdentity.Emit());

        public Task SaveIdentityAsync(ClientIdentity identity, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<PersistedDownloadOperation>> LoadDownloadOperationsAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PersistedDownloadOperation>>([]);

        public Task SaveProgressAsync(
            string operationId,
            long bytesPersisted,
            long? totalBytes,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task SaveOperationAsync(
            string operationId,
            string packageId,
            string state,
            string transferEngine,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<PinnedTrustAnchor?> LoadTrustAnchorAsync(CancellationToken cancellationToken) =>
            Task.FromResult<PinnedTrustAnchor?>(null);

        public Task SaveTrustAnchorAsync(PinnedTrustAnchor anchor, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task SaveInstallationAsync(ClientInstallation installation, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<ClientInstallation?> LoadInstallationAsync(string gameId, CancellationToken cancellationToken) =>
            Task.FromResult<ClientInstallation?>(null);
    }

    /// <summary>
    /// Layout of a folder temporal, para que el parcial de la descarga viva solo en la
    /// carpeta de las descargas (AD-9).
    /// </summary>
    private sealed class TemporaryDataLayout : IClientDataLayout
    {
        private readonly string _folder;

        public TemporaryDataLayout(List<string> temporaryFolders)
        {
            _folder = Directory.CreateTempSubdirectory("lan-party-hub-validation-presentation-").FullName;

            temporaryFolders.Add(_folder);

            Directory.CreateDirectory(DownloadsDirectory);
        }

        public string Folder => _folder;

        public string InstallDirectory => Path.Combine(_folder, "install");

        public string DownloadsDirectory => Path.Combine(_folder, "downloads");

        public string ManifestsDirectory => Path.Combine(_folder, "manifests");

        public string StateDatabasePath => Path.Combine(_folder, "state.sqlite");

        public string PartialFilePath(string packageId) =>
            Path.Combine(DownloadsDirectory, packageId + ".part");

        public void EnsureCreated() => Directory.CreateDirectory(DownloadsDirectory);

        public void SetInstallDirectory(string installDirectory)
        {
        }
    }
}
