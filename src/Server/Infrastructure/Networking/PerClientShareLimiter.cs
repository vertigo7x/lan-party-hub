namespace LANPartyHub.Server.Infrastructure.Networking;

/// <summary>
/// Reparto equitativo del enlace entre los clientes que descargan a la vez (AD-14).
///
/// <para>
/// La unidad de reparto es el CLIENTE y no la operación ni la conexión: un cliente que abre
/// varias descargas simultáneas bebe del mismo cubo que las demás conexiones de ese cliente,
/// así que un equipo con diez descargas no se lleva diez veces el ancho de banda de un
/// equipo con una (AD-14).
/// </para>
/// <para>
/// El límite es el número de RESPUESTAS DE RANGO simultáneas que el servidor admite por
/// cliente. Cuando un cliente tiene más descargas abiertas que su límite, las conexiones
/// que superan el límite ESPERAN a que una de las que ya estaban termine su tramo, y el
/// servicio se reparte por orden de llegada: el cliente que espera no pierde los bytes que
/// ya tenía y reanuda desde su offset cuando le llega el turno (AD-20, AD-24).
/// </para>
/// <para>
/// El reparto NO es una garantía de igualdad absoluta: el TCP no la ofrece, y dos sockets
/// del mismo equipo pueden avanzar a ritmos distintos. Lo que el límite garantiza es que
/// un cliente no puede abrir más conexiones simultáneas que su límite, que es lo que
/// impide que un único equipo agote el enlace del evento (AD-14).
/// </para>
/// </summary>
public sealed class PerClientShareLimiter
{
    private readonly Dictionary<string, ClientSlots> _clients = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();
    private readonly int _defaultLimit;

    /// <summary>Creates the limiter of the share of the link.</summary>
    /// <param name="defaultLimit">
    /// Number of simultaneous range responses that the server admits per client when no
    /// source fixes another one. It is the default of AD-14 and it never depends on the
    /// result of a spike (AD-14, NFR-6).
    /// </param>
    public PerClientShareLimiter(int defaultLimit)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(defaultLimit, 1);

        _defaultLimit = defaultLimit;
    }

    /// <summary>
    /// Number of simultaneous range responses that the server admits per client when no
    /// source fixes another one (AD-14).
    /// </summary>
    public int DefaultLimit => _defaultLimit;

    /// <summary>
    /// Waits until the client has a free slot and takes it.
    ///
    /// <para>
    /// La espera es JUSTA por orden de llegada: cada cliente tiene su propia fila, así que
    /// un cliente con muchas descargas no puede ocupar todos los huecos del servidor y
    /// dejar a los demás clientes sin turno (AD-14).
    /// </para>
    /// </summary>
    /// <param name="clientId">
    /// Identifier of the client whose slot is taken. It is the identifier of la
    /// correlación, no una credencial (AD-23, AD-30).
    /// </param>
    /// <param name="cancellationToken">Token that cancels the wait.</param>
    /// <returns>
    /// A lease that frees the slot when it is disposed. The caller has to dispose it when
    /// the response of the range ends.
    /// </returns>
    public async Task<ShareLease> AcquireAsync(string clientId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

        ClientSlots slots;

        lock (_lock)
        {
            if (!_clients.TryGetValue(clientId, out slots!))
            {
                slots = new ClientSlots(_defaultLimit);

                _clients[clientId] = slots;
            }
        }

        await slots.WaitAsync(cancellationToken).ConfigureAwait(false);

        return new ShareLease(slots.Release);
    }
    /// <summary>
    /// Forgets the slots of a client. The server calls it when the client disconnects, so
    /// a client that leaves the evento no deja su turno reservado para siempre.
    /// </summary>
    /// <param name="clientId">Identifier of the client that left.</param>
    public void Forget(string clientId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

        lock (_lock)
        {
            _clients.Remove(clientId);
        }
    }

    /// <summary>
    /// The slots of one client: a counting semaphore with its own queue.
    /// </summary>
    private sealed class ClientSlots
    {
        private readonly SemaphoreSlim _semaphore;

        public ClientSlots(int limit) => _semaphore = new SemaphoreSlim(limit, limit);

        public Task WaitAsync(CancellationToken cancellationToken) => _semaphore.WaitAsync(cancellationToken);

        public void Release() => _semaphore.Release();
    }

    /// <summary>
    /// Lease of one slot of the share of a client. It frees the slot when it is disposed,
    /// and it is disposed exactly once, because an <c>Interlocked</c> lo garantiza.
    /// </summary>
    public sealed class ShareLease : IDisposable
    {
        private readonly Action _release;
        private int _released;

        internal ShareLease(Action release) => _release = release;

        /// <inheritdoc />
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                _release();
            }
        }
    }
}
