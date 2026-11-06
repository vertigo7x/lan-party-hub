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
/// </summary>
public sealed class PlayabilityProjection
{
    private readonly IClientStateStore _stateStore;
    private readonly IInstallationInspector _inspector;

    /// <summary>
    /// Creates the projection of the playability of the installations.
    /// </summary>
    /// <param name="stateStore">Access to the database of the client.</param>
    /// <param name="inspector">Port of the inspection and certification of the installation.</param>
    public PlayabilityProjection(IClientStateStore stateStore, IInstallationInspector inspector)
    {
        ArgumentNullException.ThrowIfNull(stateStore);
        ArgumentNullException.ThrowIfNull(inspector);

        _stateStore = stateStore;
        _inspector = inspector;
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

        var projected = new List<InstalledGamePlayability>(installations.Count);

        foreach (var installation in installations)
        {
            // La inspección CERTIFICA la instalación contra el árbol que hay en disco, así
            // que el veredicto es el de 7.1 y no una segunda definición (AD-10, AC5).
            var inspection = await _inspector
                .InspectAsync(installation.GameId, cancellationToken)
                .ConfigureAwait(false);

            projected.Add(new InstalledGamePlayability(
                installation.GameId,
                installation.InstalledVersion,
                installation.ValidationState,
                PlayabilityResolver.Resolve(inspection?.Verdict)));
        }

        return projected;
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
public sealed record InstalledGamePlayability(
    string GameId,
    string InstalledVersion,
    Shared.Contracts.InstallationState ValidationState,
    PlayabilityDecision Decision);
