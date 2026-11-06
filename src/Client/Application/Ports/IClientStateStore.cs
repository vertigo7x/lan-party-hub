using LANPartyHub.Client.Domain;
using LANPartyHub.Client.Domain.Trust;

namespace LANPartyHub.Client.Application.Ports;

/// <summary>
/// Access to the database of the client (AD-11, AD-22).
///
/// <para>
/// The port is declared in the Application layer and it is implemented by the adapter that owns
/// EF Core and the SQLite file. The schema of that file belongs to the client and it never
/// replicates a table of the server.
/// </para>
/// </summary>
public interface IClientStateStore
{
    /// <summary>
    /// Applies the versioned migrations of the database of the client. The migrations are
    /// applied at startup and <c>EnsureCreated</c> is never used (AD-11).
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the database is up to date.</returns>
    Task MigrateAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reads the identity of the client, or <c>null</c> when the client has not emitted one
    /// yet (AD-23).
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task with the identity of the client.</returns>
    Task<ClientIdentity?> LoadIdentityAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Persists the identity of the client. The identifier is written once and it is never
    /// regenerated (AD-23).
    /// </summary>
    /// <param name="identity">Identity of the client.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the identity is persisted.</returns>
    Task SaveIdentityAsync(ClientIdentity identity, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the persisted download operations with the progress that goes with them.
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task with the persisted operations.</returns>
    Task<IReadOnlyList<PersistedDownloadOperation>> LoadDownloadOperationsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Persists the accumulated offset of a download operation (AD-11, AD-24).
    /// </summary>
    /// <param name="operationId">Opaque identifier of the operation.</param>
    /// <param name="bytesPersisted">Accumulated offset in bytes.</param>
    /// <param name="totalBytes">Total bytes of the package, or <c>null</c> when they are unknown and the persisted ones have to be kept.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the progress is persisted.</returns>
    Task SaveProgressAsync(string operationId, long bytesPersisted, long? totalBytes, CancellationToken cancellationToken);

    /// <summary>
    /// Records a download operation and the state that it is in (AD-10, AD-11, AD-24).
    ///
    /// <para>
    /// La operación es la ENTIDAD LOCAL del cliente y su estado es del cliente: el
    /// servidor solo proyecta el estado que el cliente publica (AD-10, AD-22). El registro
    /// se escribe una vez por operación y su estado se ACTUALIZA cuando la operación
    /// avanza, así que un reinicio del equipo encuentra la operación en el estado en el
    /// que estaba (AD-11).
    /// </para>
    /// </summary>
    /// <param name="operationId">Opaque identifier of the operation.</param>
    /// <param name="packageId">Opaque identifier of the package that the operation transfers.</param>
    /// <param name="state">State of the operation, as the name of the value of the machine state.</param>
    /// <param name="transferEngine">
    /// Identifier of the engine that transfers the bytes of the operation. It is the
    /// identifier of la única implementación del puerto (AD-4, AD-24).
    /// </param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the operation is persisted.</returns>
    Task SaveOperationAsync(
        string operationId,
        string packageId,
        string state,
        string transferEngine,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads the ancla de confianza that is already fixed, or <c>null</c> when the client
    /// has not fixed any (AD-27).
    ///
    /// <para>
    /// AD-27: las conexiones posteriores se validan contra el valor YA PERSISTIDO y no
    /// contra uno recién recibido, así que la lectura de este valor es lo que permite
    /// distinguir «todavía no se ha fijado» de «está fijada y es otra» (AD-27).
    /// </para>
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task with the fixed ancla, or with <c>null</c>.</returns>
    Task<PinnedTrustAnchor?> LoadTrustAnchorAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Persists the ancla de confianza that the player confirmed (AD-27).
    ///
    /// <para>
    /// AD-27: el rechazo PRECEDE a persistir. Esta operación solo se llama cuando el
    /// fingerprint calculado y el esperado coinciden, y un ancla ya fijada NO se sustituye
    /// por una clave distinta (AD-27).
    /// </para>
    /// </summary>
    /// <param name="anchor">Ancla de confianza that the player confirmed.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the ancla is persisted.</returns>
    Task SaveTrustAnchorAsync(PinnedTrustAnchor anchor, CancellationToken cancellationToken);

    /// <summary>
    /// Registers the installation of a videogame, or UPDATES the one that is already
    /// registered (AD-22, AC1, AC4).
    ///
    /// <para>
    /// La fila la posee el cliente y su CLAVE es el <c>gameId</c> (AD-22). El
    /// <c>installDirectoryId</c> identifica la raíz de instalación y NO cambia ante una
    /// reparación ni ante una reinstalación del binario (AC1), así que una fila que ya
    /// existe conserva el identificador que ya tenía (AC1).
    /// </para>
    /// </summary>
    /// <param name="installation">Installation to register.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the installation is persisted.</returns>
    Task SaveInstallationAsync(ClientInstallation installation, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the registered installation of a videogame, or <c>null</c> when the client
    /// has not registered any for it (AD-22).
    /// </summary>
    /// <param name="gameId">Identifier of the videogame.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task with the installation, or with <c>null</c>.</returns>
    Task<ClientInstallation?> LoadInstallationAsync(string gameId, CancellationToken cancellationToken);

    /// <summary>
    /// Reads EVERY installation that the client registered, so the Biblioteca can show
    /// the games that are installed with their actions (AD-22, AC8).
    ///
    /// <para>
    /// La lectura es LOCAL: las instalaciones pertenecen al cliente y la Biblioteca nunca
    /// va al servidor por ellas (AD-5, AD-22). La lista viene ORDENADA por identificador
    /// de juego, así que la Biblioteca muestra siempre las mismas filas en el mismo
    /// orden (AC8).
    /// </para>
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task with the registered installations.</returns>
    Task<IReadOnlyList<ClientInstallation>> LoadInstallationsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Persists the FAILURE of an installation, so the error queda registrado y la
    /// operación es REINTENTABLE sin reinstalar el cliente (AD-8, AD-11, AC5).
    ///
    /// <para>
    /// El estado de la instalación vive en la fila que el cliente ya tiene —el enum ya
    /// admite <c>Failed</c>—, así que NO hay migración ni tabla nueva: lo que se añade es
    /// el CÓDIGO del error en el sumidero durable, para que el siguiente arranque sepa
    /// POR QUÉ falló la operación (AD-11, AD-25).
    /// </para>
    /// </summary>
    /// <param name="installation">
    /// Installation whose failure is persisted. Its state is the terminal <c>Failed</c>.
    /// </param>
    /// <param name="errorCode">
    /// Stable code of the error of the catalog that explains the failure (AD-15, AD-25).
    /// </param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the failure is persisted.</returns>
    Task SaveInstallationFailureAsync(
        ClientInstallation installation,
        string errorCode,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads the code of the error of a FAILED installation, or <c>null</c> when the
    /// installation did not fail or the client does not know any (AD-11, AC5).
    ///
    /// <para>
    /// El código se lee del sumidero durable, así que sobrevive al reinicio del cliente y
    /// la acción «Reintentar» puede ofrecer el diagnóstico sin volver a instalar el
    /// cliente (AC5, NFR-9).
    /// </para>
    /// </summary>
    /// <param name="gameId">Identifier of the videogame.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task with the code of the error, or with <c>null</c>.</returns>
    Task<string?> LoadInstallationFailureAsync(string gameId, CancellationToken cancellationToken);
}
