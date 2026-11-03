using System.Text.Json;
using LANPartyHub.Server.Application.Packages;
using LANPartyHub.Server.Application.Ports;
using LANPartyHub.Server.Infrastructure.Http;
using LANPartyHub.Shared.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Server.Host;

/// <summary>
/// Surface of the client that registers the intention of a download (AD-5, AD-10, AC1).
///
/// <para>
/// El cliente registra su intención con <c>POST /api/v1/downloads</c> y obtiene el
/// identificador de la operación, que es su unidad de trabajo (AD-15, AD-24). La ruta
/// pertenece a la superficie del cliente, así que exige el identificador del cliente y
/// está correlacionada por el middleware de la correlación (AD-15, AD-23).
/// </para>
/// <para>
/// La respuesta es un OBJETO DEL SERVIDOR con su <c>schemaVersion</c> (AD-5): el cuerpo
/// de la mutación declara la versión del esquema que el servidor soporta, y el cliente
/// la lee para saber con qué forma habla. El cuerpo no es un contrato de <c>Shared</c>
/// porque <c>Shared</c> declara exactamente veintidós contratos y ninguno es el de la
/// intención de una descarga (AD-18).
/// </para>
/// <para>
/// AD-10, AD-22: lo que el servidor persiste es el REFLEJO de la intención, NO el estado
/// de la operación. El servidor no ejecuta la descarga y no puede saber cómo va: el
/// cliente es el dueño del estado de la máquina.
/// </para>
/// </summary>
public static class DownloadsEndpoint
{
    /// <summary>Route that registers the intention of a download.</summary>
    public const string Route = "/api/v1/downloads";

    /// <summary>
    /// Version of the schema of the body of the surface of the client that this server
    /// supports (AD-5).
    /// </summary>
    public const int SupportedSchemaVersion = 1;

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
    /// Registers the intention of a download in the application.
    /// </summary>
    /// <param name="app">Application of the server.</param>
    public static void Map(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost(Route, async (
            HttpContext context,
            RegisterDownloadIntent registerDownloadIntent,
            IMetricsCounters metrics,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("LANPartyHub.Server.Downloads");

            DownloadIntentRequest? request;

            try
            {
                request = await context.Request
                    .ReadFromJsonAsync<DownloadIntentRequest>(SerializerOptions, context.RequestAborted)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is JsonException or NotSupportedException)
            {
                // El cuerpo no se puede leer en la forma que el contrato declara: es un
                // 400 con su propio código, y no el 500 de la persistencia (AD-5, AD-15).
                logger.LogWarning(exception, "Se rechaza la intención de descarga: el cuerpo no se puede leer.");

                await WriteErrorAsync(
                    context,
                    metrics,
                    logger,
                    StatusCodes.Status400BadRequest,
                    ErrorCatalog.Resolve(ErrorCatalog.DownloadBodyMalformedCode),
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
                    ErrorCatalog.Resolve(ErrorCatalog.SchemaVersionMissingCode),
                    null)
                    .ConfigureAwait(false);

                return;
            }

            // AD-5: la versión del esquema es lo primero que se comprueba, y una mutación
            // con una versión que el servidor no entiende se rechaza antes de escribir
            // nada.
            if (request.SchemaVersion is null)
            {
                await WriteErrorAsync(
                    context,
                    metrics,
                    logger,
                    StatusCodes.Status400BadRequest,
                    ErrorCatalog.Resolve(ErrorCatalog.SchemaVersionMissingCode),
                    null)
                    .ConfigureAwait(false);

                return;
            }

            if (request.SchemaVersion != SupportedSchemaVersion)
            {
                await WriteErrorAsync(
                    context,
                    metrics,
                    logger,
                    StatusCodes.Status400BadRequest,
                    ErrorCatalog.Resolve(ErrorCatalog.SchemaVersionNotSupportedCode),
                    null)
                    .ConfigureAwait(false);

                return;
            }

            if (string.IsNullOrWhiteSpace(request.PackageId))
            {
                await WriteErrorAsync(
                    context,
                    metrics,
                    logger,
                    StatusCodes.Status400BadRequest,
                    ErrorCatalog.Resolve(ErrorCatalog.DownloadPackageMissingCode),
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
                var operationId = await registerDownloadIntent
                    .ExecuteAsync(clientId[0]!, request.PackageId, context.RequestAborted)
                    .ConfigureAwait(false);

                context.Response.StatusCode = StatusCodes.Status201Created;
                context.Response.ContentType = "application/json; charset=utf-8";

                await context.Response
                    .WriteAsync(
                        Serialize(new DownloadIntentResponse(SupportedSchemaVersion, operationId, request.PackageId)),
                        context.RequestAborted)
                    .ConfigureAwait(false);
            }
            catch (DownloadPackageNotFoundException exception)
            {
                await WriteErrorAsync(
                    context,
                    metrics,
                    logger,
                    StatusCodes.Status404NotFound,
                    ErrorCatalog.Resolve(ErrorCatalog.DownloadPackageNotFoundCode),
                    exception.PackageId)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // La excepción nunca alcanza el mensaje de la respuesta: el operador la
                // encuentra en el log, correlacionada por su operationId (AD-15).
                logger.LogError(
                    exception,
                    "No se pudo registrar la intención de descarga: la persistencia ha fallado.");

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
    /// <param name="logger">Log of the surface of las descargas.</param>
    /// <param name="statusCode">Status of the response.</param>
    /// <param name="error">Error to write.</param>
    /// <param name="field">Concrete field that fails, when the rejection names one.</param>
    /// <returns>A task that completes when the error has been written.</returns>
    private static async Task WriteErrorAsync(
        HttpContext context,
        IMetricsCounters metrics,
        ILogger logger,
        int statusCode,
        ApiError error,
        string? field)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(error);

        metrics.RecordError(error.Code);

        logger.LogWarning(
            "Se rechaza la intención de descarga con el código {Code} y el estado {StatusCode}.",
            error.Code,
            statusCode);

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";

        await context.Response
            .WriteAsync(Serialize(error), context.RequestAborted)
            .ConfigureAwait(false);
    }
}
