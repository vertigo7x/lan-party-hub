using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Server.Application.Ports;

/// <summary>
/// Port of the projection of the INSTALLATIONS of the clients (AD-22, FR-10).
///
/// <para>
/// The projection is the read-only view that the server keeps over what the clients
/// have installed. It is an entity that is DISTINCT from the <c>ClientInstallation</c> of
/// the client and it does not share its name (AD-22).
/// </para>
/// <para>
/// The port declares the two operations that the use case of the projection needs: the
/// UPSERT idempotent by <c>(clientId, gameId)</c>, and the reading of the installations
/// of a client (AD-22).
/// </para>
/// <para>
/// El puerto no dice NADA de cómo se guarda la fila: el caso de uso depende de esta
/// abstracción y nunca del adaptador que la implementa (AD-1).
/// </para>
/// </summary>
public interface IClientInstallationViewRepository
{
    /// <summary>
    /// Saves the installation of a client: it writes it when it is new and it completes
    /// it when it already exists, so a row is NEVER duplicated (AD-22).
    ///
    /// <para>
    /// El UPSERT es IDEMPOTENTE por la clave <c>(clientId, gameId)</c>: publicar dos
    /// veces la misma instalación deja UNA fila, y una instalación RETIRADA del catálogo
    /// se CONSERVA mientras el cliente la siga reportando (AD-26).
    /// </para>
    /// </summary>
    /// <param name="row">Row of the installation to save.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the row has been saved.</returns>
    Task SaveAsync(ClientInstallationViewRow row, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the installations of a client, ordered by game identifier.
    /// </summary>
    /// <param name="clientId">Canonical identifier of the client.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The installations that the client reported.</returns>
    Task<IReadOnlyList<ClientInstallationViewRow>> ListByClientAsync(
        string clientId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Removes only the installations of the client that are absent from a VALID complete
    /// snapshot. This operation is never used by a connection-only event or by a failed
    /// local read (AD-22, AD-26).
    /// </summary>
    /// <param name="clientId">Canonical identifier of the client.</param>
    /// <param name="reportedGameIds">Game identifiers present in the valid snapshot.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the absent installations have been removed.</returns>
    Task RemoveAbsentAsync(
        string clientId,
        IReadOnlySet<string> reportedGameIds,
        CancellationToken cancellationToken);
}

/// <summary>
/// A row of the projection of the installations of a client, as the use case sees it
/// (AD-22).
///
/// <para>
/// El estado de la instalación es un TEXTO de presentación copiado del
/// <c>StateDisplayMap</c> de <c>Shared</c>, y NUNCA el enum del cliente (AD-10, AD-22).
/// </para>
/// </summary>
/// <param name="ClientId">Opaque UUID v7 identifier of the client.</param>
/// <param name="GameId">Opaque identifier of the game.</param>
/// <param name="InstalledVersion">Version that the client has installed.</param>
/// <param name="ValidationStateText">Display text of the state of the installation.</param>
public sealed record ClientInstallationViewRow(
    string ClientId,
    string GameId,
    string InstalledVersion,
    string ValidationStateText);
