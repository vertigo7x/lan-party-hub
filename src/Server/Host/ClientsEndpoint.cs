using System.Text.Json;
using LANPartyHub.Server.Application.Clients;
using LANPartyHub.Server.Application.Ports;
using LANPartyHub.Server.Domain.Clients;
using LANPartyHub.Server.Infrastructure.Http;
using LANPartyHub.Shared.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Server.Host;

/// <summary>
/// Surface of the clients of the event (AD-29, AD-30, AD-32).
///
/// <para>
/// The surface carries TWO routes and ONE operation. The <c>POST</c> is the ONLY
/// point of entry of the client to the server: the alta, the reconnection and the
/// rename of the name of the team happen in it, and the server does NOT expose any
/// other endpoint of registration, of rename or of administration of names (AD-32).
/// </para>
/// <para>
/// The <c>GET</c> publishes the view of organization of the clients, and it is the
/// ONLY way of getting it: the event <c>ClientsSnapshotChanged</c> does not exist
/// (AD-29).
/// </para>
/// <para>
/// The check of the compatibility of the versions happens BEFORE the validation of the
/// name, because it is the middleware of the version that runs before this endpoint:
/// a client that is not compatible never reaches the layer of the registration
/// (AD-6, AD-32).
/// </para>
/// <para>
/// Every rejection is the single error shape of the system, <see cref="ApiError"/>,
/// with the stable code of the catalog of errors, and it is counted in the metric of
/// errors per code (AD-15, AD-25). The collision is NOT an error: the registration
/// succeeds with the effective name (AD-30).
/// </para>
/// </summary>
public static class ClientsEndpoint
{
    /// <summary>Route of the surface of the clients of the event.</summary>
    public const string Route = "/api/v1/clients";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Serializes a payload with the exact shape that the endpoint publishes.
    /// </summary>
    /// <param name="payload">Payload of the endpoint.</param>
    /// <returns>The JSON payload of the endpoint.</returns>
    public static string Serialize<T>(T payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        return JsonSerializer.Serialize(payload, SerializerOptions);
    }

    /// <summary>
    /// Registers the surface of the clients of the event in the application.
    /// </summary>
    /// <param name="app">Application of the server.</param>
    public static void Map(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // AD-29: la vista de organización de clientes se obtiene por REST. La lista no
        // viaja por el hub, porque el evento ClientsSnapshotChanged no existe.
        app.MapGet(Route, async (
            HttpContext context,
            ListClients listClients,
            IMetricsCounters metrics,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("LANPartyHub.Server.Clients");

            try
            {
                var clients = await listClients
                    .ExecuteAsync(context.RequestAborted)
                    .ConfigureAwait(false);

                context.Response.StatusCode = StatusCodes.Status200OK;
                context.Response.ContentType = "application/json; charset=utf-8";

                await context.Response
                    .WriteAsync(Serialize(clients), context.RequestAborted)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // La excepción nunca alcanza el mensaje de la respuesta: el operador la
                // encuentra en el log, correlacionada por su operationId (AD-15).
                logger.LogError(
                    exception,
                    "No se pudo leer la vista de clientes: la persistencia ha fallado.");

                await WriteErrorAsync(
                    context,
                    metrics,
                    logger,
                    StatusCodes.Status500InternalServerError,
                    ErrorCatalog.Resolve(ErrorCatalog.UnknownErrorCode),
                    null)
                    .ConfigureAwait(false);
            }
        });

        // AD-32: el registro es la ÚNICA operación de entrada del cliente al servidor.
        app.MapPost(Route, async (
            HttpContext context,
            RegisterClient registerClient,
            IMetricsCounters metrics,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("LANPartyHub.Server.Clients");

            ClientRegistrationRequest? request;

            try
            {
                request = await context.Request
                    .ReadFromJsonAsync<ClientRegistrationRequest>(SerializerOptions, context.RequestAborted)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is JsonException or NotSupportedException)
            {
                // El cuerpo no se puede leer en la forma que el contrato declara (AD-32).
                logger.LogWarning(exception, "Se rechaza el registro del cliente: el cuerpo no se puede leer.");

                await WriteErrorAsync(
                    context,
                    metrics,
                    logger,
                    StatusCodes.Status400BadRequest,
                    ErrorCatalog.Resolve(ErrorCatalog.MachineNameInvalidCode),
                    null)
                    .ConfigureAwait(false);

                return;
            }

            if (request is null)
            {
                await WriteErrorAsync(
                    context,
                    metrics,
                    logger,
                    StatusCodes.Status400BadRequest,
                    ErrorCatalog.Resolve(ErrorCatalog.MachineNameInvalidCode),
                    null)
                    .ConfigureAwait(false);

                return;
            }

            // AD-23: el identificador del cliente lo validó el middleware de la
            // correlación, así que aquí es siempre un identificador canónico.
            var clientId = context.Request.Headers[CorrelationMiddleware.ClientIdHeaderName];

            if (clientId.Count != 1 || string.IsNullOrWhiteSpace(clientId[0]))
            {
                await WriteErrorAsync(
                    context,
                    metrics,
                    logger,
                    StatusCodes.Status400BadRequest,
                    ErrorCatalog.Resolve(ErrorCatalog.ClientIdMissingCode),
                    null)
                    .ConfigureAwait(false);

                return;
            }

            try
            {
                // AD-29: el estado de conexión NO viaja en el cuerpo, cuya forma es
                // EXACTA (AD-32). El cliente se registra CONECTADO y publica su
                // estado —también el de desconexión— en el evento ClientStateChanged
                // del hub, que es el canal sancionado para el estado del cliente
                // (AD-18, AD-29).
                var outcome = await registerClient
                    .ExecuteAsync(
                        clientId[0]!,
                        request.MachineName,
                        request.ClientVersion ?? string.Empty,
                        request.ProtocolVersion,
                        ConnectionState.AnchorVerified,
                        context.RequestAborted)
                    .ConfigureAwait(false);

                // AD-32: el éxito responde 200 OK y NO 201 Created, porque la fila de
                // ClientRegistration es actualizable: la reconexión y el renombrado
                // pasan por la misma ruta.
                context.Response.StatusCode = StatusCodes.Status200OK;
                context.Response.ContentType = "application/json; charset=utf-8";

                await context.Response
                    .WriteAsync(
                        Serialize(new ClientRegistrationResponse(
                            outcome.ClientId,
                            outcome.MachineName,
                            outcome.ConnectionState,
                            outcome.LastSeenAt,
                            outcome.Renamed)),
                        context.RequestAborted)
                    .ConfigureAwait(false);
            }
            catch (MachineNameInvalidException exception)
            {
                // AD-30, AD-32: el nombre mal formado se rechaza con 422 y con la forma
                // única ApiError. El cliente queda sin registrar y no puede operar.
                await WriteErrorAsync(
                    context,
                    metrics,
                    logger,
                    StatusCodes.Status422UnprocessableEntity,
                    ErrorCatalog.Resolve(ErrorCatalog.MachineNameInvalidCode),
                    exception.Reason)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // La excepción nunca alcanza el mensaje de la respuesta: el operador la
                // encuentra en el log, correlacionada por su operationId (AD-15).
                logger.LogError(
                    exception,
                    "No se pudo registrar el cliente: la persistencia ha fallado.");

                await WriteErrorAsync(
                    context,
                    metrics,
                    logger,
                    StatusCodes.Status500InternalServerError,
                    ErrorCatalog.Resolve(ErrorCatalog.UnknownErrorCode),
                    null)
                    .ConfigureAwait(false);
            }
        });
    }

    /// <summary>
    /// Writes the single error shape of the system in the response and counts the error
    /// under its stable code (AD-15, AD-25).
    /// </summary>
    /// <param name="context">Context of the request.</param>
    /// <param name="metrics">Counters of the host.</param>
    /// <param name="logger">Log of the surface of the clients.</param>
    /// <param name="statusCode">Status of the response.</param>
    /// <param name="error">Error to write.</param>
    /// <param name="detail">Concrete rule that fails, when the rejection names one.</param>
    /// <returns>A task that completes when the error has been written.</returns>
    private static async Task WriteErrorAsync(
        HttpContext context,
        IMetricsCounters metrics,
        ILogger logger,
        int statusCode,
        ApiError error,
        string? detail)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(error);

        metrics.RecordError(error.Code);

        if (detail is null)
        {
            logger.LogWarning(
                "Se rechaza la petición de la superficie de clientes con el código {Code} y el estado {StatusCode}.",
                error.Code,
                statusCode);
        }
        else
        {
            logger.LogWarning(
                "Se rechaza la petición de la superficie de clientes con el código {Code}, el estado {StatusCode} y el motivo {Detail}.",
                error.Code,
                statusCode,
                detail);
        }

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";

        await context.Response
            .WriteAsync(Serialize(error), context.RequestAborted)
            .ConfigureAwait(false);
    }
}
