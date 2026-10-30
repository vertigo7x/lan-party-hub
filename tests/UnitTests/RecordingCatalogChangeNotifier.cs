using LANPartyHub.Server.Application.Ports;

namespace LANPartyHub.UnitTests;

/// <summary>
/// Notifier of a change of the catalog that RECORDS what the use cases announce, so that
/// the tests can observe that the trigger is emitted AFTER the commit of the write that
/// grew the version of the catalog (AD-5, AD-26).
/// </summary>
public sealed class RecordingCatalogChangeNotifier : ICatalogChangeNotifier
{
    /// <summary>The versions that the use cases announced, in order.</summary>
    public List<long> NotifiedVersions { get; } = [];

    /// <inheritdoc />
    public Task NotifyAsync(long catalogVersion, CancellationToken cancellationToken)
    {
        NotifiedVersions.Add(catalogVersion);

        return Task.CompletedTask;
    }
}
