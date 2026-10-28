using Microsoft.Extensions.Hosting;

namespace LANPartyHub.Server.Infrastructure.Networking;

/// <summary>
/// Starts the mDNS advertisement when the host starts and stops it when the host stops
/// (AD-3, AD-12).
///
/// The advertisement is published when the application has started, that is, when the HTTP
/// surface is already listening: a client that discovers the service finds a server that
/// answers. If the host fails to start, the application never starts and nothing is
/// announced. The goodbye packets of RFC 6762 are sent on the stop, so the clients of the
/// network stop seeing the service right away.
/// </summary>
public sealed class MdnsAdvertisementService : IHostedService
{
    private readonly IMdnsServiceAdvertiser _advertiser;
    private readonly IHostApplicationLifetime _lifetime;

    /// <summary>Creates the service.</summary>
    /// <param name="advertiser">Advertiser of the service.</param>
    /// <param name="lifetime">Lifetime of the host, used to know when the application starts.</param>
    public MdnsAdvertisementService(IMdnsServiceAdvertiser advertiser, IHostApplicationLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(advertiser);
        ArgumentNullException.ThrowIfNull(lifetime);

        _advertiser = advertiser;
        _lifetime = lifetime;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _lifetime.ApplicationStarted.Register(_advertiser.Start);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _advertiser.DisposeAsync();
    }
}
