using LANPartyHub.Server.Application.Ports;
using LANPartyHub.Server.Host;
using LANPartyHub.Shared.Contracts;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Server.Infrastructure.Notifications;

/// <summary>
/// Adapter of the notification of a change of the catalog (AD-5, AD-26).
///
/// <para>
/// The adapter pushes the event <see cref="CatalogChanged"/> through the hub of the state
/// of the client. The event carries ONLY the version of the catalog: it is the trigger of
/// the resynchronization, and the client reads the snapshot complete by REST (AD-5,
/// AD-26).
/// </para>
/// <para>
/// The notification is sent AFTER the commit of the write that grew the version. A client
/// that receives the trigger and reads the snapshot finds the change that the version
/// announces, and never a version that announces a change that did not happen (AD-26).
/// </para>
/// <para>
/// A failure of the push never fails the publication: the catalog is already committed and
/// the client that did not receive the trigger recovers with its own synchronization
/// (AD-26).
/// </para>
/// </summary>
public sealed class CatalogChangeNotifier : ICatalogChangeNotifier
{
    private readonly IHubContext<CatalogHub> _hubContext;
    private readonly ILogger<CatalogChangeNotifier> _logger;

    /// <summary>Creates the adapter of the notification of a change of the catalog.</summary>
    /// <param name="hubContext">Context of the hub of the state of the client.</param>
    /// <param name="logger">Log of the adapter.</param>
    public CatalogChangeNotifier(
        IHubContext<CatalogHub> hubContext,
        ILogger<CatalogChangeNotifier> logger)
    {
        ArgumentNullException.ThrowIfNull(hubContext);
        ArgumentNullException.ThrowIfNull(logger);

        _hubContext = hubContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task NotifyAsync(long catalogVersion, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(catalogVersion);

        try
        {
            await _hubContext.Clients
                .All
                .SendAsync(
                    CatalogHub.CatalogChangedEventName,
                    new CatalogChanged(catalogVersion),
                    cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Se ha notificado el cambio del catálogo con la versión {CatalogVersion}.",
                catalogVersion);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // El push es un disparo de la resincronización y no una garantía: el cliente
            // que no lo recibe recupera con su propia sincronización, y la publicación ya
            // está confirmada (AD-26).
            _logger.LogWarning(
                exception,
                "No se pudo notificar el cambio del catálogo con la versión {CatalogVersion}.",
                catalogVersion);
        }
    }
}
