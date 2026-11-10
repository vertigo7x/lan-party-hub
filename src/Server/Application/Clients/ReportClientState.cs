using LANPartyHub.Server.Application.Ports;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Server.Application.Clients;

/// <summary>
/// Use case that persists the state of the connection that a client declares and
/// PROJECTS what the client reports (AD-18, AD-22, AD-29, FR-10).
///
/// <para>
/// La transición del <c>connectionState</c> de un <c>ClientRegistration</c> es un
/// HECHO INTERNO del servidor que se PERSISTE: la tabla de clientes del organizador
/// refleja la conexión y la desconexión de cada equipo (AD-29, FR-10).
/// </para>
/// <para>
/// El cliente que publica es SIEMPRE el de la fila: el servidor no acepta una
/// publicación que nombre a otro cliente, porque el estado de un equipo es el suyo y
/// no el de otro (AD-29).
/// </para>
/// <para>
/// Un cliente que NO está registrado ignora la publicación: no se crea una fila por un
/// estado, porque el alta es la operación de registro (AD-30, AD-32).
/// </para>
/// <para>
/// Las INSTALACIONES que el cliente reporta se PROYECTAN con un UPSERT idempotente por
/// <c>(clientId, gameId)</c>, y el estado se guarda como el TEXTO de presentación copiado
/// del <see cref="StateDisplayMap"/> (AD-10, AD-22). Una instalación RETIRADA del
/// catálogo se CONSERVA mientras el cliente la siga reportando (AD-26). La instantánea
/// completa es la lista real: las instalaciones que el cliente ya no reporta se ELIMINAN
/// solo para ese cliente.
/// </para>
/// <para>
/// Las OPERACIONES en curso viven EN MEMORIA, en la proyección compartida con la
/// lectura de la vista (AD-22).
/// </para>
/// </summary>
public sealed class ReportClientState
{
    private readonly IClientRegistrationRepository _repository;
    private readonly IClientInstallationViewRepository _installations;
    private readonly ClientOperationsProjection _operations;
    private readonly ILogger<ReportClientState> _logger;

    /// <summary>Creates the use case that persists the state of the client.</summary>
    /// <param name="repository">Port of the registry of the clients.</param>
    /// <param name="installations">Port of the projection of the installations.</param>
    /// <param name="operations">Projection of the operations that are in course.</param>
    /// <param name="logger">Log of the state of the client.</param>
    public ReportClientState(
        IClientRegistrationRepository repository,
        IClientInstallationViewRepository installations,
        ClientOperationsProjection operations,
        ILogger<ReportClientState> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(installations);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _installations = installations;
        _operations = operations;
        _logger = logger;
    }

    /// <summary>
    /// Persists the state of the connection that the client declares and projects the
    /// installations and the operations that it reports (AD-22, FR-10).
    /// </summary>
    /// <param name="state">State that the client declares.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the state has been persisted.</returns>
    public async Task ExecuteAsync(
        ClientStateChanged state,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);

        var existing = await _repository
            .FindByClientIdAsync(state.ClientId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            // AD-30, AD-32: el alta del equipo es la operación de REGISTRO, así que un
            // estado de un cliente que no está registrado no crea ninguna fila.
            _logger.LogWarning(
                "El cliente {ClientId} publica su estado pero no está registrado: la publicación se ignora.",
                state.ClientId);

            return;
        }

        var updated = new ClientRegistrationRow(
            existing.ClientId,
            existing.MachineName,
            existing.ClientVersion,
            existing.ProtocolVersion,
            state.ConnectionState,
            existing.LastSeenAt);

        await _repository.SaveAsync(updated, cancellationToken).ConfigureAwait(false);

        // La conexión declarada por el cliente es válida para la proyección. El resto de
        // campos se valida POR FILA más abajo para que un dato malformado no descarte la
        // publicación completa (AD-22).

        // AD-22: las instalaciones del cliente se PROYECTAN con un UPSERT idempotente
        // por (clientId, gameId). Una instalación RETIRADA del catálogo NO se borra: se
        // conserva mientras el cliente la siga reportando (AD-26).
        var reportedGameIds = await ProjectInstallationsAsync(state, cancellationToken)
            .ConfigureAwait(false);

        // Una instantánea completa válida es la lista REAL de instalaciones del cliente.
        // Por eso, las filas de ese cliente que no aparecen en ella son instalaciones
        // desinstaladas y deben desaparecer. La retirada del catálogo no se confunde con
        // la desinstalación: una retirada que el cliente sigue reportando permanece (AD-26).
        if (state.InstalledGames is not null)
        {
            await _installations
                .RemoveAbsentAsync(state.ClientId, reportedGameIds, cancellationToken)
                .ConfigureAwait(false);
        }

        // AD-22: las operaciones en curso viven EN MEMORIA, y la lista que declara el
        // cliente es la lista COMPLETA de las suyas. Una instantánea deserializada con
        // listas null se trata como una lista vacía: una fila malformada NO puede hacer
        // fallar toda la publicación (AD-22).
        _operations.ReplaceActiveOperations(
            state.ClientId,
            state.ActiveOperations ?? []);

        _logger.LogInformation(
            "El cliente {ClientId} declara el estado de conexión {ConnectionState} con {InstallationCount} instalaciones y {OperationCount} operaciones en curso.",
            existing.ClientId,
            state.ConnectionState,
            state.InstalledGames?.Count ?? 0,
            state.ActiveOperations?.Count ?? 0);
    }

    /// <summary>
    /// Projects the installations that the client reports, converting the state of the
    /// machine into the PRESENTATION TEXT of the single table of microcopy (AD-10, AD-22).
    ///
    /// <para>
    /// Una instalación con un estado que la tabla NO reconoce se RECHAZA y el resto de
    /// la proyección se conserva: un estado desconocido no corrompe los otros datos
    /// (AD-22).
    /// </para>
    /// </summary>
    /// <param name="state">State that the client declares.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The identifiers of the valid installations that the snapshot reported.</returns>
    private async Task<IReadOnlySet<string>> ProjectInstallationsAsync(
        ClientStateChanged state,
        CancellationToken cancellationToken)
    {
        var reportedGameIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var game in state.InstalledGames ?? [])
        {
            if (game is null || string.IsNullOrWhiteSpace(game.GameId))
            {
                continue;
            }

            // La identidad declarada sigue presente aunque la fila no pueda actualizarse:
            // un estado/versión desconocidos no equivalen a una desinstalación.
            reportedGameIds.Add(game.GameId);

            if (string.IsNullOrWhiteSpace(game.InstalledVersion))
            {
                continue;
            }

            // AD-10, AD-25: el texto sale de la tabla ÚNICA de microcopia, y un estado
            // que la tabla NO reconoce NO se inventa.
            string stateText;

            try
            {
                stateText = StateDisplayMap.GetText(game.ValidationState);
            }
            catch (ArgumentOutOfRangeException)
            {
                _logger.LogWarning(
                    "El cliente {ClientId} declara un estado de instalación desconocido para el juego {GameId}: esa instalación no se proyecta y el resto se conserva.",
                    state.ClientId,
                    game.GameId);

                continue;
            }

            await _installations
                .SaveAsync(
                    new ClientInstallationViewRow(
                        state.ClientId,
                        game.GameId,
                        game.InstalledVersion,
                        stateText),
                    cancellationToken)
                .ConfigureAwait(false);

        }

        return reportedGameIds;
    }
}
