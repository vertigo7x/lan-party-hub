namespace LANPartyHub.Server.Application.Ports;

/// <summary>
/// Port of the registry of the download intentions (AD-10, AD-22).
///
/// <para>
/// The port is declared in the Application layer and it is implemented by the adapter that
/// owns the tables of the SQLite file of the server. The registry records the INTENTION of
/// a download and NOTHING about the state of the operation: the operation is executed and
/// owned by the client, and the server only projects the resulting state (AD-10, AD-22).
/// </para>
/// </summary>
public interface IDownloadIntentRepository
{
    /// <summary>
    /// Records the intention of a download of a package by a client.
    ///
    /// <para>
    /// La intención se guarda CON el estado inicial <c>Queued</c> y SIN resultado
    /// terminal: el servidor no ejecuta la descarga y no puede saber cómo va, así que
    /// su fila es el reflejo de lo que el cliente dijo que iba a hacer (AD-10, AD-22).
    /// </para>
    /// </summary>
    /// <param name="intent">Intention to record.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the intention is recorded.</returns>
    Task AddAsync(DownloadIntentRow intent, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the recorded intention of a download, or <c>null</c> when there is none.
    /// </summary>
    /// <param name="operationId">Opaque identifier of the operation.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task with the intention, or with <c>null</c>.</returns>
    Task<DownloadIntentRow?> FindAsync(string operationId, CancellationToken cancellationToken);

    /// <summary>
    /// Tells whether the server already knows the client that registered an intention, so
    /// the registry can complete its row in the minimum form that the table of the
    /// registrations demands (AD-22).
    /// </summary>
    /// <param name="clientId">Opaque identifier of the client.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes with the answer.</returns>
    Task<bool> KnowsClientAsync(string clientId, CancellationToken cancellationToken);

    /// <summary>
    /// Writes the TERMINAL result of the operation in the column <c>outcome</c> of the
    /// intention (AD-33).
    ///
    /// <para>
    /// El resultado es un RESULTADO y NUNCA un error: el valor
    /// <c>PackageWithdrawn</c> NO produce <c>ApiError</c>, NO se persiste como
    /// <c>ClientError</c> y NO se registra en el <c>ErrorCatalog</c> (AD-33, R-MVP-8).
    /// </para>
    /// <para>
    /// La escritura es IDEMPOTENTE: el mismo resultado reportado dos veces deja la
    /// misma fila, con el mismo resultado (AD-22, AD-33).
    /// </para>
    /// </summary>
    /// <param name="intent">Intention whose terminal result is written.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the result has been written.</returns>
    Task SaveOutcomeAsync(DownloadIntentRow intent, CancellationToken cancellationToken);
}

/// <summary>
/// The intention of a download, as it is recorded by the server (AD-10, AD-22).
///
/// <param name="OperationId">Opaque identifier of the operation.</param>
/// <param name="ClientId">Opaque identifier of the client that registered the intention.</param>
/// <param name="PackageId">Opaque identifier of the package of the intention.</param>
/// <param name="State">Name of the state of the machine that the operation is in.</param>
/// <param name="Outcome">Name of the terminal result of the operation, empty while it runs.</param>
public sealed record DownloadIntentRow(
    string OperationId,
    string ClientId,
    string PackageId,
    string State,
    string Outcome);
