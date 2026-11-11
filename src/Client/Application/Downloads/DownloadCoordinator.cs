using LANPartyHub.Client.Application.Diagnostics;
using LANPartyHub.Client.Application.Integrity;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Domain.StateMachines;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Client.Application.Downloads;

/// <summary>
/// Coordinator of the download operations of the client (AD-4, AD-8, AD-10, AD-11, AD-14,
/// AD-20, AD-24, AC1..AC10).
///
/// <para>
/// El coordinador es el ÚNICO dueño de las descargas del cliente: registra la intención
/// con el servidor, pide los bytes por el puerto de la transferencia, persiste el offset
/// acumulado, y gobierna la pausa, la reanudación y la cancelación (AC1, AC2, AC3, AC4).
/// </para>
/// <para>
/// El coordinador depende de los PUERTOS y nunca de un adaptador, así que su secuencia se
/// puede ejercitar sin un servidor y sin un disco (AD-1).
/// </para>
/// <para>
/// AD-14: los límites de concurrencia son POR CLIENTE y son configurables con valor por
/// defecto. El coordinador limita las descargas SIMULTÁNEAS y las conexiones TOTALES que
/// el cliente abre, de modo que un cliente no agota el enlace del evento.
/// </para>
/// <para>
/// AD-24: las operaciones que tocan el MISMO RECURSO se serializan, y las de recursos
/// distintos COEXISTEN: el recurso de una descarga es su paquete.
/// </para>
/// <para>
/// AD-8: el manifiesto se pide y se valida ANTES de registrar la intención, de modo que
/// un manifiesto que no es de fiar no deja NINGUNA escritura en disco: ni manifiesto en la
/// caché, ni archivo parcial, ni operación registrada (AD-2, AD-8, AD-27).
/// </para>
/// </summary>
public sealed class DownloadCoordinator
{
    /// <summary>
    /// Number of downloads that the client runs at the same time when no source fixes
    /// another one (AD-14).
    /// </summary>
    public const int DefaultMaxConcurrentDownloads = 2;

    /// <summary>
    /// Number of connections that the client opens in total when no source fixes another
    /// one (AD-14).
    /// </summary>
    public const int DefaultMaxTotalConnections = 4;

    /// <summary>
    /// End of a range that NO declares its final byte, so the request travels as
    /// «bytes=&lt;start&gt;-» y el servidor sirve el resto del contenido (AD-20).
    /// </summary>
    public const long UnboundedRangeEnd = long.MaxValue;

    private readonly ITransferEngine _engine;
    private readonly IDownloadIntentClient _intents;
    private readonly IClientStateStore _stateStore;
    private readonly IClientDataLayout _layout;
    private readonly IDownloadProgressReporter? _reporter;
    private readonly IServerVersionProbe? _versionProbe;
    private readonly ValidatePackage? _validation;
    private readonly RecordClientError? _errorRecorder;
    private IClientStateChangeNotifier? _stateNotifier;
    private readonly ILogger<DownloadCoordinator> _logger;
    private readonly int _maxConcurrentDownloads;
    private readonly int _maxTotalConnections;

    private readonly Dictionary<string, DownloadOperationHandle> _operations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SemaphoreSlim> _resourceLocks = new(StringComparer.Ordinal);
    private readonly HashSet<Task> _pendingReports = [];
    private readonly SemaphoreSlim _concurrencySlots;
    private readonly SemaphoreSlim _connectionSlots;
    private readonly Lock _lock = new();

    /// <summary>
    /// Creates the coordinator of the downloads.
    /// </summary>
    /// <param name="engine">Port of the transfer of bytes.</param>
    /// <param name="intents">Port of the registration of the intention of a download.</param>
    /// <param name="stateStore">Access to the database of the client.</param>
    /// <param name="layout">Layout of the data folder of the client.</param>
    /// <param name="reporter">
    /// Port of the publication of the progress, or <c>null</c> when the client does not
    /// publish it. La publicación NUNCA es una condición para que la descarga siga
    /// (AD-22).
    /// </param>
    /// <param name="logger">Log of the coordinator.</param>
    /// <param name="validation">
    /// Use case of the validation of the package, which checks the authenticity of the
    /// signed manifest BEFORE the intention is registered and the integrity of the bytes
    /// AFTER the transfer (AD-2, AD-8). It may be <c>null</c>, and then the coordinator
    /// no valida nada: el puerto es opcional porque la validación es una historia
    /// posterior a la descarga.
    /// </param>
    /// <param name="maxConcurrentDownloads">
    /// Number of downloads that the client runs at the same time. It is CONFIGURABLE and
    /// tiene valor por defecto (AD-14, AC10).
    /// </param>
    /// <param name="maxTotalConnections">
    /// Number of connections that the client opens in total. It is CONFIGURABLE y tiene
    /// valor por defecto (AD-14, AC10).
    /// </param>
    /// <param name="reporter">
    /// Port of the publication of the progress, or <c>null</c> when the client does not
    /// publish it. La publicación NUNCA es una condición para que la descarga siga
    /// (AD-22).
    /// </param>
    /// <param name="versionProbe">
    /// Port of the probe of the versions of the server, or <c>null</c> when the client
    /// does not check the compatibility before reanudar. La incompatibilidad NO es un
    /// estado: es un diagnóstico (AD-6, AC5).
    /// </param>
    /// <param name="stateNotifier">
    /// Port of the aviso de que el estado del cliente cambió, or <c>null</c> when the
    /// client does not publish it. Sin avisador la descarga SIGUE: la proyección es un
    /// complemento y no su motivo de vida (AD-1, AD-22).
    /// </param>
    /// <param name="errorRecorder">
    /// Use case that REGISTERS the errors of la descarga, or <c>null</c> when the client
    /// does not register them. Sin registrador la descarga SIGUE: el diagnóstico es un
    /// complemento y no su motivo de vida (AD-1, AD-11, NFR-7).
    /// </param>
    public DownloadCoordinator(
        ITransferEngine engine,
        IDownloadIntentClient intents,
        IClientStateStore stateStore,
        IClientDataLayout layout,
        ILogger<DownloadCoordinator> logger,
        ValidatePackage? validation = null,
        int maxConcurrentDownloads = DefaultMaxConcurrentDownloads,
        int maxTotalConnections = DefaultMaxTotalConnections,
        IDownloadProgressReporter? reporter = null,
        IServerVersionProbe? versionProbe = null,
        IClientStateChangeNotifier? stateNotifier = null,
        RecordClientError? errorRecorder = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(intents);
        ArgumentNullException.ThrowIfNull(stateStore);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(logger);

        if (maxConcurrentDownloads < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxConcurrentDownloads),
                "El límite de descargas simultáneas tiene que ser mayor que cero.");
        }

        if (maxTotalConnections < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxTotalConnections),
                "El límite de conexiones totales tiene que ser mayor que cero.");
        }

        _engine = engine;
        _intents = intents;
        _stateStore = stateStore;
        _layout = layout;
        _reporter = reporter;
        _versionProbe = versionProbe;
        _validation = validation;
        _stateNotifier = stateNotifier;
        _errorRecorder = errorRecorder;
        _logger = logger;
        _maxConcurrentDownloads = maxConcurrentDownloads;
        _maxTotalConnections = maxTotalConnections;

        _concurrencySlots = new SemaphoreSlim(maxConcurrentDownloads, maxConcurrentDownloads);
        _connectionSlots = new SemaphoreSlim(maxTotalConnections, maxTotalConnections);
    }

    /// <summary>
    /// ENLAZA TARDE el aviso de que el estado del cliente cambió, así que el coordinador
    /// puede avisar de que una descarga empieza o termina (AD-22, FR-10).
    ///
    /// <para>
    /// El enlace es TARDE porque el REGISTRO publica el estado y necesita al
    /// coordinador, y el coordinador necesita al registro: construir los dos con el uno
    /// al otro al mismo tiempo sería un ciclo (AD-22).
    /// </para>
    /// </summary>
    /// <param name="notifier">
    /// Port of the aviso de que el estado del cliente cambió, or <c>null</c> when the
    /// client does not publish it. Sin avisador la descarga SIGUE: la proyección es un
    /// complemento y no su motivo de vida (AD-1, AD-22).
    /// </param>
    public void ConfigureStateNotifier(IClientStateChangeNotifier? notifier)
    {
        lock (_lock)
        {
            _stateNotifier = notifier;
        }
    }

    /// <summary>
    /// Number of downloads that the client runs at the same time (AD-14, AC10).
    /// </summary>
    public int MaxConcurrentDownloads => _maxConcurrentDownloads;

    /// <summary>
    /// Number of connections that the client opens in total (AD-14, AC10).
    /// </summary>
    public int MaxTotalConnections => _maxTotalConnections;

    /// <summary>
    /// Raised when the state or the progress of an operation changes (AC2, UX-DR-13).
    /// </summary>
    public event Action<DownloadOperationHandle>? Changed;

    /// <summary>
    /// Starts the download of a package and returns its handle (AC1, AC2).
    ///
    /// <para>
    /// La secuencia es la del caso de uso: el cliente VALIDA el manifiesto firmado, REGISTRA
    /// la intención con el servidor, PERSISTE la operación con su identificador, y solo
    /// entonces pide los bytes. La operación es una unidad de trabajo con su propio
    /// <c>operationId</c> y su registro de estado persistido (AC1, AC10).
    /// </para>
    /// <para>
    /// AD-2, AD-8: la validación del manifiesto PRECEDE al registro de la intención, de
    /// modo que un manifiesto que no valida contra el ancla fijada no deja ninguna
    /// escritura en disco: ni manifiesto en la caché, ni archivo parcial, ni operación
    /// registrada.
    /// </para>
    /// </summary>
    /// <param name="packageId">Opaque identifier of the package that the client wants.</param>
    /// <param name="contentUri">Absolute URL of the content of the package.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The handle of the started operation.</returns>
    /// <exception cref="PackageValidationFailedException">
    /// The signed manifest of the package is not trustworthy, or it declares a schema
    /// version that the client does not support. Nothing was written on disk.
    /// </exception>
    public async Task<DownloadOperationHandle> StartAsync(
        string packageId,
        Uri contentUri,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentNullException.ThrowIfNull(contentUri);

        // AD-2, AD-8: la firma y la versión del esquema se comprueban ANTES de registrar
        // la intención. Si la comprobación falla, la operación NO se registra y NO se
        // escribe nada en disco (AD-27).
        if (_validation is not null)
        {
            var authenticity = await _validation
                .ValidateManifestAsync(packageId, cancellationToken)
                .ConfigureAwait(false);

            if (!authenticity.IsValid)
            {
                _logger.LogWarning(
                    "El manifiesto firmado del paquete {PackageId} no ha superado la validación de autenticidad: no se registra ninguna operación y no se escribe nada en disco.",
                    packageId);

                // AD-11, NFR-7, AC2: el fallo queda REGISTRADO en el registro de
                // diagnóstico del cliente. La operación todavía NO existe, así que el
                // error no va asociado a un `operationId`: lo que se conserva es el
                // código y sus tres partes (AD-23).
                await RecordErrorAsync(
                    authenticity.Error ?? ErrorCatalog.Resolve(ErrorCatalog.UnknownErrorCode),
                    null,
                    OperationTypeNames.Download,
                    cancellationToken).ConfigureAwait(false);

                throw new PackageValidationFailedException(packageId, authenticity);
            }
        }

        // AC1: el cliente registra la intención con el servidor y obtiene el
        // operationId. El servidor persiste el REFLEJO de la intención, no el estado de
        // la operación (AD-10, AD-22).
        var operationId = await _intents
            .RegisterAsync(packageId, cancellationToken)
            .ConfigureAwait(false);

        var handle = new DownloadOperationHandle(
            operationId,
            packageId,
            contentUri,
            new DownloadStateMachine(DownloadState.Queued));

        lock (_lock)
        {
            _operations[operationId] = handle;
        }

        // AD-10, AD-11: la operación y su estado se persisten, así que un reinicio del
        // equipo la encuentra en el estado en el que estaba.
        await _stateStore.SaveOperationAsync(
            operationId,
            packageId,
            handle.StateMachine.Current.ToString(),
            TransferEngineRegistry.HttpRangeTransferEngine,
            cancellationToken)
            .ConfigureAwait(false);

        // AD-22, FR-10: la descarga EMPIEZA, así que la lista de operaciones en curso del
        // cliente cambia y el servidor tiene que recibir el estado COMPLETO: sin este
        // aviso, la tabla del organizador no muestra la descarga que acaba de empezar
        // (AD-22).
        handle.Attach(Publish);

        // El aviso al organizador no bloquea el comienzo de la transferencia de bytes.
        NotifyStateChanged();

        handle.TrackRun(RunAsync(handle, cancellationToken));

        return handle;
    }

    /// <summary>
    /// Recovers the download operations that survived a restart of the machine
    /// (AC2, AC3, AD-11, AD-20, AD-24, SM-2).
    ///
    /// <para>
    /// La recuperación REHIDRATA las operaciones persistidas en operaciones VIVAS: cada
    /// una conserva su <c>operationId</c> y su <c>packageId</c>, y su offset efectivo es el
    /// que la reconciliación YA corrigió contra el archivo parcial. La rehidratación NO
    /// recalcula el offset: lo LEE (AD-11).
    /// </para>
    /// <para>
    /// La operación recuperada NO registra una intención nueva con el servidor: la
    /// intención ya está registrada y volver a llamarla la duplicaría. La reanudación pide
    /// el rango que empieza en el offset efectivo, así que el motor lo sirve con un
    /// <c>Range</c> sobre ese offset y no desde cero (AC1, AD-20, AD-24).
    /// </para>
    /// <para>
    /// Una operación TERMINAL no se recupera: una descarga completada, cancelada o fallida
    /// no se reanuda (AD-10, AC5). Su registro queda en la base de datos (AD-24).
    /// </para>
    /// </summary>
    /// <param name="contentUriResolver">
    /// Resolver of the absolute URL of the content of a package, because the url NO vive en
    /// la base de datos del cliente.
    /// </param>
    /// <param name="cancellationToken">Token that cancels the recovery.</param>
    /// <returns>The handles of the recovered operations, in the order of their identifier.</returns>
    public async Task<IReadOnlyList<DownloadOperationHandle>> RecoverAsync(
        Func<string, Uri> contentUriResolver,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contentUriResolver);

        var persisted = await _stateStore
            .LoadDownloadOperationsAsync(cancellationToken)
            .ConfigureAwait(false);

        var recovered = new List<DownloadOperationHandle>();

        foreach (var operation in persisted)
        {
            // El motor que la operación declara tiene que existir en la versión activa: la
            // reconciliación ya rechazó las que no, y una operación de otro motor no se
            // rehidrata (AD-24).
            if (!TransferEngineRegistry.IsActive(operation.TransferEngine))
            {
                continue;
            }

            var state = DownloadRecovery.ResolveState(operation.State);

            if (state is null || !DownloadRecovery.IsResumableState(state.Value))
            {
                // Una operación terminal o en un estado que la máquina no conoce NO se
                // reanuda, y su registro NO se borra (AD-10, AD-24).
                continue;
            }

            Uri contentUri;

            try
            {
                contentUri = contentUriResolver(operation.PackageId);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                _logger.LogWarning(
                    exception,
                    "No se recuperó la operación {OperationId}: la URL del contenido del paquete {PackageId} no se pudo resolver.",
                    operation.OperationId,
                    operation.PackageId);

                continue;
            }

            var handle = DownloadRecovery.Rehydrate(operation, contentUri, state.Value);

            // Una operación que estaba DESCARGANDO fue interrumpida por el reinicio, así
            // que su estado real es «Pausada». La transición está declarada en la tabla de
            // la máquina, así que la recuperación la usa en vez de inventar un estado
            // (AD-10).
            if (handle.StateMachine.Current == DownloadState.Downloading)
            {
                handle.StateMachine.TryTransitionTo(DownloadState.Paused);
            }

            lock (_lock)
            {
                _operations[handle.OperationId] = handle;
            }

            handle.Attach(Publish);

            recovered.Add(handle);

            _logger.LogInformation(
                "Se recupera la operación {OperationId} del paquete {PackageId} desde el offset {Offset} bytes.",
                handle.OperationId,
                handle.PackageId,
                handle.BytesPersisted);
        }

        // La reanudación de las operaciones recuperadas empieza DESPUÉS de la
        // rehidratación, así que la vista ya puede presentar «Reanumando desde …» con el
        // offset persistido (AC2, UX-DR-6).
        foreach (var handle in recovered)
        {
            Publish(handle);

            await ResumeAsync(handle, cancellationToken).ConfigureAwait(false);
        }

        return recovered;
    }

    /// <summary>
    /// Checks whether the client may resume its downloads against the server of the event
    /// (AD-6, FR-4, AC5).
    ///
    /// <para>
    /// Un cliente INCOMPATIBLE no inicia la descarga y no reanuda la que tenía: recibe el
    /// diagnóstico de incompatibilidad de versión. El sondeo NO decide la compatibilidad:
    /// la matriz vive SOLO en el servidor, así que el cliente se limita a leer la
    /// respuesta (AD-6).
    /// </para>
    /// <para>
    /// Un servidor que NO responde no es una incompatibilidad: el jugador tiene que poder
    /// reintentar, así que el sondeo devuelve que la compatibilidad NO se pudo comprobar
    /// (AC3, UX-DR-36).
    /// </para>
    /// </summary>
    /// <param name="endpoint">Endpoint of the server of the event.</param>
    /// <param name="cancellationToken">Token that cancels the probe.</param>
    /// <returns>The outcome of the gate of the compatibility.</returns>
    public async Task<DownloadCompatibilityGate> CheckCompatibilityAsync(
        ServerEndpoint endpoint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (_versionProbe is null)
        {
            // Sin sonda no hay veredicto, así que la puerta se abre: la descarga se intenta
            // y su propio fallo dice lo que pasó (AD-6).
            return DownloadCompatibilityGate.Compatible();
        }

        var result = await _versionProbe
            .ProbeAsync(endpoint, TimeSpan.FromSeconds(3), cancellationToken)
            .ConfigureAwait(false);

        if (!result.Reachable)
        {
            // El servidor no respondió: eso NO es una incompatibilidad y la operación
            // queda persistida para el siguiente arranque (AD-11, AC3).
            return DownloadCompatibilityGate.Unreachable();
        }

        if (!result.Compatible)
        {
            return DownloadCompatibilityGate.Incompatible(result.ServerVersion);
        }

        return DownloadCompatibilityGate.Compatible();
    }

    /// <summary>
    /// Pauses a download operation (AC3).
    ///
    /// <para>
    /// La pausa NO borra nada: el offset persistido se conserva y el archivo parcial se
    /// queda en el disco, así que la reanudación continúa desde la MISMA posición sin
    /// volver a cero (AC3, AC4).
    /// </para>
    /// </summary>
    /// <param name="handle">Handle of the operation to pause.</param>
    /// <returns>A task that completes when the operation is paused.</returns>
    public async Task PauseAsync(DownloadOperationHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);

        if (!handle.StateMachine.TryTransitionTo(DownloadState.Paused))
        {
            return;
        }

        handle.RequestPause();

        // La pausa debe interrumpir la lectura que está en curso para que el recurso se
        // libere y la reanudación pueda abrir el mismo parcial desde el offset persistido.
        handle.CancelActiveTransfer();

        await handle.WaitForActiveRunAsync().ConfigureAwait(false);

        await PersistStateAsync(handle).ConfigureAwait(false);

        Publish(handle);
    }

    /// <summary>
    /// Resumes a paused download operation from its persisted offset (AC3, AC4).
    ///
    /// <para>
    /// La reanudación NO vuelve a cero: el motor pide el rango que empieza en el offset
    /// acumulado y el motor HTTP lo sirve con un <c>Range</c> sobre ese offset (AD-20,
    /// AD-24).
    /// </para>
    /// </summary>
    /// <param name="handle">Handle of the operation to resume.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the operation has resumed.</returns>
    public Task ResumeAsync(DownloadOperationHandle handle, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handle);

        if (!handle.StateMachine.CanTransitionTo(DownloadState.Downloading))
        {
            return Task.CompletedTask;
        }

        handle.ClearPause();

        handle.TrackRun(RunAsync(handle, cancellationToken));

        return Task.CompletedTask;
    }

    /// <summary>
    /// Cancels a download operation and DELETES what it downloaded (AC4, AC5).
    ///
    /// <para>
    /// Cancelar NO es fallar: la cancelación BORRA el archivo parcial y REINICIA el
    /// registro de progreso persistido, así que <c>bytesPersisted</c> vuelve a cero y la
    /// operación NO queda reintentable por reanudación, a diferencia de <c>Failed</c>
    /// (AC5, AD-11).
    /// </para>
    /// </summary>
    /// <param name="handle">Handle of the operation to cancel.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the operation is cancelled.</returns>
    public async Task CancelAsync(DownloadOperationHandle handle, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handle);

        // La cancelación se pide desde cualquier estado que no sea terminal: una descarga
        // EN CURSO también se cancela, y su transición es la que gana la tabla de la
        // máquina (AC5).
        if (!handle.StateMachine.TryTransitionTo(DownloadState.Cancelled))
        {
            return;
        }

        handle.RequestCancel();

        // El archivo parcial puede estar abierto por la transferencia. Se cancela ANTES de
        // borrarlo para que Windows libere el handle y la cancelación sea efectiva (AC5).
        handle.CancelActiveTransfer();

        await handle.WaitForActiveRunAsync().ConfigureAwait(false);

        await PersistStateAsync(handle).ConfigureAwait(false);

        // AC5: se borra el archivo parcial y se reinicia el registro de progreso, así que
        // la descarga siguiente del mismo paquete empieza de CERO y no reutiliza ningún
        // offset de la descarga cancelada.
        await DeletePartialAsync(handle).ConfigureAwait(false);

        await _stateStore.SaveProgressAsync(
            handle.OperationId,
            0,
            handle.TotalBytes,
            cancellationToken)
            .ConfigureAwait(false);

        handle.ResetProgress();

        Publish(handle);
    }

    /// <summary>
    /// Reads the handle of a running operation, or <c>null</c> when the client does not
    /// know it.
    /// </summary>
    /// <param name="operationId">Opaque identifier of the operation.</param>
    /// <returns>The handle of the operation, or <c>null</c>.</returns>
    public DownloadOperationHandle? Find(string operationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);

        lock (_lock)
        {
            return _operations.TryGetValue(operationId, out var handle) ? handle : null;
        }
    }

    /// <summary>
    /// Runs the transfer of the bytes of an operation until it ends, pauses or fails
    /// (AC2, AC3, AC5).
    /// </summary>
    /// <param name="handle">Handle of the operation to run.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the transfer ends.</returns>
    private async Task RunAsync(DownloadOperationHandle handle, CancellationToken cancellationToken)
    {
        // AD-24: el recurso de una descarga es su PAQUETE, y las operaciones que tocan el
        // mismo recurso se serializan. Las de recursos distintos COEXISTEN.
        var resourceLock = AcquireResourceLock(handle.PackageId);
        var runCancellation = handle.BeginRun(cancellationToken);
        var resourceAcquired = false;

        try
        {
            await resourceLock.WaitAsync(runCancellation.Token).ConfigureAwait(false);
            resourceAcquired = true;

            await RunTransferAsync(handle, runCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (handle.IsPauseRequested || handle.IsCancelRequested)
        {
            // La operación se canceló mientras esperaba el recurso de su paquete; nunca
            // llegó a abrir el archivo parcial ni a iniciar una transferencia.
        }
        finally
        {
            if (resourceAcquired)
            {
                resourceLock.Release();
            }

            handle.EndRun(runCancellation);
        }
    }

    /// <summary>
    /// Transfers the bytes of an operation, honoring the limits and the requests of pause
    /// and cancellation (AC2, AC3, AC4).
    /// </summary>
    /// <param name="handle">Handle of the operation to run.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the transfer ends.</returns>
    private async Task RunTransferAsync(DownloadOperationHandle handle, CancellationToken cancellationToken)
    {
        if (!handle.StateMachine.TryTransitionTo(DownloadState.Downloading))
        {
            return;
        }

        var transferCancellation = handle.BeginTransfer(cancellationToken);
        var transferToken = transferCancellation.Token;
        var concurrencyAcquired = false;
        var connectionAcquired = false;

        try
        {
            await PersistStateAsync(handle).ConfigureAwait(false);

            Publish(handle);

            var partialPath = _layout.PartialFilePath(handle.PackageId);

            Directory.CreateDirectory(Path.GetDirectoryName(partialPath)!);

            // AD-14: los límites son POR CLIENTE. La descarga espera su turno y su conexión
            // espera la suya, así que un cliente no abre más descargas ni más conexiones de
            // las que su límite admite (AD-14, AC10).
            await _concurrencySlots.WaitAsync(transferToken).ConfigureAwait(false);
            concurrencyAcquired = true;

            await _connectionSlots.WaitAsync(transferToken).ConfigureAwait(false);
            connectionAcquired = true;

            var offset = handle.BytesPersisted;

            long? total = handle.TotalBytes;

            // AD-11, AD-20: cuando el offset persistido YA IGUALA el total, los bytes
            // están completos en el parcial y la descarga NO se vuelve a transferir:
            // pedir «bytes=<total>-» haría que el servidor respondiera 416, y esa falla
            // no es de transporte, así que la operación pasaría a «Failed» y la
            // descarga COMPLETA se perdería igual (AD-20, AD-24).
            //
            // En su lugar se pasa DIRECTAMENTE a validar los bytes ya descargados, que es
            // lo que decide si el paquete es íntegro (AD-8).
            if (total is not null && offset == total.Value)
            {
                _logger.LogInformation(
                    "La operación {OperationId} del paquete {PackageId} ya tiene los {Bytes} bytes completos: se validan sin volver a transferring.",
                    handle.OperationId,
                    handle.PackageId,
                    offset);

                await ValidateTransferredBytesAsync(handle, partialPath, transferToken).ConfigureAwait(false);

                return;
            }

            // AD-20: la reanudación pide el rango que empieza en el offset acumulado, y
            // el rango NO declara su final: viaja como «bytes=&lt;offset&gt;-», así que el
            // servidor sirve el resto del contenido y el motor lee el total del
            // <c>Content-Range</c> de la respuesta (AD-20, AD-24).
            //
            // No hace falta SONDAR el total antes de transferir: la respuesta del tramo
            // lo declara, y una petición de sondeo sería un bytes extra por cada
            // reanudación (AD-20).
            var end = UnboundedRangeEnd;

            await using var destination = new FileStream(
                partialPath,
                FileMode.OpenOrCreate,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 81920,
                useAsync: true);

            destination.Seek(offset, SeekOrigin.Begin);

            // AD-11, SM-2: el offset se PERSISTE mientras la descarga está en curso, no
            // solo al final del tramo. Una descarga que se interrumpe a mitad conserva lo
            // que ya está en el disco, así que la reanudación continúa desde ahí y no
            // vuelve a cero (AC2).
            //
            // La escritura del offset usa el token NULO a propósito: la escritura tiene
            // que SOBREVIVIR a la cancelación de la transferencia, porque el instante en
            // que se corta la descarga es justo el instante en el que el offset escrito
            // es el que decide de dónde se reanuda (AD-11, SM-2).
            await using var counting = new ProgressCountingStream(
                destination,
                async (accumulated, declaredTotal) =>
                {
                    await _stateStore.SaveProgressAsync(
                        handle.OperationId,
                        accumulated,
                        declaredTotal,
                        CancellationToken.None)
                        .ConfigureAwait(false);

                    // AD-20, UX-DR-5: el total DECLARADO se publica con el offset, así que
                    // la interfaz muestra el porcentaje y el par de tamaños desde el
                    // primer momento.
                    handle.SetProgress(accumulated, declaredTotal ?? handle.TotalBytes);
                },
                offset,
                total);

            var outcome = await _engine
                .TransferAsync(handle.ContentUri, offset, end, counting, transferToken)
                .ConfigureAwait(false);

            if (!outcome.RangeHonoured)
            {
                // AD-20: un 200 significa que el servidor IGNORÓ el rango y sirvió el
                // contenido entero, así que los bytes empiezan por el principio y el
                // offset acumulado no aplica: la transferencia se rehace desde cero.
                destination.Position = 0;
                destination.SetLength(0);
            }

            var transferred = offset + outcome.BytesTransferred;

            total = outcome.TotalBytes ?? total;

            handle.SetProgress(transferred, total);

            await _stateStore.SaveProgressAsync(
                handle.OperationId,
                transferred,
                total,
                transferToken)
                .ConfigureAwait(false);

            Publish(handle);

            // AD-8: la transferencia TERMINÓ y el archivo parcial se cierra ANTES de
            // calcular su hash, porque el hash se calcula sobre el ARCHIVO, no sobre el
            // contenido que todavía puede estar buffered.
            await destination.DisposeAsync().ConfigureAwait(false);

            await ValidateTransferredBytesAsync(handle, partialPath, transferToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (handle.IsCancelRequested)
        {
            // La cancelación es una DECISIÓN del jugador y no una falla: el estado ya
            // está en Cancelled y el parcial ya está borrado (AC5).
            _logger.LogInformation(
                "La descarga {OperationId} del paquete {PackageId} se canceló.",
                handle.OperationId,
                handle.PackageId);
        }
        catch (OperationCanceledException) when (handle.IsPauseRequested)
        {
            // La pausa es una DECISIÓN del jugador: el offset persistido se conserva y la
            // reanudación continúa desde la misma posición (AC3, AC4).
            _logger.LogInformation(
                "La descarga {OperationId} del paquete {PackageId} se pausó en {Bytes} bytes.",
                handle.OperationId,
                handle.PackageId,
                handle.BytesPersisted);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // El cliente se cierra: la operación se queda como estaba y el offset
            // persistido conserva lo que se había descargado (AD-11).
            _logger.LogInformation(
                "La descarga {OperationId} del paquete {PackageId} se interrumpió con el cierre del cliente.",
                handle.OperationId,
                handle.PackageId);
        }
        catch (Exception exception)
        {
            // AD-11, AD-24, SM-2: un fallo de TRANSPORTE (el servidor no está, la red se
            // cayó, la respuesta tardó) NO es una falla de la operación. La operación
            // conserva su parcial y su offset persistido, y su estado sigue siendo
            // REANUDABLE, así que el siguiente arranque reintenta el <c>Range</c> desde el
            // offset persistido en vez de dejar el parcial huérfano (AD-20, AC2).
            //
            // Un fallo que NO es de transporte sí es una falla de la operación: pasa a
            // <c>Failed</c>, que es TERMINAL y no se reanuda (AC5).
            if (DownloadRecovery.IsTransportFailure(exception))
            {
                _logger.LogWarning(
                    exception,
                    "La descarga {OperationId} del paquete {PackageId} falló por transporte y queda reanudable desde {Bytes} bytes.",
                    handle.OperationId,
                    handle.PackageId,
                    handle.BytesPersisted);

                // <c>Downloading</c> a <c>Paused</c> es una transición DECLARADA de la
                // tabla de la máquina, y <c>Paused</c> es REANUDABLE: la operación se
                // persiste así y el siguiente arranque la recupera (AD-10, AD-11).
                handle.StateMachine.TryTransitionTo(DownloadState.Paused);

                await PersistStateAsync(handle).ConfigureAwait(false);

                Publish(handle);

                return;
            }

            // AC5: una FALLA conserva el parcial y su offset, a diferencia de la
            // cancelación: la operación no queda reintentable por reanudación desde este
            // estado, pero los bytes descargados no se pierden (AD-11, AD-24).
            _logger.LogWarning(
                exception,
                "La descarga {OperationId} del paquete {PackageId} falló.",
                handle.OperationId,
                handle.PackageId);

            // AD-11, NFR-7, AC2: el fallo queda REGISTRADO en el registro de diagnóstico
            // del cliente, con su código y sus tres partes, y ASOCIADO a la operación
            // que lo produjo (AD-23).
            await RecordErrorAsync(
                ErrorCatalog.Resolve(ErrorCatalog.UnknownErrorCode),
                handle.OperationId,
                OperationTypeNames.Download,
                cancellationToken).ConfigureAwait(false);

            handle.StateMachine.TryTransitionTo(DownloadState.Failed);

            await PersistStateAsync(handle).ConfigureAwait(false);

            Publish(handle);
        }
        finally
        {
            if (connectionAcquired)
            {
                _connectionSlots.Release();
            }

            if (concurrencyAcquired)
            {
                _concurrencySlots.Release();
            }

            handle.EndTransfer(transferCancellation);
        }
    }

    /// <summary>
    /// Validates the INTEGRITY of the bytes that were transferred and closes the operation
    /// (AD-8, AC2, AC3).
    ///
    /// <para>
    /// AD-8: la máquina de estados va de <c>Downloading</c> a <c>Validating</c> y de ahí
    /// a <c>Completed</c> o a <c>Failed</c>. La comprobación del <c>sha256</c> contra el
    /// manifiesto FIRMADO es la que decide, y es una comprobación DISTINTA de la de
    /// autenticidad (AD-2).
    /// </para>
    /// <para>
    /// AC2: un paquete alterado se RECHAZA antes de la instalación, el archivo parcial se
    /// descarta y la operación se marca como fallida y NO reparable por reanudación
    /// (AD-8, AD-27). El registro de progreso se reinicia a cero, así que la descarga
    /// siguiente del mismo paquete empieza de CERO y no reutiliza el offset de un paquete
    /// dañado.
    /// </para>
    /// </summary>
    /// <param name="handle">Handle of the operation.</param>
    /// <param name="partialPath">Absolute path of the partial file of the package.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the operation has been closed.</returns>
    private async Task ValidateTransferredBytesAsync(
        DownloadOperationHandle handle,
        string partialPath,
        CancellationToken cancellationToken)
    {
        if (_validation is null)
        {
            // Sin el caso de uso de la validación no hay nada que comprobar, así que la
            // operación se cierra como completada (AD-8).
            handle.StateMachine.TryTransitionTo(DownloadState.Completed);

            await PersistStateAsync(handle).ConfigureAwait(false);

            Publish(handle);

            return;
        }

        if (!handle.StateMachine.TryTransitionTo(DownloadState.Validating))
        {
            return;
        }

        // AD-10, AD-11: el estado intermedio «Validating» NO se persiste. Ese estado no
        // es REANUDABLE (la recuperación sólo admite Queued, Downloading y Paused) ni
        // es TERMINAL, así que persistirlo dejaba la operación en un limbo: un cierre del
        // cliente durante el hash (decenas de segundos en un paquete de GB) la abandonaba
        // para siempre, sin fallar y sin reanudar, con su parcial huérfano en
        // «downloads/».
        //
        // Lo que se persiste sigue siendo «Downloading», que ES reanudable: el siguiente
        // arranque recupera la operación y vuelve a validar los bytes ya descargados
        // (AD-11, AD-24).
        Publish(handle);

        PackageValidationResult result;

        try
        {
            await using var content = new FileStream(
                partialPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                useAsync: true);

            result = await _validation
                .ValidateIntegrityAsync(handle.PackageId, content, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // La validación NO pudo leer los bytes: es una falla de la operación, y el
            // parcial se conserva como en cualquier otra falla (AD-11).
            _logger.LogWarning(
                exception,
                "No se pudo verificar la integridad del paquete {PackageId} de la operación {OperationId}.",
                handle.PackageId,
                handle.OperationId);

            handle.SetValidationError(ErrorCatalog.Resolve(ErrorCatalog.UnknownErrorCode));

            // AD-11, NFR-7, AC2: el fallo queda REGISTRADO en el registro de diagnóstico
            // del cliente, con su código y sus tres partes (AD-15, AD-23).
            await RecordErrorAsync(
                ErrorCatalog.Resolve(ErrorCatalog.UnknownErrorCode),
                handle.OperationId,
                OperationTypeNames.Download,
                cancellationToken).ConfigureAwait(false);

            handle.StateMachine.TryTransitionTo(DownloadState.Failed);

            await PersistStateAsync(handle).ConfigureAwait(false);

            Publish(handle);

            return;
        }

        if (result.IsValid)
        {
            handle.StateMachine.TryTransitionTo(DownloadState.Completed);

            await PersistStateAsync(handle).ConfigureAwait(false);

            Publish(handle);

            return;
        }

        // AD-8, AD-27: la integridad NO cuadra. El archivo parcial se descarta, el registro
        // de progreso se reinicia a cero y la operación queda como FALLIDA, que no es
        // reparable por reanudación.
        //
        // La corrupción y la falta de autenticidad son DOS fallos DISTINTOS, con dos
        // códigos estables distintos, así que el log los distingue: un constructor que
        // los colapsa pierde la información que separa un ataque de un archivo dañado
        // (AD-15, AD-25, AD-27).
        //
        // El handle TRANSPORTA el error del catálogo, así que la interfaz muestra la causa
        // diferenciada sin adivinar y el alta de la instalación queda bloqueada con un
        // motivo visible (AC1, AC3).
        handle.SetValidationError(result.Error ?? ErrorCatalog.Resolve(ErrorCatalog.UnknownErrorCode));

        // AD-11, NFR-7, AC2: el fallo queda REGISTRADO en el registro de diagnóstico del
        // cliente, con su código y sus tres partes, y ASOCIADO a la operación que lo
        // produjo (AD-15, AD-23).
        await RecordErrorAsync(
            handle.ValidationError!,
            handle.OperationId,
            OperationTypeNames.Download,
            cancellationToken).ConfigureAwait(false);

        if (result.IsCorrupted)
        {
            _logger.LogWarning(
                "El paquete {PackageId} de la operación {OperationId} está dañado: se descarta el archivo parcial y la operación no se puede reanudar.",
                handle.PackageId,
                handle.OperationId);
        }
        else
        {
            _logger.LogWarning(
                "El paquete {PackageId} de la operación {OperationId} no es de fiar: se descarta el archivo parcial y la operación no se puede reanudar.",
                handle.PackageId,
                handle.OperationId);
        }

        await DeletePartialAsync(handle).ConfigureAwait(false);

        await _stateStore.SaveProgressAsync(
            handle.OperationId,
            0,
            handle.TotalBytes,
            cancellationToken)
            .ConfigureAwait(false);

        handle.ResetProgress();

        handle.StateMachine.TryTransitionTo(DownloadState.Failed);

        await PersistStateAsync(handle).ConfigureAwait(false);

        Publish(handle);
    }

    /// <summary>
    /// Reads the total size of the content when the client does not know it, asking the
    /// server por el rango que empieza en el offset (AD-20).
    /// </summary>
    /// <param name="handle">Handle of the operation.</param>
    /// <param name="offset">Offset accumulated so far.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The total size of the content, or <c>null</c> when it cannot be read.</returns>
    private async Task<long?> ProbeTotalAsync(
        DownloadOperationHandle handle,
        long offset,
        CancellationToken cancellationToken)
    {
        try
        {
            using var probe = new MemoryStream();

            var outcome = await _engine
                .TransferAsync(handle.ContentUri, offset, offset, probe, cancellationToken)
                .ConfigureAwait(false);

            return outcome.TotalBytes;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // El total no es imprescindible para transferir: el motor lo declara en la
            // respuesta del tramo y la interfaz lo muestra cuando llega (AD-20).
            _logger.LogWarning(
                exception,
                "No se pudo leer el tamaño total del paquete {PackageId}.",
                handle.PackageId);

            return null;
        }
    }

    /// <summary>
    /// Persists the state of an operation (AD-10, AD-11).
    /// </summary>
    /// <param name="handle">Handle of the operation.</param>
    /// <returns>A task that completes when the state is persisted.</returns>
    private async Task PersistStateAsync(DownloadOperationHandle handle)
    {
        try
        {
            await _stateStore.SaveOperationAsync(
                handle.OperationId,
                handle.PackageId,
                handle.StateMachine.Current.ToString(),
                TransferEngineRegistry.HttpRangeTransferEngine,
                CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // La persistencia del estado NO puede impedir que la descarga siga: el
            // registro del progreso es lo que la reanudación necesita, y se escribe
            // aparte (AD-11).
            _logger.LogWarning(
                exception,
                "No se pudo persistir el estado de la operación {OperationId}.",
                handle.OperationId);
        }
    }

    /// <summary>
    /// REGISTERS an error of la descarga en el registro de diagnóstico del cliente
    /// (AD-11, AD-15, NFR-7, AC2).
    ///
    /// <para>
    /// El registro NUNCA impide que la descarga siga: el diagnóstico es un complemento y
    /// no su motivo de vida, así que un fallo del registrador se anota y la operación
    /// continúa (AD-1).
    /// </para>
    /// </summary>
    /// <param name="error">Error of the catalog that explains the failure.</param>
    /// <param name="operationId">
    /// Identifier of the operation that failed, or <c>null</c> when the error is not bound
    /// to one (AD-23).
    /// </param>
    /// <param name="operationType">Kind of operation that produced the error (AC2).</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the error has been registered, or has failed.</returns>
    private async Task RecordErrorAsync(
        ApiError error,
        string? operationId,
        string operationType,
        CancellationToken cancellationToken)
    {
        if (_errorRecorder is null)
        {
            return;
        }

        try
        {
            await _errorRecorder
                .RecordAsync(error, operationId, operationType, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(
                exception,
                "No se pudo registrar el error {Code} de la operación {OperationId} en el registro de diagnóstico del cliente.",
                error.Code,
                operationId);
        }
    }

    /// <summary>
    /// Deletes the partial file of an operation (AC5).
    /// </summary>
    /// <param name="handle">Handle of the operation.</param>
    /// <returns>A task that completes when the file is deleted.</returns>
    private async Task DeletePartialAsync(DownloadOperationHandle handle)
    {
        try
        {
            var path = _layout.PartialFilePath(handle.PackageId);

            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(
                exception,
                "No se pudo borrar el archivo parcial del paquete {PackageId}.",
                handle.PackageId);
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the lock of the resource of a package, creating it when it does not exist
    /// (AD-24).
    /// </summary>
    /// <param name="packageId">Opaque identifier of the package.</param>
    /// <returns>The lock of the resource.</returns>
    private SemaphoreSlim AcquireResourceLock(string packageId)
    {
        lock (_lock)
        {
            if (!_resourceLocks.TryGetValue(packageId, out var resourceLock))
            {
                resourceLock = new SemaphoreSlim(1, 1);

                _resourceLocks[packageId] = resourceLock;
            }

            return resourceLock;
        }
    }

    /// <summary>
    /// Publishes the state and the progress of an operation (AC2, UX-DR-13).
    ///
    /// <para>
    /// La publicación del estado va a la interfaz, y la del progreso va al servidor con
    /// el CONTRATO EXACTO <see cref="DownloadProgressUpdated"/>: el identificador del
    /// cliente, el de la operación, los bytes transferidos, el total y el estado de la
    /// máquina (AC5, AD-22).
    /// </para>
    /// <para>
    /// La publicación al servidor NUNCA impide la descarga: una falla del hub se registra
    /// y se sigue, porque lo que sobrevive al reinicio es el offset persistido en el disco
    /// del cliente (AD-11, AD-22).
    /// </para>
    /// </summary>
    /// <param name="handle">Handle of the operation.</param>
    private void Publish(DownloadOperationHandle handle)
    {
        Changed?.Invoke(handle);

        // AD-22, FR-10: la operación TERMINA, así que deja de estar en curso y la lista
        // de operaciones del cliente cambia. El aviso publica el estado COMPLETO, así que
        // el servidor deja de mostrar una descarga que ya terminó (AD-22).
        //
        // El aviso es de MEJOR ESFUERZO y NUNCA impide que la descarga siga: lo que
        // sobrevive al reinicio es el offset persistido en el disco del cliente (AD-11,
        // AD-22).
        if (IsTerminal(handle.StateMachine.Current))
        {
            NotifyStateChanged();
        }

        if (_reporter is null)
        {
            return;
        }

        // La publicación al servidor es ASÍNCRONA y no bloquea a la interfaz: la tarea se
        // REGISTRA para que el cierre del cliente pueda esperarla y para que la prueba
        // pueda observarla (AD-22).
        var report = ReportAsync(handle);

        lock (_lock)
        {
            _pendingReports.Add(report);
        }

        _ = report.ContinueWith(
            completed =>
            {
                lock (_lock)
                {
                    _pendingReports.Remove(completed);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>
    /// Avisa de que el estado del cliente cambió, sin esperar al servidor: la
    /// notificación es de MEJOR ESFUERZO y NUNCA impide que la descarga siga (AD-1,
    /// AD-11, AD-22).
    /// </summary>
    private void NotifyStateChanged()
    {
        if (_stateNotifier is null)
        {
            return;
        }

        var notification = NotifyStateChangedAsync(CancellationToken.None);

        lock (_lock)
        {
            _pendingReports.Add(notification);
        }

        _ = notification.ContinueWith(
            completed =>
            {
                lock (_lock)
                {
                    _pendingReports.Remove(completed);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>
    /// Avisa de que el estado del cliente cambió, y ESPERA al servidor (AD-22).
    ///
    /// <para>
    /// La espera es la del ARRANQUE de una descarga: la operación ya está persistida, así
    /// que esperar al servidor no pone en riesgo el offset (AD-11).
    /// </para>
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the notification.</param>
    /// <returns>A task that completes when the notification has been attempted.</returns>
    private async Task NotifyStateChangedAsync(CancellationToken cancellationToken)
    {
        if (_stateNotifier is null)
        {
            return;
        }

        try
        {
            await _stateNotifier
                .NotifyAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // La notificación del cambio de estado NO puede impedir que la descarga
            // siga: el offset persistido es lo que sobrevive al reinicio (AD-11, AD-22).
            _logger.LogWarning(
                exception,
                "No se pudo publicar el cambio de estado del cliente tras la operación de descarga: la descarga sigue.");
        }
    }

    /// <summary>
    /// Tells whether a state of the download machine is TERMINAL, y la operación deja de
    /// estar en curso (AD-10).
    /// </summary>
    /// <param name="state">State of the download machine.</param>
    /// <returns><c>true</c> when the state is terminal.</returns>
    private static bool IsTerminal(DownloadState state) =>
        state is DownloadState.Completed or DownloadState.Cancelled or DownloadState.Failed;

    /// <summary>
    /// Waits until every publication of the progress that is in flight has finished
    /// (AD-22).
    /// </summary>
    /// <returns>A task that completes when the publications have finished.</returns>
    public async Task WaitForPendingReportsAsync()
    {
        Task[] pending;

        lock (_lock)
        {
            pending = _pendingReports.ToArray();
        }

        if (pending.Length > 0)
        {
            await Task.WhenAll(pending).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Reports the progress of an operation to the server (AC5, AD-22).
    /// </summary>
    /// <param name="handle">Handle of the operation.</param>
    /// <returns>A task that completes when the progress has been reported.</returns>
    private async Task ReportAsync(DownloadOperationHandle handle)
    {
        try
        {
            var clientId = await _stateStore
                .LoadIdentityAsync(CancellationToken.None)
                .ConfigureAwait(false);

            if (clientId is null)
            {
                return;
            }

            var progress = handle.Progress;

            await _reporter!
                .ReportAsync(
                    new DownloadProgressUpdated(
                        clientId.CanonicalId,
                        handle.OperationId,
                        progress.BytesTransferred,
                        progress.TotalBytes ?? 0L,
                        handle.StateMachine.Current),
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // La proyección del progreso NO puede impedir que la descarga siga: el offset
            // persistido es lo que sobrevive al reinicio (AD-11, AD-22).
            _logger.LogWarning(
                exception,
                "No se pudo reportar el progreso de la operación {OperationId}.",
                handle.OperationId);
        }
    }
}

/// <summary>
/// Handle of a download operation of the client (AD-10, AC2).
///
/// <para>
/// El handle es la operación VIVA del cliente: lleva su identificador, su máquina de
/// estados, su offset acumulado y las peticiones de pausa y cancelación (AD-10, AD-11).
/// </para>
/// </summary>
public sealed class DownloadOperationHandle
{
    private readonly DownloadStateMachine _stateMachine;
    private readonly Lock _lock = new();

    private long _bytesPersisted;
    private long? _totalBytes;
    private bool _pauseRequested;
    private bool _cancelRequested;
    private ApiError? _validationError;
    private CancellationTokenSource? _transferCancellation;
    private CancellationTokenSource? _runCancellation;
    private Task? _activeRun;

    /// <summary>Creates the handle of an operation.</summary>
    /// <param name="operationId">Opaque identifier of the operation.</param>
    /// <param name="packageId">Opaque identifier of the package that the operation transfers.</param>
    /// <param name="contentUri">Absolute URL of the content of the package.</param>
    /// <param name="stateMachine">State machine of the operation.</param>
    public DownloadOperationHandle(
        string operationId,
        string packageId,
        Uri contentUri,
        DownloadStateMachine stateMachine)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentNullException.ThrowIfNull(contentUri);
        ArgumentNullException.ThrowIfNull(stateMachine);

        OperationId = operationId;
        PackageId = packageId;
        ContentUri = contentUri;
        _stateMachine = stateMachine;
    }

    /// <summary>Opaque identifier of the operation.</summary>
    public string OperationId { get; }

    /// <summary>Opaque identifier of the package that the operation transfers.</summary>
    public string PackageId { get; }

    /// <summary>Absolute URL of the content of the package.</summary>
    public Uri ContentUri { get; }

    /// <summary>State machine of the operation.</summary>
    public DownloadStateMachine StateMachine => _stateMachine;

    /// <summary>Accumulated offset that is persisted.</summary>
    public long BytesPersisted
    {
        get
        {
            lock (_lock)
            {
                return _bytesPersisted;
            }
        }
    }

    /// <summary>
    /// Total size of the content, or <c>null</c> when the server has not declared it.
    /// </summary>
    public long? TotalBytes
    {
        get
        {
            lock (_lock)
            {
                return _totalBytes;
            }
        }
    }

    /// <summary>True when the player asked to pause the operation.</summary>
    public bool IsPauseRequested
    {
        get
        {
            lock (_lock)
            {
                return _pauseRequested;
            }
        }
    }

    /// <summary>True when the player asked to cancel the operation.</summary>
    public bool IsCancelRequested
    {
        get
        {
            lock (_lock)
            {
                return _cancelRequested;
            }
        }
    }

    /// <summary>Progress of the operation, with its percentage and its pair of sizes.</summary>
    public DownloadProgress Progress => new(BytesPersisted, TotalBytes);

    /// <summary>
    /// The error of the catalog that explains WHY the validation of the package failed,
    /// or <c>null</c> when the package has not failed its validation (AD-8, AD-15, AC3).
    ///
    /// <para>
    /// El error del catálogo es el que lleva la CAUSA diferenciada: la corrupción y la
    /// falta de autenticidad son dos fallos distintos, y el handle los transporta para
    /// que la interfaz pueda mostrarlos sin adivinar (AD-27, AC6).
    /// </para>
    /// </summary>
    public ApiError? ValidationError
    {
        get
        {
            lock (_lock)
            {
                return _validationError;
            }
        }
    }

    /// <summary>
    /// Records the error of the catalog that explains the failure of the validation of the
    /// package (AD-8, AD-15).
    /// </summary>
    /// <param name="error">Error of the catalog, or <c>null</c> to clear it.</param>
    internal void SetValidationError(ApiError? error)
    {
        lock (_lock)
        {
            _validationError = error;
        }
    }

    /// <summary>
    /// Attaches the callback that publishes the state and the progress of the operation.
    /// </summary>
    /// <param name="publish">Callback that publishes the changes.</param>
    internal void Attach(Action<DownloadOperationHandle> publish)
    {
        ArgumentNullException.ThrowIfNull(publish);

        _publish = publish;
    }

    private Action<DownloadOperationHandle>? _publish;

    /// <summary>Asks the operation to pause.</summary>
    internal void RequestPause()
    {
        lock (_lock)
        {
            _pauseRequested = true;
        }
    }

    /// <summary>Clears the request of pause, so the operation can continue.</summary>
    internal void ClearPause()
    {
        lock (_lock)
        {
            _pauseRequested = false;
        }
    }

    /// <summary>Asks the operation to cancel.</summary>
    internal void RequestCancel()
    {
        lock (_lock)
        {
            _cancelRequested = true;
        }
    }

    /// <summary>Starts the cancellation scope of one transfer attempt.</summary>
    /// <param name="cancellationToken">Token of the application lifetime.</param>
    /// <returns>The source that owns this transfer attempt.</returns>
    internal CancellationTokenSource BeginTransfer(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _transferCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            return _transferCancellation;
        }
    }

    /// <summary>Cancels the current run, whether it is waiting or transferring bytes.</summary>
    internal void CancelActiveTransfer()
    {
        lock (_lock)
        {
            _runCancellation?.Cancel();
            _transferCancellation?.Cancel();
        }
    }

    /// <summary>Starts the cancellation scope of one run, including its resource wait.</summary>
    /// <param name="cancellationToken">Token of the application lifetime.</param>
    /// <returns>The source that owns this run.</returns>
    internal CancellationTokenSource BeginRun(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            return _runCancellation;
        }
    }

    /// <summary>Closes the cancellation scope when its run ended.</summary>
    /// <param name="runCancellation">Source created for that run.</param>
    internal void EndRun(CancellationTokenSource runCancellation)
    {
        lock (_lock)
        {
            if (ReferenceEquals(_runCancellation, runCancellation))
            {
                _runCancellation = null;
            }
        }

        runCancellation.Dispose();
    }

    /// <summary>Closes the cancellation scope when its transfer attempt ended.</summary>
    /// <param name="transferCancellation">Source created for that attempt.</param>
    internal void EndTransfer(CancellationTokenSource transferCancellation)
    {
        lock (_lock)
        {
            if (ReferenceEquals(_transferCancellation, transferCancellation))
            {
                _transferCancellation = null;
            }
        }

        transferCancellation.Dispose();
    }

    /// <summary>Tracks the task that owns the current attempt over this package.</summary>
    /// <param name="run">Task that is transferring or waiting for the package resource.</param>
    internal void TrackRun(Task run)
    {
        ArgumentNullException.ThrowIfNull(run);

        lock (_lock)
        {
            _activeRun = run;
        }
    }

    /// <summary>Waits until the current attempt released its partial file and resource lock.</summary>
    public async Task WaitForActiveRunAsync()
    {
        Task? run;

        lock (_lock)
        {
            run = _activeRun;
        }

        if (run is not null)
        {
            await run.ConfigureAwait(false);
        }
    }

    /// <summary>Records the offset and the total of the operation.</summary>
    /// <param name="bytesPersisted">Accumulated offset.</param>
    /// <param name="totalBytes">Total size of the content, or <c>null</c> when it is unknown.</param>
    internal void SetProgress(long bytesPersisted, long? totalBytes)
    {
        lock (_lock)
        {
            _bytesPersisted = bytesPersisted;
            _totalBytes = totalBytes;
        }

        _publish?.Invoke(this);
    }

    /// <summary>
    /// Resets the offset of the operation, as the cancellation does (AC5).
    /// </summary>
    internal void ResetProgress()
    {
        lock (_lock)
        {
            _bytesPersisted = 0;
        }

        _publish?.Invoke(this);
    }
}

/// <summary>
/// The signed manifest of a package did not pass the validation of authenticity
/// (AD-2, AD-8, AC3, AC4).
///
/// <para>
/// La excepción se lanza ANTES de registrar la intención de la descarga, así que no hay
/// operación registrada, ni archivo parcial, ni manifiesto en la caché: el rechazo
/// precede a persistir (AD-2, AD-27).
/// </para>
/// <para>
/// La excepción lleva el RESULTADO de la validación, con su error del catálogo y su causa,
/// así que el diagnóstico distingue la corrupción de la falta de autenticidad (AC6).
/// </para>
/// </summary>
public sealed class PackageValidationFailedException : Exception
{
    /// <summary>Creates the exception of a package that is not trustworthy.</summary>
    /// <param name="packageId">Opaque identifier of the package that failed the validation.</param>
    /// <param name="result">Result of the validation that rejected the package.</param>
    public PackageValidationFailedException(string packageId, PackageValidationResult result)
        : base($"El manifiesto firmado del paquete '{packageId}' no ha superado la validación.")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentNullException.ThrowIfNull(result);

        PackageId = packageId;
        Result = result;
    }

    /// <summary>Opaque identifier of the package that failed the validation.</summary>
    public string PackageId { get; }

    /// <summary>Result of the validation that rejected the package.</summary>
    public PackageValidationResult Result { get; }
}
