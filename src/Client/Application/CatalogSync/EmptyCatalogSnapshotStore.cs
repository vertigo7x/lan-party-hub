using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Client.Application.CatalogSync;

/// <summary>
/// The catalog snapshot store that NEVER holds a snapshot (AC1, AD-1).
///
/// <para>
/// El store existe para que un caso de uso que necesita el snapshot del catálogo pueda
/// construirse SIN él: sin snapshot no hay paquete que resolver, así que la reparación
/// no es posible y el motivo visible es el de la re-descarga (AC1, AC2).
/// </para>
/// <para>
/// NUNCA se inventa un snapshot vacío: un snapshot falso daría al juego un paquete que
/// no existe (AC5).
/// </para>
/// </summary>
public sealed class EmptyCatalogSnapshotStore : ICatalogSnapshotStore
{
    /// <summary>The only instance of the store.</summary>
    public static EmptyCatalogSnapshotStore Instance { get; } = new();

    private EmptyCatalogSnapshotStore()
    {
    }

    /// <inheritdoc />
    public Task<CatalogSnapshot?> LoadAsync(CancellationToken cancellationToken) =>
        Task.FromResult<CatalogSnapshot?>(null);

    /// <inheritdoc />
    public Task<long> GetAppliedVersionAsync(CancellationToken cancellationToken) =>
        Task.FromResult(0L);

    /// <inheritdoc />
    public Task<DateTimeOffset?> GetLastSynchronizedAtAsync(CancellationToken cancellationToken) =>
        Task.FromResult<DateTimeOffset?>(null);

    /// <inheritdoc />
    public Task SaveAsync(CatalogSnapshot snapshot, CancellationToken cancellationToken) =>
        throw new InvalidOperationException(
            "Este store de snapshot no guarda ningún snapshot: es el store vacío del cliente.");

    /// <inheritdoc />
    public Task<IReadOnlyList<InstalledGame>> LoadInstalledGamesAsync(
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<InstalledGame>>(Array.Empty<InstalledGame>());

    /// <inheritdoc />
    public Task<IReadOnlyList<ActiveOperation>> LoadActiveOperationsAsync(
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ActiveOperation>>(Array.Empty<ActiveOperation>());
}
