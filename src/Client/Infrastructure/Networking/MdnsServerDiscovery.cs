using Haukcode.Mdns;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Client.Infrastructure.Networking;

/// <summary>
/// Searches the event server with the browse of mDNS over UDP 5353 (AC1, AC2, AD-3,
/// AD-12).
///
/// <para>
/// The adapter is the ONLY place that knows the library of mDNS: the types of the library
/// stay inside this layer and the use case of the discovery only knows the port that the
/// Application layer declares (AD-1).
/// </para>
/// <para>
/// The mechanism is EXCLUSIVELY the browse of mDNS of the service
/// <c>_lanpartyhub._tcp</c> (RFC 6762/6763): no unicast sweep of the subnet, no SSDP and no
/// DNS-SRV of an external network (AC2, AD-3).
/// </para>
/// <para>
/// The adapter NEVER publishes the fingerprint of the trust anchor and NEVER authenticates
/// the client: the discovery only says which servers are on the network, it does not make
/// any of them trustworthy (AD-2, AD-6, AD-27).
/// </para>
/// </summary>
public sealed class MdnsServerDiscovery : IServerDiscovery
{
    /// <summary>
    /// Service type that the event server announces (AD-3).
    /// </summary>
    public const string ServiceType = "_lanpartyhub._tcp";

    private readonly ILogger<MdnsServerDiscovery> _logger;
    private readonly Lock _gate = new();
    private readonly List<ServerEndpoint> _endpoints = [];
    private readonly HashSet<(string Address, int Port)> _seen = [];

    /// <summary>Creates the adapter of the discovery of the event server.</summary>
    /// <param name="logger">Log of the adapter.</param>
    public MdnsServerDiscovery(ILogger<MdnsServerDiscovery> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ServerEndpoint>> DiscoverAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);

        var browser = new MdnsBrowser(ServiceType);

        // El browse contesta en el hilo que recibe los paquetes, así que la colección se
        // protege con un candado: el resultado se lee cuando el browse ya está cerrado y
        // un paquete tardío no puede tocarlo (AC1).
        lock (_gate)
        {
            _endpoints.Clear();
            _seen.Clear();
        }

        try
        {
            browser.ServiceFound += Collect;

            browser.Start();

            await Task.Delay(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // La cancelación la pidió el jugador: el browse se cierra y la cancelación
            // viaja intacta (AC4).
            throw;
        }
        catch (Exception exception)
        {
            // La red puede bloquear el multicast: el fallo NO es fatal para el cliente y
            // el caso de uso lo traduce a «bloqueada» (AC4, AD-3).
            _logger.LogWarning(
                exception,
                "No se pudo navegar por mDNS el servicio {ServiceType}: la búsqueda del servidor termina como bloqueada.",
                ServiceType);

            throw;
        }
        finally
        {
            await browser.DisposeAsync().ConfigureAwait(false);
        }

        lock (_gate)
        {
            return _endpoints.ToArray();
        }
    }

    /// <summary>
    /// Turns a service that answered the browse into the contract of the endpoint and
    /// keeps it only once (AC1).
    /// </summary>
    /// <param name="profile">Service that answered the browse.</param>
    private void Collect(ServiceProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        // El SRV lleva el puerto del HTTP del servidor y el registro A/AAAA lleva su
        // dirección: los dos juntos son el mismo contrato que usa la dirección manual
        // (AC1, AD-3).
        foreach (var address in profile.Addresses)
        {
            if (address is null)
            {
                continue;
            }

            var endpoint = new ServerEndpoint(address.ToString(), profile.Port);

            lock (_gate)
            {
                // El mismo servidor puede contestar más de una vez por el mismo browse,
                // así que el par de dirección y puerto se conserva UNA sola vez (AC1).
                if (_seen.Add((endpoint.Address, endpoint.Port)))
                {
                    _endpoints.Add(endpoint);
                }
            }
        }
    }
}
