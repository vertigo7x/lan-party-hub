using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LANPartyHub.Client.Host;

/// <summary>
/// Acceso a la base de datos del cliente con la CONEXIÓN que lo sostiene (AD-11, AD-22).
///
/// <para>
/// El <see cref="ClientStateDbContext"/> de la publicación del estado se abre sobre el
/// MISMO archivo SQLite que el arranque ya migró, así que la publicación ve las
/// instalaciones que el cliente ya registró (AD-11, AD-22).
/// </para>
/// <para>
/// El contexto NO se deja vivo: el alquiler lo DISPONE cuando la sesión del cliente
/// termina. Un contexto abierto sobre <c>state.sqlite</c> RETIENE la conexión con el
/// archivo y compite con los demás contextos que el cliente abre sobre la misma base,
/// así que un contexto sin disponer es una conexión retenida (AD-11).
/// </para>
/// </summary>
public sealed class ClientStateStoreLease : IDisposable
{
    private readonly ClientStateDbContext _context;
    private bool _disposed;

    /// <summary>Creates the lease over the given context.</summary>
    /// <param name="context">Database of the client that the lease owns.</param>
    /// <param name="store">Adapter over the database.</param>
    public ClientStateStoreLease(ClientStateDbContext context, IClientStateStore store)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(store);

        _context = context;
        Store = store;
    }

    /// <summary>
    /// The adapter over the database of the client, que es la fuente de las
    /// instalaciones y de las operaciones REALES que el cliente publica (AD-22).
    /// </summary>
    public IClientStateStore Store { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _context.Dispose();
    }
}
