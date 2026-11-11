using LANPartyHub.Client.Application.Ports;

namespace LANPartyHub.Client.Application.GameLauncher;

/// <summary>
/// The projection of the playability of the installations of the client (FR-9, AC1, AC2,
/// AC8, AD-1, AD-22).
///
/// <para>
/// La proyección LEE las instalaciones que el cliente registró y las INSPECTA, así que la
/// decisión se REderiva al pintar y NO se persiste: no hay columna nueva ni migración
/// (AD-11, AD-22).
/// </para>
/// <para>
/// La proyección consume los PUERTOS del cliente, así que no conoce ni el disco ni la
/// interfaz (AD-1).
/// </para>
/// <para>
/// La VERSIÓN PUBLICADA se lee del SNAPSHOT LOCAL que el cliente ya sincronizó: la
/// comparación de las versiones usa el orden semántico del catálogo, que es la regla
/// ÚNICA de <c>Shared</c> (AC2, AR-5, AD-10).
/// </para>
/// </summary>
public sealed class PlayabilityProjection
{
    private readonly IClientStateStore _stateStore;
    private readonly IInstallationInspector _inspector;
    private readonly ICatalogSnapshotStore _snapshots;

    /// <summary>
    /// Creates the projection of the playability of the installations.
    /// </summary>
    /// <param name="stateStore">Access to the database of the client.</param>
    /// <param name="inspector">Port of the inspection and certification of the installation.</param>
    /// <param name="snapshots">
    /// Port of the persistence of the snapshot of the catalog, del que sale la versión
    /// PUBLICADA de cada juego.
    /// </param>
    public PlayabilityProjection(
        IClientStateStore stateStore,
        IInstallationInspector inspector,
        ICatalogSnapshotStore snapshots)
    {
        ArgumentNullException.ThrowIfNull(stateStore);
        ArgumentNullException.ThrowIfNull(inspector);
        ArgumentNullException.ThrowIfNull(snapshots);

        _stateStore = stateStore;
        _inspector = inspector;
        _snapshots = snapshots;
    }

    /// <summary>
    /// Projects the playability of EVERY installation that the client registered (AC1,
    /// AC8).
    ///
    /// <para>
    /// La lectura es LOCAL: las instalaciones pertenecen al cliente y la proyección no
    /// habla con el servidor (AD-5, AD-22).
    /// </para>
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task with the projection of the installations.</returns>
    public async Task<IReadOnlyList<InstalledGamePlayability>> ProjectAsync(
        CancellationToken cancellationToken)
    {
        var installations = await _stateStore
            .LoadInstallationsAsync(cancellationToken)
            .ConfigureAwait(false);

        var publishedVersions = await ReadPublishedVersionsAsync(cancellationToken)
            .ConfigureAwait(false);

        var projected = new List<InstalledGamePlayability>(installations.Count);

        foreach (var installation in installations)
        {
            // La inspección CERTIFICA la instalación contra el árbol que hay en disco, así
            // que el veredicto es el de 7.1 y no una segunda definición (AD-10, AC5).
            var inspection = await _inspector
                .InspectAsync(installation.GameId, cancellationToken)
                .ConfigureAwait(false);

            publishedVersions.TryGetValue(installation.GameId, out var publishedVersion);

            projected.Add(new InstalledGamePlayability(
                installation.GameId,
                installation.InstalledVersion,
                installation.ValidationState,
                PlayabilityResolver.Resolve(
                    inspection?.Verdict,
                    installation.InstalledVersion,
                    publishedVersion),
                publishedVersion));
        }

        return projected;
    }

    /// <summary>
    /// Finds the installation that the client registered for ONE videogame, con su
    /// decisión sobre la jugabilidad (AC1, AC2, AC8).
    ///
    /// <para>
    /// La lectura es LOCAL: la reparación necesita la VERSIÓN INSTALADA para resolver el
    /// paquete que repara, y no va al servidor por ella (AD-5, AD-22).
    /// </para>
    /// </summary>
    /// <param name="gameId">Identifier of the videogame.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>
    /// The installation of the videogame, or <c>null</c> when the client has none.
    /// </returns>
    public async Task<InstalledGamePlayability?> FindInstallationAsync(
        string gameId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);

        var installations = await _stateStore
            .LoadInstallationsAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var installation in installations)
        {
            if (!string.Equals(installation.GameId, gameId, StringComparison.Ordinal))
            {
                continue;
            }

            var inspection = await _inspector
                .InspectAsync(installation.GameId, cancellationToken)
                .ConfigureAwait(false);

            var publishedVersions = await ReadPublishedVersionsAsync(cancellationToken)
                .ConfigureAwait(false);

            publishedVersions.TryGetValue(gameId, out var publishedVersion);

            return new InstalledGamePlayability(
                installation.GameId,
                installation.InstalledVersion,
                installation.ValidationState,
                PlayabilityResolver.Resolve(
                    inspection?.Verdict,
                    installation.InstalledVersion,
                    publishedVersion),
                publishedVersion);
        }

        return null;
    }

    /// <summary>
    /// Reads the version that the catalog PUBLISHES for every game of the snapshot local
    /// del cliente (AC1, AC3, AR-5).
    ///
    /// <para>
    /// La lectura es LOCAL: el snapshot ya está en el cliente, así que la proyección no
    /// habla con el servidor (AD-5, AD-26).
    /// </para>
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The published version of every game, keyed by the identifier of the game.</returns>
    private async Task<IReadOnlyDictionary<string, string>> ReadPublishedVersionsAsync(
        CancellationToken cancellationToken)
    {
        var snapshot = await _snapshots.LoadAsync(cancellationToken).ConfigureAwait(false);

        if (snapshot is null)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        return snapshot.Games
            .GroupBy(game => game.GameId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Last().Version,
                StringComparer.Ordinal);
    }
}

/// <summary>
/// The playability of an installed videogame, as the surfaces consume it (AC1, AC2, AC8).
///
/// <para>
/// Es una PROYECCIÓN del estado local del cliente, y no un contrato de <c>Shared</c>: la
/// ruta de la instalación nunca sale del cliente (AD-18, AD-22).
/// </para>
/// </summary>
/// <param name="GameId">Identifier of the videogame that is installed.</param>
/// <param name="InstalledVersion">Version of the videogame that is installed.</param>
/// <param name="ValidationState">State of the installation.</param>
/// <param name="Decision">Decision about the playability of the installation.</param>
/// <param name="PublishedVersion">
/// Version that the catalog publishes for the videogame, or <c>null</c> when the client
/// does not know it (AC1, AC3).
/// </param>
public sealed record InstalledGamePlayability(
    string GameId,
    string InstalledVersion,
    Shared.Contracts.InstallationState ValidationState,
    PlayabilityDecision Decision,
    string? PublishedVersion = null);
