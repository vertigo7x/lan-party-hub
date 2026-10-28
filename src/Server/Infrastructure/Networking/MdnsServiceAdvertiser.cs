using Haukcode.Mdns;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Server.Infrastructure.Networking;

/// <summary>
/// Publishes the mDNS records of the event server (AD-3, AD-12).
///
/// It is the seam that isolates the mDNS library: the types of the library stay inside the
/// infrastructure and only this implementation has to change if the library does.
/// </summary>
public interface IMdnsPublisher : IAsyncDisposable
{
    /// <summary>Name of the published instance.</summary>
    string InstanceName { get; }

    /// <summary>TCP port published in the SRV record.</summary>
    int Port { get; }

    /// <summary>
    /// Publishes the records of the service. It throws when the announcement cannot be
    /// emitted, and the caller decides what to do about it.
    /// </summary>
    void Publish();
}

/// <summary>
/// Publishes <c>_lanpartyhub._tcp</c> over UDP 5353 with the mDNS library (AD-3, AD-12).
///
/// The library owns the details of the announcement: it binds the socket of every suitable
/// adapter of the host and sends the packets with a multicast TTL of 255, as RFC 6762
/// requires.
/// </summary>
public sealed class HaukcodeMdnsPublisher : IMdnsPublisher
{
    private readonly string _serviceType;
    private readonly IReadOnlyDictionary<string, string> _properties;

    private MdnsAdvertiser? _advertiser;

    /// <summary>Creates the publisher.</summary>
    /// <param name="instanceName">Name of the instance; the name of the machine of the host.</param>
    /// <param name="serviceType">Service type to publish.</param>
    /// <param name="port">TCP port of the HTTP surface, published in the SRV record.</param>
    /// <param name="properties">Records of the TXT entry.</param>
    public HaukcodeMdnsPublisher(
        string instanceName,
        string serviceType,
        int port,
        IReadOnlyDictionary<string, string> properties)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceType);
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);
        ArgumentNullException.ThrowIfNull(properties);

        InstanceName = instanceName;
        _serviceType = serviceType;
        Port = port;
        _properties = properties;
    }

    /// <inheritdoc />
    public string InstanceName { get; }

    /// <inheritdoc />
    public int Port { get; }

    /// <inheritdoc />
    public void Publish()
    {
        var profile = new ServiceProfile(InstanceName, _serviceType, (ushort)Port, _properties);

        var advertiser = new MdnsAdvertiser(profile);

        try
        {
            advertiser.Start();
        }
        catch
        {
            // The socket of UDP 5353 can already be bound when the start fails, so the
            // half initialized advertiser has to release it before the failure leaves.
            try
            {
                advertiser.Dispose();
            }
            catch (Exception disposeException)
            {
                // The failure of the start is the one that matters and it is the one that
                // travels to the caller.
                _ = disposeException;
            }

            throw;
        }

        _advertiser = advertiser;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        var advertiser = _advertiser;
        _advertiser = null;

        if (advertiser is null)
        {
            return;
        }

        // Disposing sends the goodbye packets of RFC 6762, so the clients of the network
        // stop seeing the service right away.
        await advertiser.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>
/// Announces the event server over mDNS (AD-3, AD-12).
///
/// The announcement is a convenience, not a requirement: the manual address is a permanent
/// and equivalent way to reach the server (AD-3). The types of the mDNS library stay inside
/// the infrastructure, so replacing the library only touches the publisher.
/// </summary>
public interface IMdnsServiceAdvertiser : IAsyncDisposable
{
    /// <summary>
    /// Whether the announcement is running. A failed announcement leaves it false and the
    /// server keeps serving (AD-3).
    /// </summary>
    bool IsAdvertising { get; }

    /// <summary>
    /// Starts the announcement. It never throws: a failure is logged and the server starts
    /// anyway, because the manual address is an equivalent way to reach it (AD-3).
    /// </summary>
    void Start();
}

/// <summary>
/// Announces <c>_lanpartyhub._tcp</c> over UDP 5353 (AD-3, AD-12).
///
/// The record carries nothing but the version of the TXT record: the fingerprint of the
/// trust anchor is never published over mDNS (AD-2, AD-27), because a value that travels
/// through the network can never become the anchor on its own.
/// </summary>
public sealed class MdnsServiceAdvertiser : IMdnsServiceAdvertiser
{
    /// <summary>Service type announced by the event server.</summary>
    public const string ServiceType = "_lanpartyhub._tcp";

    /// <summary>Records of the TXT entry of the announcement.</summary>
    public static IReadOnlyDictionary<string, string> TxtProperties { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["txtvers"] = "1",
        };

    private readonly ILogger<MdnsServiceAdvertiser> _logger;
    private readonly IMdnsPublisher _publisher;

    /// <summary>Creates the advertiser.</summary>
    /// <param name="logger">Logger of the host.</param>
    /// <param name="publisher">Publisher of the mDNS records.</param>
    public MdnsServiceAdvertiser(ILogger<MdnsServiceAdvertiser> logger, IMdnsPublisher publisher)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(publisher);

        _logger = logger;
        _publisher = publisher;
    }

    /// <inheritdoc />
    public bool IsAdvertising { get; private set; }

    /// <inheritdoc />
    public void Start()
    {
        try
        {
            _publisher.Publish();

            IsAdvertising = true;

            _logger.LogInformation(
                "Anuncio mDNS publicado: {ServiceType} en el puerto {Port} con el nombre de instancia {InstanceName}.",
                ServiceType,
                _publisher.Port,
                _publisher.InstanceName);
        }
        catch (Exception exception)
        {
            IsAdvertising = false;

            _logger.LogError(
                exception,
                "No se pudo publicar el anuncio mDNS {ServiceType}: el servidor sigue arrancando y la dirección manual sigue siendo operativa.",
                ServiceType);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        IsAdvertising = false;

        await _publisher.DisposeAsync().ConfigureAwait(false);
    }
}
