namespace LANPartyHub.Client.Application.Downloads;

/// <summary>
/// Stream that counts the bytes that reach the partial file and PERSISTS the accumulated
/// offset while the download is in course (AD-11, AD-24, SM-2).
///
/// <para>
/// La persistencia del progreso NO puede esperar al final del tramo: una descarga que se
/// interrumpe a mitad habría perdido todo el offset y la reanudación volvería a cero,
/// que es justo lo que la historia prohíbe (SM-2, AC2).
/// </para>
/// <para>
/// El contador es el OFFSET ACUMULADO, no un mapa de fragmentos: bytes válidos contiguos
/// desde el inicio del parcial, que es lo que la tabla del progreso guarda (AD-24,
/// AD-28).
/// </para>
/// <para>
/// La persistencia es ESPACIADA: escribir en el disco del cliente en cada bloque de ocho
/// kilobytes sería un atropello de escrituras, así que el offset se escribe cada vez que
/// se acumulan los bytes de un tramo (AD-11, NFR-3).
/// </para>
/// </summary>
public sealed class ProgressCountingStream : Stream, IContentTotalAware
{
    /// <summary>
    /// Number of bytes que se acumulan antes de escribir el offset en el disco (AD-11).
    ///
    /// <para>
    /// El intervalo es un COMPROMISO entre la frecuencia de las escrituras y lo que se
    /// puede perder: un corte de corriente pierde como mucho un tramo, y un tramo de
    /// dieciséis kilobytes es una parada de red, no una descarga entera (AD-11, NFR-3).
    /// </para>
    /// </summary>
    public const long PersistenceInterval = 16 * 1024L;

    private readonly Stream _inner;
    private readonly Func<long, long?, Task> _persist;
    private readonly Lock _lock = new();

    private long _written;
    private long _lastPersisted;
    private long? _totalBytes;
    private bool _totalPersisted;
    private Task _totalWrite = Task.CompletedTask;

    /// <summary>Creates the counting stream over the partial file.</summary>
    /// <param name="inner">Stream that receives the bytes of the download.</param>
    /// <param name="persist">
    /// Callback that persists the accumulated offset and the total of the content.
    /// </param>
    /// <param name="initialOffset">
    /// Offset that the transfer starts from, so el contador es ACUMULADO y no el del
    /// último tramo (AD-24).
    /// </param>
    /// <param name="totalBytes">Total of the content, when it is known.</param>
    public ProgressCountingStream(
        Stream inner,
        Func<long, long?, Task> persist,
        long initialOffset = 0,
        long? totalBytes = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(persist);

        _inner = inner;
        _persist = persist;
        _written = initialOffset;
        _lastPersisted = initialOffset;
        _totalBytes = totalBytes;
    }

    /// <summary>Number of bytes accumulated so far, including the initial offset.</summary>
    public long Written
    {
        get
        {
            lock (_lock)
            {
                return _written;
            }
        }
    }

    /// <summary>Total of the content, when it is known (AD-20).</summary>
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

    /// <summary>
    /// Declares the total size of the content, que el motor lee de la respuesta antes de
    /// servir el cuerpo (AD-20).
    ///
    /// <para>
    /// El total se PERSISTE en cuanto llega, aunque no se haya escrito ningún byte
    /// todavía: el total es lo que permite que la interfaz muestre el porcentaje y el par
    /// de tamaños de una descarga que acaba de empezar, y lo que permite que una
    /// descarga interrumpida conserve el tamaño del paquete (AD-11, UX-DR-5).
    /// </para>
    /// </summary>
    /// <param name="totalBytes">Total size of the content in exact bytes.</param>
    public void DeclareTotal(long totalBytes)
    {
        if (totalBytes <= 0)
        {
            return;
        }

        long accumulated;

        lock (_lock)
        {
            _totalBytes = totalBytes;

            if (_totalPersisted)
            {
                return;
            }

            _totalPersisted = true;

            accumulated = _written;
        }

        // La escritura del total NO puede esperar: el motor lo declara antes de servir
        // el primer byte, así que la descarga puede cortar en cualquier momento y el
        // total tiene que estar YA en el disco (AD-11, SM-2).
        _totalWrite = PersistAsync(accumulated, totalBytes);
    }    /// <inheritdoc />
    public override bool CanRead => false;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => true;

    /// <inheritdoc />
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override void Flush() => _inner.Flush();

    /// <inheritdoc />
    public override Task FlushAsync(CancellationToken cancellationToken) =>
        _inner.FlushAsync(cancellationToken);

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    /// <inheritdoc />
    public override async Task WriteAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        await _inner.WriteAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);

        await CountAsync(count, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        await _inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);

        await CountAsync(buffer.Length, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Counts the bytes that landed in the partial file and persists the offset cuando
    /// se ha acumulado un tramo entero (AD-11).
    /// </summary>
    /// <param name="count">Number of bytes that were written.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the offset has been persisted.</returns>
    private async Task CountAsync(int count, CancellationToken cancellationToken)
    {
        // La escritura del total tiene que estar EN EL DISCO antes de que la descarga
        // siga: si el equipo se apaga en este instante, el total del paquete se pierde y
        // la reanudación no puede mostrar el porcentaje (AD-11, SM-2).
        await _totalWrite.ConfigureAwait(false);

        long accumulated;
        bool due;

        lock (_lock)
        {
            _written += count;

            accumulated = _written;

            due = accumulated - _lastPersisted >= PersistenceInterval;
        }

        if (!due)
        {
            return;
        }

        lock (_lock)
        {
            _lastPersisted = accumulated;
        }

        await PersistAsync(accumulated, TotalBytes).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes the accumulated offset y el total en el disco del cliente (AD-11).
    /// </summary>
    /// <param name="accumulated">Accumulated offset.</param>
    /// <param name="total">Total of the content, when it is known.</param>
    /// <returns>A task that completes when the offset has been persisted.</returns>
    private async Task PersistAsync(long accumulated, long? total)
    {
        await _persist(accumulated, total).ConfigureAwait(false);
    }
}
