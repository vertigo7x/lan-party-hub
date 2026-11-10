using LANPartyHub.Server.Application.Clients;
using LANPartyHub.Shared.Contracts;
using Microsoft.AspNetCore.SignalR;

namespace LANPartyHub.Server.Host;

/// <summary>
/// Hub of the state of the client (AD-5, AD-26, AD-29).
///
/// <para>
/// The hub lives in the route <c>/hubs/client-state</c> and it is the ONLY channel of push
/// from the server to the client (AD-5). The only event that travels server to client
/// through it in the story of the catalog is <see cref="CatalogChanged"/>, the trigger of the
/// resynchronization: it carries the version of the catalog and nothing else (AD-5,
/// AD-26).
/// </para>
/// <para>
/// The hub does not authenticate and does not authorize anybody: the LAN is trusted by
/// design and the only gating of the client is the compatibility of the version (AD-6).
/// The event carries no data of other clients, so a subscriber never learns anything about
/// the rest of the event (AD-29).
/// </para>
/// <para>
/// AD-29: el hub NO publica ningún evento de servidor → cliente, y el
/// <c>ClientsSnapshotChanged</c> NO existe. Los métodos que este hub declara van en la
/// dirección CONTRARIA —cliente → servidor— y reciben lo que el propio cliente declara:
/// el estado del equipo (AD-18, AD-29), el progreso de sus operaciones (AD-22) y el
/// resultado terminal de sus operaciones (AD-33).
/// </para>
/// </summary>
public sealed class CatalogHub : Hub
{
    private readonly ReportClientState _reportClientState;
    private readonly ReportClientProgress _reportClientProgress;
    private readonly ReportOperationOutcome _reportOperationOutcome;
    private readonly ReportClientConnection _reportClientConnection;

    /// <summary>Route of the hub of the state of the client.</summary>
    public const string Route = "/hubs/client-state";

    /// <summary>
    /// Name of the event that triggers the resynchronization of the catalog (AD-5).
    /// </summary>
    public const string CatalogChangedEventName = nameof(CatalogChanged);

    /// <summary>
    /// Name of the method that recibe el estado que declara el cliente (AD-18, AD-29).
    /// </summary>
    public const string ClientStateChangedMethodName = nameof(ClientStateChanged);

    /// <summary>
    /// Name of the method that receives the progress of a download operation (AD-22).
    /// </summary>
    public const string DownloadProgressUpdatedMethodName = nameof(DownloadProgressUpdated);

    /// <summary>
    /// Name of the method that receives the progress of an installation operation
    /// (AD-22).
    /// </summary>
    public const string InstallationProgressUpdatedMethodName = nameof(InstallationProgressUpdated);

    /// <summary>
    /// Name of the method that receives the TERMINAL result of an operation (AD-33).
    /// </summary>
    public const string OperationOutcomeReportedMethodName = nameof(OperationOutcomeReported);

    /// <summary>
    /// Name of the method that receives the CONNECTION state ALONE (AD-18, AD-29).
    /// </summary>
    public const string ClientConnectionChangedMethodName = nameof(ClientConnectionChanged);

    /// <summary>Creates the hub of the state of the client.</summary>
    /// <param name="reportClientState">Use case that persists the state of the client.</param>
    /// <param name="reportClientProgress">Use case that projects the progress of the client.</param>
    /// <param name="reportOperationOutcome">Use case that records the terminal result of an operation.</param>
    /// <param name="reportClientConnection">
    /// Use case that persists ONLY the connection state that the client declares.
    /// </param>
    public CatalogHub(
        ReportClientState reportClientState,
        ReportClientProgress reportClientProgress,
        ReportOperationOutcome reportOperationOutcome,
        ReportClientConnection reportClientConnection)
    {
        ArgumentNullException.ThrowIfNull(reportClientState);
        ArgumentNullException.ThrowIfNull(reportClientProgress);
        ArgumentNullException.ThrowIfNull(reportOperationOutcome);
        ArgumentNullException.ThrowIfNull(reportClientConnection);

        _reportClientState = reportClientState;
        _reportClientProgress = reportClientProgress;
        _reportOperationOutcome = reportOperationOutcome;
        _reportClientConnection = reportClientConnection;
    }

    /// <summary>
    /// Receives the state that the client declares (AD-18, AD-22, AD-29).
    ///
    /// <para>
    /// La transición del <c>connectionState</c> de un <c>ClientRegistration</c> es un
    /// hecho que el servidor PERSISTE, para que la tabla de clientes refleje la
    /// conexión y la desconexión de cada equipo (AD-29, FR-10).
    /// </para>
    /// <para>
    /// El método NO lleva ningún dato de OTRO cliente: el <c>clientId</c> del payload
    /// es el del propio cliente que publica, y el servidor no acepta una publicación
    /// que nombre a otro (AD-29).
    /// </para>
    /// <para>
    /// El nombre del método es EXACTAMENTE el que invoca el cliente
    /// (<see cref="ClientStateChangedMethodName"/>): SignalR registra el método con su
    /// nombre de .NET y NO recorta el sufijo <c>Async</c>, así que un nombre distinto
    /// hace que el servidor responda «Method does not exist» y la publicación se pierde.
    /// </para>
    /// </summary>
    /// <param name="state">State that the client declares.</param>
    /// <returns>A task that completes when the state has been persisted.</returns>
    public async Task ClientStateChanged(ClientStateChanged state)
    {
        ArgumentNullException.ThrowIfNull(state);

        await _reportClientState
            .ExecuteAsync(state, Context.ConnectionAborted)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Receives the progress of a download operation that the client declares (AD-22,
    /// FR-10).
    ///
    /// <para>
    /// El progreso lleva los BYTES REALES que el cliente transfirió: el servidor no
    /// estima ni inventa un porcentaje que el cliente no declaró (AD-22).
    /// </para>
    /// </summary>
    /// <param name="progress">Progress that the client declares.</param>
    /// <returns>A task that completes when the progress has been projected.</returns>
    public async Task DownloadProgressUpdated(DownloadProgressUpdated progress)
    {
        ArgumentNullException.ThrowIfNull(progress);

        await _reportClientProgress
            .ExecuteAsync(progress, Context.ConnectionAborted)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Receives the REAL phase of an installation operation that the client declares
    /// (AD-22, FR-10).
    ///
    /// <para>
    /// La fase es la REAL y el progreso NO lleva porcentaje: un porcentaje sobre un
    /// total que no se conoce sería una mentira (AD-22).
    /// </para>
    /// </summary>
    /// <param name="progress">Progress that the client declares.</param>
    /// <returns>A task that completes when the phase has been projected.</returns>
    public async Task InstallationProgressUpdated(InstallationProgressUpdated progress)
    {
        ArgumentNullException.ThrowIfNull(progress);

        await _reportClientProgress
            .ExecuteAsync(progress, Context.ConnectionAborted)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Receives the TERMINAL result of an operation that the client declares (AD-33).
    ///
    /// <para>
    /// El resultado es un RESULTADO y NUNCA un error: un resultado
    /// <see cref="OperationOutcome.PackageWithdrawn"/> no produce <see cref="ApiError"/> ni
    /// se persiste como <c>ClientError</c> (AD-33, R-MVP-8).
    /// </para>
    /// </summary>
    /// <param name="reported">Result that the client declares.</param>
    /// <returns>A task that completes when the result has been recorded.</returns>
    public async Task OperationOutcomeReported(OperationOutcomeReported reported)
    {
        ArgumentNullException.ThrowIfNull(reported);

        await _reportOperationOutcome
            .ExecuteAsync(reported, Context.ConnectionAborted)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Receives the CONNECTION state that the client declares ALONE (AD-18, AD-29,
    /// AD-32).
    ///
    /// <para>
    /// El método existe para el cliente que NO puede sostener una INSTANTÁNEA COMPLETA
    /// de su estado: en ese caso declara SOLO la conexión, y el servidor SOLO actualiza
    /// el <c>connectionState</c> de la fila, sin tocar las instalaciones ni las
    /// operaciones que ya proyectó de ese cliente (AD-22, AD-29).
    /// </para>
    /// <para>
    /// El método NO lleva ningún dato de OTRO cliente: el <c>clientId</c> del payload es
    /// el del propio cliente que publica, y el servidor no acepta una publicación que
    /// nombre a otro (AD-29).
    /// </para>
    /// </summary>
    /// <param name="changed">Connection state that the client declares.</param>
    /// <returns>A task that completes when the connection has been persisted.</returns>
    public async Task ClientConnectionChanged(ClientConnectionChanged changed)
    {
        ArgumentNullException.ThrowIfNull(changed);

        await _reportClientConnection
            .ExecuteAsync(changed, Context.ConnectionAborted)
            .ConfigureAwait(false);
    }
}
