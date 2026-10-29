using System.Text;
using System.Text.Json;
using LANPartyHub.Server.Application.Packages;
using LANPartyHub.Server.Application.Ports;
using LANPartyHub.Server.Domain.Catalog;
using LANPartyHub.Server.Domain.Packages;
using LANPartyHub.Shared.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Server.Host;

/// <summary>
/// Administration surface of the publication of the packages (AD-5, AD-7, AD-8, AD-15).
///
/// <para>
/// The endpoint publishes the package of a videogame of the catalog. The body carries the
/// <c>schemaVersion</c> that every mutation of the API carries (AD-5), and the data of the
/// package and of its installation manifest travel in the contracts of <c>Shared</c> that
/// the body brings (AD-7).
/// </para>
/// <para>
/// The surface of the administration is not the surface of the client: the organizer is not
/// a client of the LAN and does not carry the identifier of a client, so the request is not
/// rejected for that. The operation is a unit of work all the same, and it is correlated by
/// its <c>operationId</c> (AD-15, AD-30).
/// </para>
/// <para>
/// Every rejection is the single error shape of the system, <see cref="ApiError"/>, with
/// the stable code of the catalog of errors, and it is counted in the metric of errors per
/// code (AD-15, AD-25).
/// </para>
/// </summary>
public static class PackagesEndpoint
{
    /// <summary>Route of the administration of the publication of the packages.</summary>
    public const string Route = "/api/v1/admin/packages";

    /// <summary>
    /// Version of the schema of the body of the administration that this server supports
    /// (AD-5).
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
    /// Registers the administration of the publication of the packages in the application.
    /// </summary>
    /// <param name="app">Application of the server.</param>
    public static void Map(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost(Route, async (
            HttpContext context,
            PublishPackage publishPackage,
            IMetricsCounters metrics,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("LANPartyHub.Server.Packages");

            PackageRequest? request;

            // The body is read here and not by the binder of the framework, because a
            // manifest that declares an operation outside the closed set cannot be read
            // into the contract of Shared. The rejection of that case has to be the
            // single error shape of the system, and a body that the framework rejects on
            // its own would answer with a shape that is not the one of the system
            // (AD-7, AD-8, AD-15).
            try
            {
                request = await JsonSerializer
                    .DeserializeAsync<PackageRequest>(context.Request.Body, SerializerOptions, context.RequestAborted)
                    .ConfigureAwait(false);
            }
            catch (JsonException exception)
            {
                logger.LogWarning(
                    exception,
                    "Se rechaza la publicación del paquete: el manifiesto declara una operación fuera del conjunto cerrado.");

                await WriteErrorAsync(
                    context,
                    metrics,
                    logger,
                    StatusCodes.Status422UnprocessableEntity,
                    ErrorCatalog.Resolve(ErrorCatalog.ManifestOperationNotAllowedCode),
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

            // The version of the schema is the first thing that is checked: a mutation
            // with a version that the server does not understand is rejected before
            // anything is written (AD-5).
            try
            {
                EnsureSupportedSchemaVersion(request.SchemaVersion);
            }
            catch (SchemaVersionMissingException)
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
            catch (SchemaVersionNotSupportedException)
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

            try
            {
                var data = request.ToPublicationData();

                var descriptor = await publishPackage
                    .ExecuteAsync(
                        data.GameId,
                        data.PackageId,
                        data.Version,
                        data.SizeBytes,
                        data.ChunkSizeBytes,
                        data.Launch,
                        data.Operations,
                        context.RequestAborted)
                    .ConfigureAwait(false);

                context.Response.StatusCode = StatusCodes.Status201Created;
                context.Response.ContentType = "application/json; charset=utf-8";

                await context.Response
                    .WriteAsync(Serialize(descriptor), context.RequestAborted)
                    .ConfigureAwait(false);
            }
            catch (PackagePayloadMissingException exception)
            {
                // El cuerpo lleva la versión del esquema pero no los datos de la
                // publicación: el código de esta ausencia es el suyo, de modo que la
                // métrica de errores por código no lo mezcla con la ausencia de la
                // versión del esquema (AD-15).
                await WriteErrorAsync(
                    context,
                    metrics,
                    logger,
                    StatusCodes.Status400BadRequest,
                    ErrorCatalog.Resolve(ErrorCatalog.PackagePayloadMissingCode),
                    exception.Message)
                    .ConfigureAwait(false);
            }
            catch (PackageLicenseMissingException exception)
            {
                // El bloqueo de la publicación nombra el campo concreto que falta, porque
                // la forma de ApiError es fija y no tiene hueco para el campo (AD-25).
                await WriteErrorAsync(
                    context,
                    metrics,
                    logger,
                    StatusCodes.Status422UnprocessableEntity,
                    ErrorCatalog.Resolve(ErrorCatalog.PackageLicenseMissingCode),
                    exception.FieldName)
                    .ConfigureAwait(false);
            }
            catch (PackageDistributionRestrictionsMissingException exception)
            {
                await WriteErrorAsync(
                    context,
                    metrics,
                    logger,
                    StatusCodes.Status422UnprocessableEntity,
                    ErrorCatalog.Resolve(ErrorCatalog.PackageDistributionRestrictionsMissingCode),
                    exception.FieldName)
                    .ConfigureAwait(false);
            }
            catch (PackageNotFoundException exception)
            {
                await WriteErrorAsync(
                    context,
                    metrics,
                    logger,
                    StatusCodes.Status404NotFound,
                    ErrorCatalog.Resolve(ErrorCatalog.PackageGameNotFoundCode),
                    exception.GameId)
                    .ConfigureAwait(false);
            }
            catch (PackageAlreadyPublishedException exception)
            {
                // Un paquete publicado es inmutable: republicarlo es un conflicto con la
                // fila que ya existe, y la fila no se modifica (AD-7).
                await WriteErrorAsync(
                    context,
                    metrics,
                    logger,
                    StatusCodes.Status409Conflict,
                    ErrorCatalog.Resolve(ErrorCatalog.PackageAlreadyPublishedCode),
                    exception.PackageId)
                    .ConfigureAwait(false);
            }
            catch (PackageContentMismatchException exception)
            {
                await WriteErrorAsync(
                    context,
                    metrics,
                    logger,
                    StatusCodes.Status422UnprocessableEntity,
                    ErrorCatalog.Resolve(ErrorCatalog.PackageContentMismatchCode),
                    exception.Message)
                    .ConfigureAwait(false);
            }
            catch (ManifestOperationNotAllowedException exception)
            {
                // La operación no pertenece al conjunto cerrado de AD-7: se rechaza antes
                // de escribir nada (AD-7, AD-8).
                await WriteErrorAsync(
                    context,
                    metrics,
                    logger,
                    StatusCodes.Status422UnprocessableEntity,
                    ErrorCatalog.Resolve(ErrorCatalog.ManifestOperationNotAllowedCode),
                    exception.OperationName)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // La excepción nunca alcanza el mensaje de la respuesta: el operador la
                // encuentra en el log, correlacionada por su operationId (AD-15).
                logger.LogError(
                    exception,
                    "No se pudo publicar el paquete: la persistencia ha fallado.");

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
    /// Comprueba que el cuerpo de la mutación lleva la versión del esquema que el servidor
    /// soporta (AD-5).
    /// </summary>
    /// <param name="schemaVersion">Versión del esquema que lleva el cuerpo.</param>
    /// <exception cref="SchemaVersionMissingException">El cuerpo no lleva la versión del esquema.</exception>
    /// <exception cref="SchemaVersionNotSupportedException">
    /// El cuerpo lleva una versión del esquema que el servidor no soporta.
    /// </exception>
    private static void EnsureSupportedSchemaVersion(int? schemaVersion)
    {
        if (schemaVersion is null)
        {
            throw new SchemaVersionMissingException();
        }

        if (schemaVersion != SupportedSchemaVersion)
        {
            throw new SchemaVersionNotSupportedException(schemaVersion.Value);
        }
    }

    /// <summary>
    /// Writes the single error shape of the system in the response and counts the error
    /// under its stable code (AD-15, AD-25).
    /// </summary>
    /// <param name="context">Context of the request.</param>
    /// <param name="metrics">Counters of the host.</param>
    /// <param name="logger">Log of the administration of the packages.</param>
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

        if (field is null)
        {
            logger.LogWarning(
                "Se rechaza la publicación del paquete con el código {Code} y el estado {StatusCode}.",
                error.Code,
                statusCode);
        }
        else
        {
            logger.LogWarning(
                "Se rechaza la publicación del paquete con el código {Code}, el estado {StatusCode} y el campo {Field}.",
                error.Code,
                statusCode,
                field);
        }

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";

        await context.Response
            .WriteAsync(Serialize(error), context.RequestAborted)
            .ConfigureAwait(false);
    }
}
