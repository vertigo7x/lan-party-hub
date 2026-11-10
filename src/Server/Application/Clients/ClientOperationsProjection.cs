using System.Collections.Concurrent;
using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Server.Application.Clients;

/// <summary>
/// Projection of the OPERATIONS that the clients are running right now (AD-22, FR-10).
///
/// <para>
/// Las operaciones en curso son el estado VIVO de una operación, y su dueño es el
/// CLIENTE: el servidor no las persiste, las PROYECTA (AD-10, AD-22). Por eso viven
/// EN MEMORIA, en un servicio CONCURRENTE que el hub y el caso de uso de la lectura
/// comparten (AD-22).
/// </para>
/// <para>
/// La proyección es IDEMPOTENTE por <c>(clientId, operationId)</c>: publicar dos veces
/// la misma operación no la duplica, y un mismo resultado terminal reportado dos veces
/// no crea un segundo resultado (AD-22, AD-33).
/// </para>
/// <para>
/// El texto del estado y el texto del resultado salen de la ÚNICA tabla de microcopia,
/// <see cref="StateDisplayMap"/>, y el estado de la máquina del cliente NUNCA se guarda
/// como enumeración del servidor (AD-10, AD-25).
/// </para>
/// </summary>
public sealed class ClientOperationsProjection
{
    /// <summary>
    /// Name of the kind of a DOWNLOAD operation, que es el nombre estable del valor y no
    /// un texto libre (AD-22, R-MVP-7).
    /// </summary>
    public const string DownloadOperationTypeName = OperationTypeNames.Download;

    /// <summary>
    /// Name of the kind of an INSTALLATION operation, que es el nombre estable del valor
    /// y no un texto libre (AD-22, R-MVP-7).
    /// </summary>
    public const string InstallationOperationTypeName = OperationTypeNames.Installation;

    /// <summary>
    /// Maximum number of TERMINAL results that the projection keeps PER CLIENT, así que
    /// la memoria que consume es ACOTADA (AD-22).
    ///
    /// <para>
    /// «Reciente» es un nombre, no una promesa de eternidad: la proyección de resultados
    /// es memoria de estado VIVO, y una lista sin tope crece con cada operación terminada
    /// del evento. El tope conserva los resultados MÁS RECIENTES, que son los que el
    /// organizador necesita ver (AD-22).
    /// </para>
    /// </summary>
    public const int MaxOutcomesPerClient = 50;

    /// <summary>
    /// Time after which a TERMINAL result stops being «recent» (AD-22).
    ///
    /// <para>
    /// «Reciente» es un nombre, no una promesa de eternidad: la proyección de resultados
    /// es memoria de estado VIVO, así que un resultado que envejece se suelta y la
    /// memoria que consume queda ACOTADA por el tiempo además de por el número (AD-22).
    /// </para>
    /// </summary>
    public static readonly TimeSpan OutcomeExpiry = TimeSpan.FromHours(1);

    /// <summary>
    /// Time after which an operation in course that the client does not report anymore
    /// is CONSIDERED EXPIRED, así que la proyección no conserva operaciones de un equipo
    /// que desapareció (AD-22).
    ///
    /// <para>
    /// La operación en curso es el estado VIVO de una operación y su dueño es el cliente:
    /// si el cliente deja de reportarla durante más que este plazo, la proyección la
    /// suelta, porque una operación que nadie declara no está «en curso» para nadie
    /// (AD-22).
    /// </para>
    /// </summary>
    public static readonly TimeSpan OperationExpiry = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Time during which the projection remembers that an operation ALREADY FINISHED, así
    /// que un progreso TARDÍO no la resucita (AD-22, AD-33).
    ///
    /// <para>
    /// La ventana de deduplicación de un resultado terminal es la vida del resultado
    /// terminal que la proyección conserva. El recuerdo de la terminación vive MÁS que
    /// el resultado: si el resultado se caduca o se descarta por el tope, el recuerdo
    /// sigue ahí, así que la idempotencia de la deduplicación NO depende de que el
    /// resultado siga visible (AD-22, AD-33).
    /// </para>
    /// </summary>
    public static readonly TimeSpan TerminationRetention = TimeSpan.FromHours(2);

    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, ProjectedOperation>> _operations =
        new(StringComparer.Ordinal);

    /// <summary>
    /// TERMINAL results, grouped BY CLIENT, so the reading of one client does not walk
    /// the results of the whole event (AD-22, AD-33).
    /// </summary>
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, ProjectedOutcome>> _outcomes =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Operations that ALREADY FINISHED, grouped by client, so a late publication does
    /// not resurrect an operation that the client closed (AD-22, AD-33).
    ///
    /// <para>
    /// El recuerdo es INDEPENDIENTE del resultado terminal que la proyección muestra: el
    /// resultado puede caducar o ser descartado por el tope, y el recuerdo sigue
    /// guardando que la operación se cerró, así que la deduplicación no se debilita
    /// cuando el resultado deja de verse (AD-33).
    /// </para>
    /// </summary>
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, DateTimeOffset>> _terminations =
        new(StringComparer.Ordinal);

    private readonly TimeProvider _timeProvider;

    /// <summary>Creates the projection of the operations that are in course.</summary>
    public ClientOperationsProjection()
        : this(TimeProvider.System)
    {
    }

    /// <summary>
    /// Creates the projection of the operations that are in course, over the clock that
    /// measures the expiry of las operaciones (AD-22).
    /// </summary>
    /// <param name="timeProvider">
    /// Clock that measures how long an operation has been without being reported, so the
    /// expiry can be exercised with a known instant (AD-22).
    /// </param>
    public ClientOperationsProjection(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Projects the operations that the client is running right now, REPLACING the ones
    /// that it reported before (AD-22).
    ///
    /// <para>
    /// La lista que publica el cliente es la lista COMPLETA de sus operaciones en curso,
    /// así que una operación que ya no aparece deja de estar en curso. El hueco NO
    /// borra las instalaciones, que son CONSERVADAS (AD-26).
    /// </para>
    /// </summary>
    /// <param name="clientId">Canonical identifier of the client.</param>
    /// <param name="operations">Operations that the client declared.</param>
    public void ReplaceActiveOperations(
        string clientId,
        IReadOnlyList<ClientStateChanged.ActiveOperation> operations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentNullException.ThrowIfNull(operations);

        var now = _timeProvider.GetUtcNow();

        var byClient = _operations.GetOrAdd(clientId, _ => new ConcurrentDictionary<string, ProjectedOperation>(StringComparer.Ordinal));

        // AD-22: las operaciones en curso caducan: una operación que el cliente dejó de
        // reportar durante más que el plazo NO está «en curso» para nadie, así que se
        // suelta ANTES de aplicar la instantánea (AD-22).
        PruneExpiredOperations(byClient, now);

        var reported = new HashSet<string>(StringComparer.Ordinal);

        foreach (var operation in operations)
        {
            if (operation is null || string.IsNullOrWhiteSpace(operation.OperationId))
            {
                continue;
            }

            // AD-22, AD-33: la operación que ya TERMINÓ no vuelve a estar en curso porque
            // el cliente la declare de nuevo. La instantsánea del cliente puede llegar
            // RETRASADA, después del resultado terminal, así que declararla no la
            // resucita (AD-22, AD-33).
            if (IsTerminated(clientId, operation.OperationId, now))
            {
                continue;
            }

            // AD-22: el TIPO de la operación es el nombre estable del valor. Un tipo que
            // el servidor NO reconoce no se inventa: la operación conserva el tipo que
            // ya tenía, así que no desaparece de la vista por un detalle de vocabulario
            // (AD-22).
            byClient.TryGetValue(operation.OperationId, out var existing);
            var operationType = ResolveOperationType(operation.OperationType, existing);
            if (operationType is null)
            {
                continue;
            }

            // AD-10, AD-22: el texto del estado sale de la tabla ÚNICA de microcopia, y
            // el estado se resuelve DENTRO de la máquina que declara el TIPO de la
            // operación: una descarga y una instalación son DOS máquinas distintas, así
            // que sus estados NO se confunden (AD-22).
            //
            // Un estado que la tabla NO reconoce no se inventa: la operación se descarta
            // y el resto de la proyección se conserva (AD-22).
            //
            // La validación va ANTES de declarar la operación como reportada: una
            // declaración inválida NO puede conservar viva una operación antigua que el
            // cliente ya no declara (AD-22).
            if (!TryResolveStateText(operation.State, operationType, out var stateText, out var declaredOrder))
            {
                continue;
            }

            // La operación que el servidor YA conoce: la instantánea solo declara el TIPO
            // y el ESTADO de la operación, así que el progreso REAL que el cliente ya
            // declaró en otro evento NO se pierde (AD-22, FR-10).
            // AD-22: el ORDEN del estado se compara DENTRO de la misma máquina de estados.
            // Una descarga y una instalación son DOS máquinas distintas, así que sus
            // posiciones NO se comparan entre sí (AD-10, AD-22).
            reported.Add(operation.OperationId);

            // AD-22: la fase REAL y los bytes REALES que el cliente ya declaró NO se
            // pierden cuando la operación se vuelve a declarar. La lista que publica el
            // cliente declara el TIPO y el ESTADO de la operación, no su progreso: el
            // progreso lo declaró el cliente en otro evento, así que sustituirlo por
            // vacío sería perder un dato REAL (AD-22, FR-10).
            //
            // AD-22: el estado que declara la instantánea es el ÚLTIMO que el cliente
            // tuvo, así que puede ir MÁS ATRAS que el estado del evento de progreso. Si el
            // progreso ya declaró un estado más avanzado, la instantánea NO lo REGRESA:
            // la vista no puede mostrar «En cola» de una descarga que ya tiene bytes
            // transferidos (AD-22).
            byClient[operation.OperationId] = existing is not null
                ? existing with
                {
                    OperationType = operationType,
                    StateText = existing.HasProgress ? existing.StateText : stateText,
                    StateOrder = existing.HasProgress ? existing.StateOrder : declaredOrder,
                    LastReportedAt = now,
                }
                : new ProjectedOperation(
                    operation.OperationId,
                    operationType,
                    stateText,
                    StateOrder: declaredOrder,
                    LastReportedAt: now);
        }

        // La lista que declara el cliente es la lista COMPLETA: la operación que ya no
        // aparece deja de estar en curso.
        foreach (var existing in byClient.Keys)
        {
            if (!reported.Contains(existing))
            {
                byClient.TryRemove(existing, out _);
            }
        }
    }

    /// <summary>
    /// Records the bytes that the client transferred in an operation, so the view shows
    /// the REAL progress and never an invented one (AD-22, FR-10).
    ///
    /// <para>
    /// AD-22: el progreso de una descarga ORDINARIA es la PRIMERA noticia que el
    /// servidor tiene de esa operación, así que la proyección la CREA cuando todavía no
    /// la conocía. El tipo y el estado de la operación salen del propio progreso, que
    /// lleva el <see cref="DownloadState"/> REAL que el cliente declaró, y los bytes son
    /// los REALES: no se inventa un porcentaje ni un total que el cliente no declaró
    /// (AD-10, AD-22).
    /// </para>
    /// <para>
    /// Un progreso TERMINAL cierra la operación: deja de estar en curso, así que la tabla
    /// del organizador no muestra una descarga que ya terminó (AD-10, AD-22).
    /// </para>
    /// </summary>
    /// <param name="clientId">Canonical identifier of the client.</param>
    /// <param name="operationId">Identifier of the operation.</param>
    /// <param name="bytesTransferred">Bytes transferred so far.</param>
    /// <param name="totalBytes">Total bytes of the operation, when they are known.</param>
    /// <param name="state">
    /// REAL state of the download machine that the client declared, or <c>null</c> when
    /// the publication does not carry it.
    /// </param>
    public void ReportTransfer(
        string clientId,
        string operationId,
        long bytesTransferred,
        long totalBytes,
        DownloadState? state = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);

        // AD-10, AD-22: un estado numérico que la tabla NO reconoce se RECHAZA solo en
        // esta publicación. La excepción no sale hacia el hub y la proyección de las
        // demás operaciones se conserva (AD-22).
        if (state is { } declaredState && !Enum.IsDefined(declaredState))
        {
            return;
        }

        if (bytesTransferred < 0 || totalBytes < 0)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();

        if (state is not null && IsTerminal(state.Value))
        {
            // AD-10, AD-22: la operación TERMINADA deja de estar en curso. Los bytes que
            // declaró NO se guardan en una operación que ya no existe: el resultado
            // terminal viaja aparte (AD-33).
            RememberTermination(clientId, operationId, now);
            RemoveOperation(clientId, operationId);

            return;
        }

        // AD-22, AD-33: un progreso TARDÍO de una operación que ya terminó NO puede
        // resucitarla. El recuerdo de la terminación es INDEPENDIENTE del resultado que
        // la proyección muestra, así que un resultado que caducó o que fue descartado por
        // el tope NO abre la puerta a resucitar la operación (AD-22, AD-33).
        if (IsTerminated(clientId, operationId, now))
        {
            return;
        }

        var byClient = _operations.GetOrAdd(
            clientId,
            _ => new ConcurrentDictionary<string, ProjectedOperation>(StringComparer.Ordinal));

        // AD-22: las operaciones en curso caducan: una operación que el cliente dejó de
        // reportar durante más que el plazo NO está «en curso» para nadie (AD-22).
        PruneExpiredOperations(byClient, now);

        // AD-10, AD-25: el texto del estado sale de la tabla única de microcopia, y el
        // estado REAL lo declara el propio cliente en el progreso (AD-22).
        var stateText = state is not null
            ? StateDisplayMap.GetText(state.Value)
            : null;

        if (!byClient.TryGetValue(operationId, out var operation))
        {
            // AD-22: la operación se CREA con el tipo y el estado REALES que declara el
            // progreso, y con los BYTES REALES que el cliente transfirió. El total que no
            // se conoce NO se inventa: queda desconocido (AD-10).
            byClient[operationId] = new ProjectedOperation(
                operationId,
                DownloadOperationTypeName,
                stateText ?? StateDisplayMap.GetText(DownloadState.Downloading),
                bytesTransferred,
                totalBytes > 0 ? totalBytes : null,
                StateOrder: state is not null ? StateOrderOf(state.Value) : (int?)null,
                LastReportedAt: now,
                HasProgress: true);

            return;
        }

        // AD-22: el progreso es la ÚLTIMA noticia del estado de la operación, así que el
        // estado que declara NO REGRESA al que ya hay cuando es más RETRASADO, y el ORDEN
        // del estado se conserva para que una instantánea posterior no pueda hacer
        // retroceder la operación (AD-22).
        var declaredOrder = state is not null ? StateOrderOf(state.Value) : (int?)null;

        byClient[operationId] = operation with
        {
            BytesTransferred = bytesTransferred,
            TotalBytes = totalBytes > 0 ? totalBytes : null,
            StateText = stateText ?? operation.StateText,
            StateOrder = declaredOrder ?? operation.StateOrder,
            LastReportedAt = now,
            HasProgress = true,
        };
    }

    /// <summary>
    /// Removes an operation from the projection, SOLO para el cliente que la declaró
    /// (AD-22, AD-33).
    ///
    /// <para>
    /// La operación es del cliente que la declaró: el resultado terminal de un cliente
    /// NUNCA cierra la operación de otro cliente, aunque ambos compartan el identificador
    /// de la operación (AD-22).
    /// </para>
    /// </summary>
    /// <param name="clientId">Canonical identifier of the client.</param>
    /// <param name="operationId">Identifier of the operation.</param>
    /// <returns><c>true</c> when the operation was in course and left it.</returns>
    public bool RemoveOperation(string clientId, string operationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);

        if (!_operations.TryGetValue(clientId, out var byClient))
        {
            return false;
        }

        return byClient.TryRemove(operationId, out _);
    }

    /// <summary>
    /// Records the REAL phase and the REAL state of an installation operation
    /// (AD-22, FR-10).
    ///
    /// <para>
    /// La fase se publica SIN porcentaje: un porcentaje sobre un total que no se conoce
    /// sería una mentira, así que el progreso conserva su longitud REAL (AD-22).
    /// </para>
    /// <para>
    /// El estado REAL que declara el cliente se proyecta con el TEXTO de la tabla ÚNICA
    /// de microcopia, así que la tabla del organizador no dice «Pendiente» de una
    /// instalación que ya se está instalando (AD-10, AD-22, AD-25).
    /// </para>
    /// </summary>
    /// <param name="clientId">Canonical identifier of the client.</param>
    /// <param name="operationId">Identifier of the operation.</param>
    /// <param name="phase">Real phase of the installation.</param>
    /// <param name="state">
    /// REAL state of the installation machine that the client declared, or <c>null</c>
    /// when the publication does not carry it.
    /// </param>
    public void ReportPhase(
        string clientId,
        string operationId,
        string phase,
        InstallationState? state = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);

        var now = _timeProvider.GetUtcNow();

        if (state is { } declaredState && !Enum.IsDefined(declaredState))
        {
            return;
        }

        // AD-22, AD-33: un progreso TARDÍO de una operación que ya terminó NO puede
        // resucitarla. El recuerdo de la terminación es INDEPENDIENTE del resultado que
        // la proyección muestra, así que un resultado que caducó o que fue descartado por
        // el tope NO abre la puerta a resucitar la operación (AD-22, AD-33).
        if (IsTerminated(clientId, operationId, now))
        {
            return;
        }

        var byClient = _operations.GetOrAdd(
            clientId,
            _ => new ConcurrentDictionary<string, ProjectedOperation>(StringComparer.Ordinal));

        // AD-22: las operaciones en curso caducan: una operación que el cliente dejó de
        // reportar durante más que el plazo NO está «en curso» para nadie (AD-22).
        PruneExpiredOperations(byClient, now);

        // AD-10, AD-25: el texto del estado sale de la tabla única de microcopia, y un
        // estado que la tabla NO reconoce NO se inventa: la operación conserva el estado
        // que ya tenía y solo cambia su fase (AD-22).
        string? stateText = null;

        if (state is { } declared)
        {
            try
            {
                stateText = StateDisplayMap.GetText(declared);
            }
            catch (ArgumentOutOfRangeException)
            {
                stateText = null;
            }
        }

        // La fase puede llegar antes que el ClientStateChanged que crea la operación.
        // En ese caso la proyección conserva la fase y la operación queda visible con su
        // tipo y su estado, sin inventar bytes ni porcentaje (AD-22).
        if (!byClient.TryGetValue(operationId, out var operation))
        {
            byClient[operationId] = new ProjectedOperation(
                operationId,
                InstallationOperationTypeName,
                stateText ?? StateDisplayMap.GetText(InstallationState.Installing),
                Phase: phase,
                StateOrder: state is not null ? StateOrderOf(state.Value) : (int?)null,
                LastReportedAt: now,
                HasProgress: true);
            return;
        }

        // AD-22: la fase es la ÚLTIMA noticia de la operación, así que el estado que
        // declara NO REGRESA al que ya hay cuando es más RETRASADO (AD-22).
        var declaredOrder = state is not null ? StateOrderOf(state.Value) : (int?)null;

        byClient[operationId] = operation with
        {
            Phase = phase,
            StateText = stateText ?? operation.StateText,
            StateOrder = declaredOrder ?? operation.StateOrder,
            LastReportedAt = now,
            HasProgress = true,
        };
    }

    /// <summary>
    /// Records the TERMINAL result of an operation, IDEMPOTENTLY (AD-33).
    ///
    /// <para>
    /// El resultado es un RESULTADO y NUNCA un error: un resultado DESCONOCIDO se
    /// rechaza sin corromper el resto de la proyección (AD-22, AD-33).
    /// </para>
    /// </summary>
    /// <param name="clientId">Canonical identifier of the client.</param>
    /// <param name="operationId">Identifier of the operation.</param>
    /// <param name="outcome">Result of the finished operation.</param>
    /// <returns><c>true</c> when the result was recorded.</returns>
    public bool RecordOutcome(string clientId, string operationId, OperationOutcome outcome)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);

        if (!Enum.IsDefined(outcome))
        {
            // AD-10, AD-25: un resultado que la tabla de microcopia NO reconoce no se
            // inventa y no se guarda: el resto de la proyección se conserva (AD-22).
            return false;
        }

        var now = _timeProvider.GetUtcNow();

        // AD-33: el resultado terminal se DEDUPLICA por operación. El mismo resultado
        // reportado dos veces NO crea un segundo resultado: la proyección conserva el
        // primero, que es el más reciente en su momento (AD-22, AD-33).
        var byClient = _outcomes.GetOrAdd(
            clientId,
            _ => new ConcurrentDictionary<string, ProjectedOutcome>(StringComparer.Ordinal));

        // AD-22: los resultados que caducaron se sueltan ANTES de guardar el nuevo, así
        // que el tope cuenta solo los resultados VIVOS (AD-22).
        PruneExpiredOutcomes(byClient, now);

        byClient.TryAdd(operationId, new ProjectedOutcome(
            operationId,
            StateDisplayMap.GetText(outcome),
            RecordedAt: now));

        // AD-22: el tope por cliente conserva los resultados MÁS RECIENTES, que son los
        // que el organizador necesita ver. Un resultado que se descarta por el tope NO
        // se pierde para la deduplicación: el recuerdo de la terminación lo conserva
        // aparte (AD-22, AD-33).
        EnforceOutcomeCap(byClient);

        // AD-22, AD-33: la operación se recuerda como TERMINADA, así que un progreso
        // tardío NO la resucita. El recuerdo es INDEPENDIENTE del resultado visible, así
        // que la deduplicación no se debilita cuando el resultado caduca o se descarta
        // por el tope (AD-33).
        RememberTermination(clientId, operationId, now);

        RemoveOperation(clientId, operationId);

        return true;
    }

    /// <summary>
    /// Reads the operations that the client is running right now, ordered by operation
    /// identifier.
    /// </summary>
    /// <param name="clientId">Canonical identifier of the client.</param>
    /// <returns>The operations that are in course.</returns>
    public IReadOnlyList<ClientRegistrationView.ActiveOperation> ListActiveOperations(string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId) ||
            !_operations.TryGetValue(clientId, out var byClient))
        {
            return [];
        }

        // AD-22: las operaciones que caducaron se sueltan al leer, así que una operación
        // de un equipo que desapareció no se queda en la memoria del servidor (AD-22).
        PruneExpiredOperations(byClient, _timeProvider.GetUtcNow());

        return
        [
            .. byClient.Values
                .OrderBy(operation => operation.OperationId, StringComparer.Ordinal)
                .Select(operation => new ClientRegistrationView.ActiveOperation(
                    operation.OperationId,
                    operation.OperationType,
                    operation.StateText,
                    operation.BytesTransferred,
                    operation.TotalBytes,
                    operation.Phase))
        ];
    }

    /// <summary>
    /// Reads the TERMINAL results that the client reported, ordered by operation
    /// identifier (AD-33).
    /// </summary>
    /// <param name="clientId">Canonical identifier of the client.</param>
    /// <returns>The results that the client reported.</returns>
    public IReadOnlyList<ClientRegistrationView.OperationOutcomeResult> ListOutcomes(string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId) ||
            !_outcomes.TryGetValue(clientId, out var byClient))
        {
            return [];
        }

        // AD-22: los resultados que caducaron se sueltan al leer, así que la memoria que
        // consume la proyección queda ACOTADA por el tiempo además de por el número
        // (AD-22).
        PruneExpiredOutcomes(byClient, _timeProvider.GetUtcNow());

        return
        [
            .. byClient.Values
                .OrderBy(entry => entry.OperationId, StringComparer.Ordinal)
                .Select(entry => new ClientRegistrationView.OperationOutcomeResult(
                    entry.OperationId,
                    entry.OutcomeText))
        ];
    }

    /// <summary>
    /// Tells whether a state of the download machine is TERMINAL, y NO se publica como
    /// operación en curso (AD-10).
    /// </summary>
    /// <param name="state">State of the download machine.</param>
    /// <returns><c>true</c> when the state is terminal.</returns>
    private static bool IsTerminal(DownloadState state) =>
        state is DownloadState.Completed or DownloadState.Cancelled or DownloadState.Failed;

    /// <summary>
    /// Resolves the text and the ORDER of the state that the client declares, DENTRO de
    /// la máquina de estados que nombra el TIPO de la operación (AD-10, AD-22).
    ///
    /// <para>
    /// Una descarga y una instalación son DOS máquinas distintas, así que sus estados NO
    /// se confunden: el estado <c>Failed</c> de una descarga no es el estado
    /// <c>Failed</c> de una instalación (AD-22).
    /// </para>
    /// </summary>
    /// <param name="state">Name of the machine state of the operation.</param>
    /// <param name="operationType">Name of the kind of the operation.</param>
    /// <param name="text">Display text of the state, when it is known.</param>
    /// <param name="order">Order of the state in its machine, when it is known.</param>
    /// <returns><c>true</c> when the state is known.</returns>
    private static bool TryResolveStateText(
        string? state,
        string? operationType,
        out string text,
        out int? order)
    {
        text = string.Empty;
        order = null;

        if (string.IsNullOrWhiteSpace(state))
        {
            return false;
        }

        // AD-22: el TIPO declara la máquina. Un tipo que el servidor NO reconoce no
        // inventa una máquina: el estado se busca en la máquina del cliente, que es la
        // que el cliente usa cuando no declara el tipo (AD-22).
        if (IsInstallationType(operationType))
        {
            if (Enum.TryParse<InstallationState>(state, ignoreCase: false, out var installationState) &&
                Enum.IsDefined(installationState))
            {
                text = StateDisplayMap.GetText(installationState);
                order = StateOrderOf(installationState);

                return true;
            }

            return false;
        }

        if (Enum.TryParse<DownloadState>(state, ignoreCase: false, out var downloadState) &&
            Enum.IsDefined(downloadState))
        {
            text = StateDisplayMap.GetText(downloadState);
            order = StateOrderOf(downloadState);

            return true;
        }

        if (Enum.TryParse<InstallationState>(state, ignoreCase: false, out var fallbackState) &&
            Enum.IsDefined(fallbackState))
        {
            text = StateDisplayMap.GetText(fallbackState);
            order = StateOrderOf(fallbackState);

            return true;
        }

        return false;
    }

    /// <summary>
    /// Tells whether the name of the kind of an operation names an INSTALLATION
    /// (AD-22).
    /// </summary>
    /// <param name="operationType">Name of the kind of the operation.</param>
    /// <returns><c>true</c> when the operation is an installation.</returns>
    private static bool IsInstallationType(string? operationType) =>
        operationType is not null &&
        operationType.Contains("install", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves the ORDEN of a machine state of an operation, así que el servidor puede
    /// saber si una publicación es MÁS NUEVA o más RETRASADA que la que ya tiene
    /// (AD-22).
    ///
    /// <para>
    /// El orden es el de la máquina de estados del cliente: una operación avanza por sus
    /// pasos y no vuelve atrás. La comparación NO usa el texto de presentación —que es
    /// microcopia y no un dato comparable— sino la POSICIÓN del valor en su enumeración
    /// (AD-10, AD-22).
    /// </para>
    /// <para>
    /// El orden de una descarga y el de una instalación se calculan en MÁQUINAS
    /// DISTINTAS: sus posiciones NO se comparan entre sí (AD-22).
    /// </para>
    /// </summary>
    /// <param name="state">State of the download machine.</param>
    /// <returns>The position of the state in its machine.</returns>
    private static int StateOrderOf(DownloadState state) => (int)state;

    /// <summary>
    /// Resolves the ORDEN of a machine state of an installation operation (AD-22).
    /// </summary>
    /// <param name="state">State of the installation machine.</param>
    /// <returns>The position of the state in its machine.</returns>
    private static int StateOrderOf(InstallationState state) => (int)state;

    /// <summary>
    /// Resolves the text of the state that the operation shows, KEEPING the one that is
    /// NEWER when the client declares a state that goes BACKWARDS (AD-22).
    ///
    /// <para>
    /// AD-22: el estado que declara la INSTANTÁNEA es el ÚLTIMO que el cliente tuvo, así
    /// que puede ir MÁS ATRAS que el estado del evento de progreso. Si el progreso ya
    /// declaró un estado más avanzado, la instantánea NO lo REGRESA: la vista no puede
    /// mostrar «En cola» de una descarga que ya tiene bytes transferidos (AD-22, FR-10).
    /// </para>
    /// </summary>
    /// <param name="currentText">Text of the state that the projection already holds.</param>
    /// <param name="declaredText">Text of the state that the client declares, or <c>null</c> when the client does not declare it.</param>
    /// <param name="declaredOrder">Order of the state that the client declares.</param>
    /// <param name="currentOrder">Order of the state that the projection already holds.</param>
    /// <returns>The text of the state that the operation shows.</returns>
    private static string NewerStateText(
        string currentText,
        string? declaredText,
        int? declaredOrder,
        int? currentOrder)
    {
        // El cliente NO declara estado: el que ya hay se conserva.
        if (declaredText is null || declaredOrder is null)
        {
            return currentText;
        }

        // El estado declarado NO es más nuevo que el que ya hay, así que se conserva el
        // que ya hay: la instantánea no puede hacer retroceder la operación (AD-22).
        return IsNewerOrEqual(declaredOrder.Value, currentOrder) ? declaredText : currentText;
    }

    /// <summary>
    /// Resolves the ORDEN del estado que la operación conserva, así que una publicación
    /// RETRASADA no hace retroceder la operación (AD-22).
    /// </summary>
    /// <param name="declaredOrder">Order of the state that the client declares.</param>
    /// <param name="currentOrder">Order of the state that the projection already holds.</param>
    /// <returns>The order that the operation keeps.</returns>
    private static int? NewerStateOrder(int? declaredOrder, int? currentOrder)
    {
        if (declaredOrder is null)
        {
            return currentOrder;
        }

        if (currentOrder is null)
        {
            return declaredOrder;
        }

        return IsNewerOrEqual(declaredOrder.Value, currentOrder.Value)
            ? declaredOrder
            : currentOrder;
    }

    /// <summary>
    /// Tells whether a declared order is NEWER OR EQUAL than the one that the projection
    /// already holds (AD-22).
    /// </summary>
    /// <param name="declaredOrder">Order of the state that the client declares.</param>
    /// <param name="currentOrder">Order of the state that the projection already holds.</param>
    /// <returns><c>true</c> when the declared state does not go backwards.</returns>
    private static bool IsNewerOrEqual(int declaredOrder, int? currentOrder) =>
        currentOrder is null || declaredOrder >= currentOrder.Value;

    /// <summary>
    /// Resolves the NAME of the kind of an operation, y conserva el que ya tenía cuando
    /// el cliente declara un tipo que el servidor NO reconoce (AD-22).
    ///
    /// <para>
    /// El tipo de la operación es el NOMBRE ESTABLE del valor, y el servidor solo
    /// admite los que declara: una descarga y una instalación (AD-22). Un tipo
    /// desconocido no se inventa, así que la operación conserva el tipo que ya tenía y
    /// no desaparece de la vista por un detalle de vocabulario (AD-22).
    /// </para>
    /// </summary>
    /// <param name="operationType">Name of the kind of the operation.</param>
    /// <param name="existing">
    /// Operation that the projection already knows, or <c>null</c> when the projection
    /// does not know it yet.
    /// </param>
    /// <returns>The name of the kind of the operation.</returns>
    private static string? ResolveOperationType(
        string? operationType,
        ProjectedOperation? existing)
    {
        if (string.Equals(operationType, DownloadOperationTypeName, StringComparison.OrdinalIgnoreCase))
        {
            return DownloadOperationTypeName;
        }

        if (string.Equals(operationType, InstallationOperationTypeName, StringComparison.OrdinalIgnoreCase))
        {
            return InstallationOperationTypeName;
        }

        // AD-22: un tipo que el servidor NO reconoce no se inventa: la operación conserva
        // el tipo que ya tenía. Si no existía, se rechaza la fila sin inventar uno.
        return existing?.OperationType;
    }

    /// <summary>
    /// Suelta las operaciones en curso que CADUCARON, así que la proyección no conserva
    /// operaciones de un equipo que desapareció (AD-22).
    /// </summary>
    /// <param name="byClient">Operations of the client.</param>
    /// <param name="now">Instant of the reading.</param>
    private void PruneExpiredOperations(
        ConcurrentDictionary<string, ProjectedOperation> byClient,
        DateTimeOffset now)
    {
        var deadline = now - OperationExpiry;

        foreach (var entry in byClient)
        {
            if (entry.Value.LastReportedAt <= deadline)
            {
                byClient.TryRemove(entry.Key, out _);
            }
        }
    }

    /// <summary>
    /// Suelta los resultados terminales que CADUCARON, así que la memoria que consume
    /// la proyección queda ACOTADA por el tiempo además de por el número (AD-22).
    /// </summary>
    /// <param name="byClient">Results of the client.</param>
    /// <param name="now">Instant of the reading.</param>
    private static void PruneExpiredOutcomes(
        ConcurrentDictionary<string, ProjectedOutcome> byClient,
        DateTimeOffset now)
    {
        var deadline = now - OutcomeExpiry;

        foreach (var entry in byClient)
        {
            if (entry.Value.RecordedAt <= deadline)
            {
                byClient.TryRemove(entry.Key, out _);
            }
        }
    }

    /// <summary>
    /// Conserva SOLO los resultados MÁS RECIENTES del cliente, así que la memoria que
    /// consume la proyección queda ACOTADA por el número (AD-22).
    /// </summary>
    /// <param name="byClient">Results of the client.</param>
    private static void EnforceOutcomeCap(ConcurrentDictionary<string, ProjectedOutcome> byClient)
    {
        if (byClient.Count <= MaxOutcomesPerClient)
        {
            return;
        }

        var surplus = byClient.Count - MaxOutcomesPerClient;

        foreach (var entry in byClient
            .OrderBy(entry => entry.Value.RecordedAt)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .Take(surplus))
        {
            byClient.TryRemove(entry.Key, out _);
        }
    }

    /// <summary>
    /// Recuerda que una operación TERMINÓ, así que un progreso TARDÍO no la resucita
    /// (AD-22, AD-33).
    /// </summary>
    /// <param name="clientId">Canonical identifier of the client.</param>
    /// <param name="operationId">Identifier of the operation.</param>
    /// <param name="now">Instant of the termination.</param>
    private void RememberTermination(string clientId, string operationId, DateTimeOffset now)
    {
        // AD-22, AD-33: los recuerdos que CADUCARON se sueltan ANTES de recordar el
        // nuevo, así que la memoria que consume la proyección queda ACOTADA por el tiempo
        // y no crece con cada operación terminada del evento (AD-22).
        PruneExpiredTerminations(now);

        var byClient = _terminations.GetOrAdd(
            clientId,
            _ => new ConcurrentDictionary<string, DateTimeOffset>(StringComparer.Ordinal));

        byClient[operationId] = now;
    }

    /// <summary>
    /// Suelta los recuerdos de terminación que CADUCARON, así que el recuerdo de que una
    /// operación ya terminó no crece sin límite (AD-22, AD-33).
    /// </summary>
    /// <param name="now">Instant of the publication.</param>
    private void PruneExpiredTerminations(DateTimeOffset now)
    {
        var deadline = now - TerminationRetention;

        foreach (var entry in _terminations)
        {
            foreach (var termination in entry.Value)
            {
                if (termination.Value <= deadline)
                {
                    entry.Value.TryRemove(termination.Key, out _);
                }
            }
        }
    }

    /// <summary>
    /// Tells whether una operación YA TERMINÓ y su recuerdo NO ha caducado, así que un
    /// progreso tardío no la resucita (AD-22, AD-33).
    /// </summary>
    /// <param name="clientId">Canonical identifier of the client.</param>
    /// <param name="operationId">Identifier of the operation.</param>
    /// <param name="now">Instant of the publication.</param>
    /// <returns><c>true</c> when the operation already finished.</returns>
    private bool IsTerminated(string clientId, string operationId, DateTimeOffset now)
    {
        if (!_terminations.TryGetValue(clientId, out var byClient))
        {
            return false;
        }

        if (!byClient.TryGetValue(operationId, out var terminatedAt))
        {
            return false;
        }

        // AD-22, AD-33: el recuerdo de la terminación VIVE MÁS que el resultado: cuando
        // caduca, la operación ya no está en curso y puede volver a declararse (AD-33).
        if (now - terminatedAt > TerminationRetention)
        {
            byClient.TryRemove(operationId, out _);

            return false;
        }

        return true;
    }

    /// <summary>
    /// An operation that is in course, as the projection sees it (AD-22).
    /// </summary>
    /// <param name="OperationId">Identifier of the operation.</param>
    /// <param name="OperationType">Name of the kind of operation.</param>
    /// <param name="StateText">Display text of the state of the operation.</param>
    /// <param name="BytesTransferred">Bytes transferred so far, when they are known.</param>
    /// <param name="TotalBytes">Total bytes of the operation, when they are known.</param>
    /// <param name="Phase">Real phase of the operation, when it has one.</param>
    /// <param name="StateOrder">
    /// ORDEN de la máquina de estados que declaró el cliente, así que el servidor puede
    /// saber si una publicación es MÁS NUEVA o más retrasada que la que ya tiene, sin
    /// guardar el estado de la máquina del cliente como enumeración del servidor
    /// (AD-10, AD-22).
    /// </param>
    /// <param name="LastReportedAt">Instant of the last publication about this operation.</param>
    private sealed record ProjectedOperation(
        string OperationId,
        string OperationType,
        string StateText,
        long? BytesTransferred = null,
        long? TotalBytes = null,
        string? Phase = null,
        int? StateOrder = null,
        DateTimeOffset LastReportedAt = default,
        bool HasProgress = false);

    /// <summary>
    /// A terminal result that the client reported, as the projection sees it (AD-33).
    /// </summary>
    /// <param name="OperationId">Identifier of the operation that finished.</param>
    /// <param name="OutcomeText">Display text of the result.</param>
    /// <param name="RecordedAt">Instant of the publication of the result.</param>
    private sealed record ProjectedOutcome(
        string OperationId,
        string OutcomeText,
        DateTimeOffset RecordedAt = default);
}
