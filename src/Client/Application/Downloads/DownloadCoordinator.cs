using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Domain.StateMachines;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Client.Application.Downloads;

/// <summary>
/// Coordinator of the download operations of the client (AD-4, AD-10, AD-11, AD-14, AD-20,
/// AD-24, AC1..AC10).
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

    private readonly ITransferEngine _engine;
    private readonly IDownloadIntentClient _intents;
    private readonly IClientStateStore _stateStore;
    private readonly IClientDataLayout _layout;
    private readonly ILogger<DownloadCoordinator> _logger;
    private readonly int _maxConcurrentDownloads;
    private readonly int _maxTotalConnections;

    private readonly Dictionary<string, DownloadOperationHandle> _operations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SemaphoreSlim> _resourceLocks = new(StringComparer.Ordinal);
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
    /// <param name="logger">Log of the coordinator.</param>
    /// <param name="maxConcurrentDownloads">
    /// Number of downloads that the client runs at the same time. It is CONFIGURABLE and
    /// tiene valor por defecto (AD-14, AC10).
    /// </param>
    /// <param name="maxTotalConnections">
    /// Number of connections that the client opens in total. It is CONFIGURABLE y tiene
    /// valor por defecto (AD-14, AC10).
    /// </param>
    public DownloadCoordinator(
        ITransferEngine engine,
        IDownloadIntentClient intents,
        IClientStateStore stateStore,
        IClientDataLayout layout,
        ILogger<DownloadCoordinator> logger,
        int maxConcurrentDownloads = DefaultMaxConcurrentDownloads,
        int maxTotalConnections = DefaultMaxTotalConnections)
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
        _logger = logger;
        _maxConcurrentDownloads = maxConcurrentDownloads;
        _maxTotalConnections = maxTotalConnections;

        _concurrencySlots = new SemaphoreSlim(maxConcurrentDownloads, maxConcurrentDownloads);
        _connectionSlots = new SemaphoreSlim(maxTotalConnections, maxTotalConnections);
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
    /// La secuencia es la del caso de uso: el cliente REGISTRA la intención con el
    /// servidor, PERSISTE la operación con su identificador, y solo entonces pide los
    /// bytes. La operación es una unidad de trabajo con su propio <c>operationId</c> y su
    /// registro de estado persistido (AC1, AC10).
    /// </para>
    /// </summary>
    /// <param name="packageId">Opaque identifier of the package that the client wants.</param>
    /// <param name="contentUri">Absolute URL of the content of the package.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The handle of the started operation.</returns>
    public async Task<DownloadOperationHandle> StartAsync(
        string packageId,
        Uri contentUri,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentNullException.ThrowIfNull(contentUri);

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

        handle.Attach(Publish);

        handle.TrackRun(RunAsync(handle, cancellationToken));

        return handle;
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

                if (offset > 0)
                {
                    // La reanudación pide el rango que empieza en el offset acumulado: el
                    // motor HTTP lo sirve con un Range sobre ese offset (AD-20, AD-24).
                    total = await ProbeTotalAsync(handle, offset, transferToken).ConfigureAwait(false);
                }

                await using var destination = new FileStream(
                    partialPath,
                    FileMode.OpenOrCreate,
                    FileAccess.Write,
                    FileShare.Read,
                    bufferSize: 81920,
                    useAsync: true);

                destination.Seek(offset, SeekOrigin.Begin);

                var end = total is { } declared ? declared - 1 : long.MaxValue - 1;

                var outcome = await _engine
                    .TransferAsync(handle.ContentUri, offset, end, destination, transferToken)
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
            // AC5: una FALLA conserva el parcial y su offset, a diferencia de la
            // cancelación: la operación no queda reintentable por reanudación desde este
            // estado, pero los bytes descargados no se pierden (AD-11, AD-24).
            _logger.LogWarning(
                exception,
                "La descarga {OperationId} del paquete {PackageId} falló.",
                handle.OperationId,
                handle.PackageId);

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
    /// </summary>
    /// <param name="handle">Handle of the operation.</param>
    private void Publish(DownloadOperationHandle handle) => Changed?.Invoke(handle);
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
    internal async Task WaitForActiveRunAsync()
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
