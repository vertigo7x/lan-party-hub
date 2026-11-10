using LANPartyHub.Server.Application.Clients;
using LANPartyHub.Server.Application.Ports;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The registration of the error that a client reports (AD-15, AD-18, AD-22, FR-10).
///
/// <para>
/// El identificador del error es ESTABLE y se DERIVA del reporte: el mismo error
/// reportado dos veces produce el MISMO identificador, así que la escritura es
/// idempotente y una republicación del cliente NO duplica el diagnóstico que el
/// organizador ve (AD-15, AD-22).
/// </para>
/// </summary>
public sealed class ReportClientErrorTests
{
    private const string ClientId = "0193f2a1b2c34d4e5f60718293a4b5c6";
    private const string OtherClientId = "0193f2a1b2c34d4e5f60718293a4b5c7";
    private const string OperationId = "0193f2a1-b2c3-4d4e-5f60-718293a4b5c8";
    private const string OtherOperationId = "0193f2a1-b2c3-4d4e-5f60-718293a4b5c9";

    [Fact]
    public async Task The_error_that_the_client_reports_is_registered_with_its_three_parts()
    {
        var errors = new RecordingErrors();
        var useCase = new ReportClientError(
            new RegisteredRegistry(ClientId),
            errors,
            NullLogger<ReportClientError>.Instance);

        var error = ErrorCatalog.Resolve(ErrorCatalog.PackageIntegrityMismatchCode);

        await useCase.ExecuteAsync(
            new ClientErrorReported(ClientId, OperationId, error),
            TestContext.Current.CancellationToken);

        var registered = Assert.Single(errors.Writes);

        Assert.Equal(ClientId, registered.ClientId);
        Assert.Equal(OperationId, registered.OperationId);
        Assert.Equal(ErrorCatalog.PackageIntegrityMismatchCode, registered.Code);
        Assert.Equal(error.Message, registered.Message);
        Assert.Equal(error.Cause, registered.Cause);
        Assert.Equal(error.NextStep, registered.NextStep);
        Assert.False(string.IsNullOrWhiteSpace(registered.ErrorId));
    }

    [Fact]
    public async Task The_identifier_of_the_error_is_the_same_when_the_client_republishes_it()
    {
        var errors = new RecordingErrors();
        var useCase = new ReportClientError(
            new RegisteredRegistry(ClientId),
            errors,
            NullLogger<ReportClientError>.Instance);

        var error = ErrorCatalog.Resolve(ErrorCatalog.PackageIntegrityMismatchCode);

        await useCase.ExecuteAsync(
            new ClientErrorReported(ClientId, OperationId, error),
            TestContext.Current.CancellationToken);

        await useCase.ExecuteAsync(
            new ClientErrorReported(ClientId, OperationId, error),
            TestContext.Current.CancellationToken);

        await useCase.ExecuteAsync(
            new ClientErrorReported(ClientId, OperationId, error),
            TestContext.Current.CancellationToken);

        // AD-15, AD-22: el identificador NO cambia entre republicaciones, así que la
        // escritura idempotente por `errorId` puede deduplicar la fila.
        var identifiers = errors.Writes
            .Select(write => write.ErrorId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.Single(identifiers);
    }

    [Fact]
    public async Task The_identifier_of_the_error_is_different_for_a_different_operation()
    {
        var errors = new RecordingErrors();
        var useCase = new ReportClientError(
            new RegisteredRegistry(ClientId),
            errors,
            NullLogger<ReportClientError>.Instance);

        var error = ErrorCatalog.Resolve(ErrorCatalog.PackageIntegrityMismatchCode);

        await useCase.ExecuteAsync(
            new ClientErrorReported(ClientId, OperationId, error),
            TestContext.Current.CancellationToken);

        await useCase.ExecuteAsync(
            new ClientErrorReported(ClientId, OtherOperationId, error),
            TestContext.Current.CancellationToken);

        // Dos operaciones DISTINTAS del mismo equipo son dos diagnósticos distintos.
        Assert.Equal(2, errors.Writes.Select(write => write.ErrorId).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task The_identifier_of_the_error_is_different_for_a_different_client()
    {
        var errors = new RecordingErrors();
        var useCase = new ReportClientError(
            new RegisteredRegistry(ClientId, OtherClientId),
            errors,
            NullLogger<ReportClientError>.Instance);

        var error = ErrorCatalog.Resolve(ErrorCatalog.PackageIntegrityMismatchCode);

        await useCase.ExecuteAsync(
            new ClientErrorReported(ClientId, OperationId, error),
            TestContext.Current.CancellationToken);

        await useCase.ExecuteAsync(
            new ClientErrorReported(OtherClientId, OperationId, error),
            TestContext.Current.CancellationToken);

        // El error de un equipo NO es el error de otro equipo.
        Assert.Equal(2, errors.Writes.Select(write => write.ErrorId).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task The_identifier_of_the_error_is_different_for_a_different_code()
    {
        var errors = new RecordingErrors();
        var useCase = new ReportClientError(
            new RegisteredRegistry(ClientId),
            errors,
            NullLogger<ReportClientError>.Instance);

        await useCase.ExecuteAsync(
            new ClientErrorReported(
                ClientId,
                OperationId,
                ErrorCatalog.Resolve(ErrorCatalog.PackageIntegrityMismatchCode)),
            TestContext.Current.CancellationToken);

        await useCase.ExecuteAsync(
            new ClientErrorReported(
                ClientId,
                OperationId,
                ErrorCatalog.Resolve(ErrorCatalog.ManifestSignatureInvalidCode)),
            TestContext.Current.CancellationToken);

        // Dos códigos del catálogo son dos diagnósticos distintos.
        Assert.Equal(2, errors.Writes.Select(write => write.ErrorId).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task The_error_of_a_client_that_is_not_registered_creates_no_row()
    {
        var errors = new RecordingErrors();
        var useCase = new ReportClientError(
            new RegisteredRegistry(),
            errors,
            NullLogger<ReportClientError>.Instance);

        var error = ErrorCatalog.Resolve(ErrorCatalog.PackageIntegrityMismatchCode);

        await useCase.ExecuteAsync(
            new ClientErrorReported(ClientId, OperationId, error),
            TestContext.Current.CancellationToken);

        // AD-30, AD-32: el alta del equipo es la operación de REGISTRO, así que un error
        // de un cliente que no está registrado no crea ninguna fila.
        Assert.Empty(errors.Writes);
    }

    [Fact]
    public async Task An_error_without_its_three_parts_creates_no_row()
    {
        var errors = new RecordingErrors();
        var useCase = new ReportClientError(
            new RegisteredRegistry(ClientId),
            errors,
            NullLogger<ReportClientError>.Instance);

        await useCase.ExecuteAsync(
            new ClientErrorReported(ClientId, OperationId, new ApiError(
                ErrorCatalog.PackageIntegrityMismatchCode,
                "El paquete está dañado: el hash no coincide",
                string.Empty,
                "Descarta el paquete y descárgalo de nuevo: la operación no se puede reanudar.")),
            TestContext.Current.CancellationToken);

        // AD-15, UX-DR-36: un error sin sus tres partes no le dice nada al organizador,
        // así que se registra el hueco y no se escribe una fila incompleta.
        Assert.Empty(errors.Writes);
    }

    /// <summary>
    /// A registry of clients that knows the clients that the test gives it, without a
    /// database (AD-22).
    /// </summary>
    private sealed class RegisteredRegistry : IClientRegistrationRepository
    {
        private readonly HashSet<string> _registered;

        public RegisteredRegistry(params string[] registered)
        {
            _registered = new HashSet<string>(registered, StringComparer.Ordinal);
        }

        public Task<ClientRegistrationRow?> FindByClientIdAsync(
            string clientId,
            CancellationToken cancellationToken) =>
            Task.FromResult(_registered.Contains(clientId)
                ? new ClientRegistrationRow(
                    clientId,
                    "EQ-PC",
                    "1.0.0",
                    1,
                    ConnectionState.AnchorVerified,
                    DateTimeOffset.UtcNow)
                : null);

        public Task<ClientRegistrationRow?> FindByCanonicalMachineNameAsync(
            string canonicalMachineName,
            string excludedClientId,
            CancellationToken cancellationToken) =>
            Task.FromResult<ClientRegistrationRow?>(null);

        public Task<IReadOnlyList<ClientRegistrationRow>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ClientRegistrationRow>>([]);

        public Task SaveAsync(ClientRegistrationRow row, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    /// <summary>
    /// A registry of errors that keeps the writes that the use case makes, without a
    /// database (AD-15, AD-22).
    /// </summary>
    private sealed class RecordingErrors : IClientErrorRepository
    {
        private readonly List<ClientErrorWrite> _writes = [];

        public IReadOnlyList<ClientErrorWrite> Writes => _writes;

        public Task<IReadOnlyList<ClientErrorRow>> ListByClientAsync(
            string clientId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ClientErrorRow>>([]);

        public Task SaveAsync(ClientErrorWrite error, CancellationToken cancellationToken)
        {
            _writes.Add(error);

            return Task.CompletedTask;
        }
    }
}
