using System.Collections.Concurrent;
using LANPartyHub.Client.Application.Install;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Client.Application.Install;

/// <summary>
/// Tracker of the INSTALLATION operations that the client is running right now
/// (AD-18, AD-22, FR-10).
///
/// <para>
/// La operación de instalación es una UNIDAD DE TRABAJO del cliente, y su dueño es el
/// cliente: el servidor solo PROYECTA lo que el cliente declara (AD-10, AD-22). Por eso
/// el identificador de la operación lo MIENTE el cliente, con la misma forma opaca UUID
/// v7 que usa el resto de identificadores del sistema (AD-23).
/// </para>
/// <para>
/// El tracker hace DOS cosas: le da al servidor la operación ACTIVA —con su tipo y su
/// estado de máquina— y le publica la FASE REAL de su progreso (AD-22). La fase se
/// publica SIN PORCENTAJE: un porcentaje sobre un total que no se conoce sería una
/// mentira, así que el payload conserva su longitud real (AD-22).
/// </para>
/// <para>
/// La publicación NUNCA impide que la instalación siga: un servidor que no recibe el
/// progreso no puede impedir que el manifiesto se aplique (AD-1, AD-22).
/// </para>
/// <para>
/// El tracker publica ALSO el ESTADO COMPLETO del cliente cuando una operación empieza
/// o termina, así que el servidor ve la operación aparecer y desaparecer sin esperar a
/// la siguiente publicación periódica (AD-22, FR-10). La publicación se ENLAZA TARDE,
/// después de que exista la composición del registro: el registro necesita al tracker y
/// el tracker necesita al registro, así que uno de los dos tiene que llegar después
/// (AD-22).
/// </para>
/// </summary>
public sealed class ActiveInstallations : LANPartyHub.Client.Application.Clients.IActiveInstallationSource
{
    /// <summary>
    /// Name of the kind of an installation operation, que es el nombre estable del valor
    /// y no un texto libre (AD-22, R-MVP-7).
    /// </summary>
    public const string InstallationOperationTypeName = OperationTypeNames.Installation;

    private readonly ConcurrentDictionary<string, TrackedInstallation> _installations =
        new(StringComparer.Ordinal);

    private readonly IInstallationProgressReporter? _reporter;
    private readonly ILogger<ActiveInstallations>? _logger;

    private readonly Lock _publicationGate = new();

    private Func<CancellationToken, Task>? _statePublication;

    /// <summary>Creates the tracker of the installations in course.</summary>
    /// <param name="reporter">
    /// Port of the publication of the progress of la instalación, or <c>null</c> when the
    /// client cannot publish it. Sin publicador la instalación SIGUE: la proyección es
    /// un complemento y no su motivo de vida (AD-1, AD-22).
    /// </param>
    /// <param name="logger">Log of the tracker, or <c>null</c> when it has none.</param>
    public ActiveInstallations(
        IInstallationProgressReporter? reporter = null,
        ILogger<ActiveInstallations>? logger = null)
    {
        _reporter = reporter;
        _logger = logger;
    }

    /// <summary>
    /// ENLAZA TARDE la publicación del ESTADO COMPLETO del cliente, así que el tracker
    /// puede avisar de que una operación aparece o desaparece (AD-22, FR-10).
    ///
    /// <para>
    /// El enlace es TARDE porque el registro del equipo PUBLICARÍA el estado y necesita
    /// al tracker como fuente de las operaciones en curso: construir el tracker con el
    /// registro y el registro con el tracker al mismo tiempo sería un ciclo (AD-22).
    /// </para>
    /// </summary>
    /// <param name="publication">
    /// Publication of the state of the client, or <c>null</c> when the client cannot
    /// publish it. Sin publicador la instalación SIGUE: la proyección es un complemento y
    /// no su motivo de vida (AD-1, AD-22).
    /// </param>
    public void ConfigureStatePublication(Func<CancellationToken, Task>? publication)
    {
        lock (_publicationGate)
        {
            _statePublication = publication;
        }
    }

    /// <summary>
    /// Publishes the COMPLETE state of the client, so the server sees the operation that
    /// just started or just finished (AD-22, FR-10).
    ///
    /// <para>
    /// La publicación es de MEJOR ESFUERZO: un servidor que no recibe el estado no puede
    /// impedir que el manifiesto se aplique (AD-1, AD-22).
    /// </para>
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the publication.</param>
    /// <returns>A task that completes when the publication has been attempted.</returns>
    public async Task PublishStateAsync(CancellationToken cancellationToken)
    {
        Func<CancellationToken, Task>? publication;

        lock (_publicationGate)
        {
            publication = _statePublication;
        }

        if (publication is null)
        {
            return;
        }

        try
        {
            await publication(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // La publicación del estado NUNCA impide que la instalación siga (AD-1, AD-22).
            _logger?.LogWarning(
                exception,
                "No se pudo publicar el estado del cliente tras el cambio de las instalaciones en curso: la instalación sigue.");
        }
    }

    /// <summary>
    /// Starts the tracking of an installation operation, MINTANDO su identificador (AD-22).
    /// </summary>
    /// <param name="clientId">Canonical identifier of the client that installs.</param>
    /// <param name="gameId">Identifier of the game that is installed.</param>
    /// <returns>The identifier of the installation operation.</returns>
    public string Begin(string clientId, string gameId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);

        // AD-23: el identificador de la operación es opaco y UUID v7, como el resto de
        // identificadores del sistema.
        var operationId = Guid.CreateVersion7().ToString("N");

        _installations[operationId] = new TrackedInstallation(
            operationId,
            gameId,
            InstallationState.Pending);

        return operationId;
    }

    /// <summary>
    /// Publishes the REAL phase of an installation operation and keeps the operation in
    /// course with the state that the phase declares (AD-22).
    ///
    /// <para>
    /// La fase se publica como el TEXTO de la fase, y el estado de la máquina es el
    /// que la fase declara: el servidor convierte ese estado en el texto de
    /// presentación de la tabla única de microcopia (AD-10, AD-25).
    /// </para>
    /// </summary>
    /// <param name="clientId">Canonical identifier of the client that installs.</param>
    /// <param name="operationId">Identifier of the installation operation.</param>
    /// <param name="phase">Real phase of the installation.</param>
    /// <param name="cancellationToken">Token that cancels the publication.</param>
    /// <returns>A task that completes when the phase has been published.</returns>
    public async Task ReportPhaseAsync(
        string clientId,
        string operationId,
        InstallPhase phase,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);

        var state = StateOf(phase);

        if (_installations.TryGetValue(operationId, out var tracked))
        {
            tracked.Phase = PhaseTextOf(phase);
            tracked.State = state;
        }

        if (_reporter is null)
        {
            return;
        }

        try
        {
            // AD-22: el payload NO lleva porcentaje. La fase es REAL y el total de
            // bytes puede no conocerse, así que un porcentaje sería inventado.
            await _reporter
                .ReportAsync(
                    new InstallationProgressUpdated(
                        clientId,
                        operationId,
                        state,
                        PhaseTextOf(phase)),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // La publicación NO impide que la instalación siga (AD-1, AD-22).
            _logger?.LogWarning(
                exception,
                "No se pudo publicar la fase '{Phase}' de la instalación {OperationId}: la instalación sigue.",
                phase,
                operationId);
        }
    }

    /// <summary>
    /// Finishes the tracking of an installation operation: la operación deja de estar en
    /// curso, y su instalación PERSISTE (AD-22, AD-26).
    /// </summary>
    /// <param name="operationId">Identifier of the installation operation.</param>
    public void Complete(string operationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);

        _installations.TryRemove(operationId, out _);
    }

    /// <summary>
    /// Reads the installation operations that the client is running right now, ordered by
    /// operation identifier (AD-22).
    /// </summary>
    /// <returns>The installation operations that are in course.</returns>
    public IReadOnlyList<ClientStateChanged.ActiveOperation> ListActiveOperations()
    {
        return
        [
            .. _installations.Values
                .OrderBy(installation => installation.OperationId, StringComparer.Ordinal)
                .Select(installation => new ClientStateChanged.ActiveOperation(
                    installation.OperationId,
                    InstallationOperationTypeName,
                    installation.State.ToString()))
        ];
    }

    /// <summary>
    /// Reads the REAL phase of an installation operation that is in course, or
    /// <c>null</c> when the operation is not tracked (AD-22).
    /// </summary>
    /// <param name="operationId">Identifier of the installation operation.</param>
    /// <returns>The real phase of the operation.</returns>
    public string? PhaseOf(string operationId)
    {
        if (string.IsNullOrWhiteSpace(operationId) ||
            !_installations.TryGetValue(operationId, out var tracked))
        {
            return null;
        }

        return tracked.Phase;
    }

    /// <summary>
    /// Resolves the machine state that a phase declares (AD-10, AD-22).
    ///
    /// <para>
    /// La fase es un paso de la operación y el estado es el de la máquina: una fase que
    /// no se reconoce NO se inventa, así que se declara el estado <c>Installing</c> y la
    /// publicación conserva la fase REAL (AD-10, AD-25).
    /// </para>
    /// </summary>
    /// <param name="phase">Phase of the installation.</param>
    /// <returns>The machine state that the phase declares.</returns>
    public static InstallationState StateOf(InstallPhase phase) => phase switch
    {
        InstallPhase.AwaitingConfirmation => InstallationState.AwaitingConfirmation,
        InstallPhase.Validating => InstallationState.ValidatingInstallation,
        InstallPhase.Completed => InstallationState.Installed,
        InstallPhase.Failed => InstallationState.Failed,
        _ => InstallationState.Installing,
    };

    /// <summary>
    /// Resolves the TEXT of a phase, que es el nombre estable del valor de la fase y no
    /// un texto libre (AD-22).
    ///
    /// <para>
    /// El texto de la fase es el NOMBRE del valor, porque la fase es un dato del
    /// cliente y el servidor la muestra TAL CUAL, sin traducirla: el servidor no
    /// inventa texto de presentación que la tabla de microcopia no declara (AD-10,
    /// AD-25).
    /// </para>
    /// </summary>
    /// <param name="phase">Phase of the installation.</param>
    /// <returns>The text of the phase.</returns>
    public static string PhaseTextOf(InstallPhase phase) => phase.ToString();

    /// <summary>
    /// An installation operation that the client is running right now (AD-22).
    /// </summary>
    /// <param name="OperationId">Identifier of the operation.</param>
    /// <param name="GameId">Identifier of the game that is installed.</param>
    /// <param name="State">Current machine state of the operation.</param>
    private sealed class TrackedInstallation(string operationId, string gameId, InstallationState state)
    {
        /// <summary>Identifier of the operation.</summary>
        public string OperationId { get; } = operationId;

        /// <summary>Identifier of the game that is installed.</summary>
        public string GameId { get; } = gameId;

        /// <summary>Current machine state of the operation.</summary>
        public InstallationState State { get; set; } = state;

        /// <summary>Real phase of the operation, when it has published one.</summary>
        public string? Phase { get; set; }
    }
}
