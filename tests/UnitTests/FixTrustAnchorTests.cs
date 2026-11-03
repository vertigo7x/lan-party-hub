using LANPartyHub.Client.Application.Trust;
using LANPartyHub.Client.Domain.Trust;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The fixation of the trust anchor outside of band (AC2, AC3, AC6, AD-27).
///
/// <para>
/// AD-27: el rechazo PRECEDE a persistir. Estos tests son el nivel mínimo donde «no se
/// persiste nada» es observable, porque el store es un doble que cuenta las escrituras
/// (AD-16, AD-27).
/// </para>
/// </summary>
public sealed class FixTrustAnchorTests
{
    [Fact]
    public async Task A_matching_fingerprint_fixes_the_anchor()
    {
        // AC3: cuando los fingerprints coinciden, el ancla queda fijada.
        var store = new RecordingClientStateStore();
        var useCase = CreateUseCase(store);

        var result = await useCase.ExecuteAsync(
            AnchorFingerprintTests.GoldenFingerprint,
            GoldenAnchor(),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFixed);
        Assert.Null(result.Error);
        Assert.Equal(AnchorFingerprintTests.GoldenFingerprint, result.FixedFingerprint);
    }

    [Fact]
    public async Task A_matching_fingerprint_persists_the_anchor()
    {
        var store = new RecordingClientStateStore();
        var useCase = CreateUseCase(store);

        await useCase.ExecuteAsync(
            AnchorFingerprintTests.GoldenFingerprint,
            GoldenAnchor(),
            TestContext.Current.CancellationToken);

        Assert.Equal(1, store.SaveCount);
        Assert.NotNull(store.SavedAnchor);
        Assert.Equal(AnchorFingerprintTests.GoldenFingerprint, store.SavedAnchor!.Fingerprint);
        Assert.Equal(AnchorFingerprintTests.GoldenPublicKeyBase64, store.SavedAnchor.PublicKey);
    }

    [Fact]
    public async Task A_mismatching_fingerprint_is_rejected()
    {
        // AC2: si NO coinciden, el cliente RECHAZA.
        var store = new RecordingClientStateStore();
        var useCase = CreateUseCase(store);

        var result = await useCase.ExecuteAsync(
            OtherFingerprint(),
            GoldenAnchor(),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsRejected);
        Assert.Equal(TrustAnchorOutcome.Rejected, result.Outcome);
    }

    [Fact]
    public async Task A_mismatching_fingerprint_writes_nothing()
    {
        // AC2, AD-27: el rechazo precede a persistir. CERO escrituras: ni el ancla, ni un
        // manifiesto, ni un archivo parcial, ni una operación registrada.
        var store = new RecordingClientStateStore();
        var useCase = CreateUseCase(store);

        await useCase.ExecuteAsync(
            OtherFingerprint(),
            GoldenAnchor(),
            TestContext.Current.CancellationToken);

        Assert.Equal(0, store.SaveCount);
        Assert.Null(store.SavedAnchor);
    }

    [Fact]
    public async Task A_mismatching_fingerprint_carries_the_anchor_mismatch_code()
    {
        // AC2, AD-27: el `code` del diagnóstico es el de «fingerprint de ancla no
        // coincide», DISTINTO del de «firma inválida».
        var store = new RecordingClientStateStore();
        var useCase = CreateUseCase(store);

        var result = await useCase.ExecuteAsync(
            OtherFingerprint(),
            GoldenAnchor(),
            TestContext.Current.CancellationToken);

        Assert.NotNull(result.Error);
        Assert.Equal(ErrorCatalog.AnchorFingerprintMismatchCode, result.Error!.Code);
        Assert.NotEqual(ErrorCatalog.ManifestSignatureInvalidCode, result.Error.Code);
    }

    [Fact]
    public async Task A_mismatching_fingerprint_shows_the_three_parts_of_the_error()
    {
        var store = new RecordingClientStateStore();
        var useCase = CreateUseCase(store);

        var result = await useCase.ExecuteAsync(
            OtherFingerprint(),
            GoldenAnchor(),
            TestContext.Current.CancellationToken);

        Assert.NotNull(result.Error);
        Assert.False(string.IsNullOrWhiteSpace(result.Error!.Message));
        Assert.False(string.IsNullOrWhiteSpace(result.Error.Cause));
        Assert.False(string.IsNullOrWhiteSpace(result.Error.NextStep));
        Assert.NotEqual(result.Error.Code, result.Error.Message);
    }

    [Fact]
    public async Task A_mismatching_fingerprint_reports_the_calculated_fingerprint()
    {
        // AC2: la superficie muestra el fingerprint calculado junto al esperado, para que
        // el jugador vea la diferencia.
        var store = new RecordingClientStateStore();
        var useCase = CreateUseCase(store);

        var result = await useCase.ExecuteAsync(
            OtherFingerprint(),
            GoldenAnchor(),
            TestContext.Current.CancellationToken);

        Assert.Equal(AnchorFingerprintTests.GoldenFingerprint, result.PresentedFingerprint);
        Assert.Null(result.FixedFingerprint);
    }

    [Fact]
    public async Task An_expected_fingerprint_that_is_not_a_fingerprint_is_rejected()
    {
        var store = new RecordingClientStateStore();
        var useCase = CreateUseCase(store);

        var result = await useCase.ExecuteAsync(
            "no-es-un-fingerprint",
            GoldenAnchor(),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsRejected);
        Assert.Equal(0, store.SaveCount);
        Assert.Equal(ErrorCatalog.AnchorFingerprintMismatchCode, result.Error!.Code);
    }

    [Fact]
    public async Task A_server_that_publishes_no_anchor_is_rejected()
    {
        var store = new RecordingClientStateStore();
        var useCase = CreateUseCase(store);

        var result = await useCase.ExecuteAsync(
            AnchorFingerprintTests.GoldenFingerprint,
            presentedAnchor: null,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsRejected);
        Assert.Equal(0, store.SaveCount);
        Assert.Equal(ErrorCatalog.AnchorFingerprintMismatchCode, result.Error!.Code);
    }

    [Fact]
    public async Task A_fixed_anchor_is_not_replaced_by_a_different_key()
    {
        // AC6, AD-27: un ancla fijada es INMUTABLE. Una clave distinta se rechaza y exige
        // un emparejamiento fuera de banda nuevo.
        var store = new RecordingClientStateStore
        {
            ExistingAnchor = new PinnedTrustAnchor(
                AnchorFingerprintTests.GoldenPublicKeyBase64,
                AnchorFingerprintTests.GoldenFingerprint,
                "2026-11-03T09:00:00.0000000Z"),
        };

        var useCase = CreateUseCase(store);

        var result = await useCase.ExecuteAsync(
            OtherFingerprint(),
            OtherAnchor(),
            TestContext.Current.CancellationToken);

        Assert.Equal(TrustAnchorOutcome.AlreadyFixed, result.Outcome);
        Assert.Equal(0, store.SaveCount);
        Assert.Equal(ErrorCatalog.AnchorFingerprintMismatchCode, result.Error!.Code);
    }

    [Fact]
    public async Task A_fixed_anchor_reports_both_fingerprints_when_it_does_not_match()
    {
        // AC6: la superficie muestra el fingerprint que presentó el servidor y el que ya
        // está fijado.
        var store = new RecordingClientStateStore
        {
            ExistingAnchor = new PinnedTrustAnchor(
                AnchorFingerprintTests.GoldenPublicKeyBase64,
                AnchorFingerprintTests.GoldenFingerprint,
                "2026-11-03T09:00:00.0000000Z"),
        };

        var useCase = CreateUseCase(store);

        var result = await useCase.ExecuteAsync(
            OtherFingerprint(),
            OtherAnchor(),
            TestContext.Current.CancellationToken);

        Assert.NotEqual(result.PresentedFingerprint, result.FixedFingerprint);
        Assert.Equal(AnchorFingerprintTests.GoldenFingerprint, result.FixedFingerprint);
    }

    [Fact]
    public async Task A_fixed_anchor_that_matches_is_not_written_again()
    {
        // AC3, AD-27: cuando el ancla fijada ES la que presenta el servidor, no se vuelve
        // a escribir: el cliente valida contra el valor ya persistido.
        var store = new RecordingClientStateStore
        {
            ExistingAnchor = new PinnedTrustAnchor(
                AnchorFingerprintTests.GoldenPublicKeyBase64,
                AnchorFingerprintTests.GoldenFingerprint,
                "2026-11-03T09:00:00.0000000Z"),
        };

        var useCase = CreateUseCase(store);

        var result = await useCase.ExecuteAsync(
            AnchorFingerprintTests.GoldenFingerprint,
            GoldenAnchor(),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFixed);
        Assert.Equal(0, store.SaveCount);
        Assert.Equal(AnchorFingerprintTests.GoldenFingerprint, result.FixedFingerprint);
    }

    [Fact]
    public async Task The_expected_fingerprint_is_normalized_before_the_comparison()
    {
        // AD-27: la comparación no puede depender de cómo lo escribió la persona.
        var store = new RecordingClientStateStore();
        var useCase = CreateUseCase(store);

        var grouped = "19bb 47ff 53da 69b4 b8bd 88d5 9d9b ef79 2e3f 5d40 e742 c9b6 0316 050d ad3a 5889";

        var result = await useCase.ExecuteAsync(
            grouped,
            GoldenAnchor(),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFixed);
        Assert.Equal(1, store.SaveCount);
    }

    private static TrustAnchor GoldenAnchor() =>
        new(
            AnchorFingerprintTests.GoldenPublicKeyBase64,
            AnchorFingerprintTests.GoldenFingerprint);

    private static TrustAnchor OtherAnchor() =>
        new(
            Convert.ToBase64String(new byte[32]),
            OtherFingerprint());

    private static string OtherFingerprint() =>
        new string('a', 64);

    private static FixTrustAnchor CreateUseCase(RecordingClientStateStore store) =>
        new(
            store,
            TimeProvider.System,
            NullLogger<FixTrustAnchor>.Instance);

    /// <summary>
    /// Doble de la base de datos del cliente que CUENTA las escrituras, para que «no se
    /// persiste nada» sea observable sin un disco (AD-16, AD-27).
    /// </summary>
    private sealed class RecordingClientStateStore : IClientStateStore
    {
        /// <summary>The ancla that is already fixed, when there is one.</summary>
        public PinnedTrustAnchor? ExistingAnchor { get; init; }

        /// <summary>Number of times that the ancla was written.</summary>
        public int SaveCount { get; private set; }

        /// <summary>The ancla that was written, when one was.</summary>
        public PinnedTrustAnchor? SavedAnchor { get; private set; }

        public Task MigrateAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<ClientIdentity?> LoadIdentityAsync(CancellationToken cancellationToken) =>
            Task.FromResult<ClientIdentity?>(null);

        public Task SaveIdentityAsync(ClientIdentity identity, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<PersistedDownloadOperation>> LoadDownloadOperationsAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PersistedDownloadOperation>>(Array.Empty<PersistedDownloadOperation>());

        public Task SaveProgressAsync(
            string operationId,
            long bytesPersisted,
            long? totalBytes,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<PinnedTrustAnchor?> LoadTrustAnchorAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ExistingAnchor);

        public Task SaveTrustAnchorAsync(PinnedTrustAnchor anchor, CancellationToken cancellationToken)
        {
            SaveCount++;
            SavedAnchor = anchor;

            return Task.CompletedTask;
        }
    }
}
