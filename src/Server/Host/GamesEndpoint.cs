using System.Text;
using System.Text.Json;
using LANPartyHub.Server.Application.Catalog;
using LANPartyHub.Server.Application.Ports;
using LANPartyHub.Server.Domain.Catalog;
using LANPartyHub.Shared.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Server.Host;

/// <summary>
/// Administration surface of the videogames of the catalog (AD-5, AD-15, AD-34).
///
/// <para>
/// The endpoint registers a videogame and modifies a videogame that is already in the
/// catalog. The body carries the <c>schemaVersion</c> that every mutation of the API
/// carries, and the identifier of the videogame tells the two apart: a body that does not
/// carry it registers a videogame and the server generates the identifier; a body that
/// carries it modifies that videogame and the identifier never changes (AD-34).
/// </para>
/// <para>
/// The surface of the administration is not the surface of the client: the organizer is
/// not a client of the LAN and does not carry the identifier of a client, so the request
/// is not rejected for that. The operation is a unit of work all the same, and it is
/// correlated by its <c>operationId</c> (AD-15).
/// </para>
/// <para>
/// Every rejection is the single error shape of the system, <see cref="ApiError"/>, with
/// the stable code of the catalog of errors, and it is counted in the metric of errors per
/// code (AD-15, AD-25).
/// </para>
/// </summary>
public static class GamesEndpoint
{
    /// <summary>Route of the administration of the videogames of the catalog.</summary>
    public const string Route = "/api/v1/admin/games";

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
    /// Registers the administration of the videogames in the application.
    /// </summary>
    /// <param name="app">Application of the server.</param>
    public static void Map(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost(Route, async (
            GameRequest request,
            RegisterGame registerGame,
            ModifyGame modifyGame,
            IMetricsCounters metrics,
            ILoggerFactory loggerFactory,
            HttpContext context) =>
        {
            var logger = loggerFactory.CreateLogger("LANPartyHub.Server.Catalog");

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

            try
            {
                var descriptor = string.IsNullOrWhiteSpace(request.GameId)
                    ? await registerGame
                        .ExecuteAsync(request.ToMetadata(), context.RequestAborted)
                        .ConfigureAwait(false)
                    : await modifyGame
                        .ExecuteAsync(request.GameId, request.ToMetadata(), context.RequestAborted)
                        .ConfigureAwait(false);

                var status = string.IsNullOrWhiteSpace(request.GameId)
                    ? StatusCodes.Status201Created
                    : StatusCodes.Status200OK;

                context.Response.StatusCode = status;
                context.Response.ContentType = "application/json; charset=utf-8";

                await context.Response
                    .WriteAsync(Serialize(descriptor), context.RequestAborted)
                    .ConfigureAwait(false);
            }
            catch (GameMetadataMissingException exception)
            {
                // The message names the concrete field that is missing, so the organizer
                // knows which one to fill in (AD-25).
                var error = new ApiError(
                    ErrorCatalog.GameMetadataMissingCode,
                    exception.Message,
                    ErrorCatalog.Resolve(ErrorCatalog.GameMetadataMissingCode).Cause,
                    ErrorCatalog.Resolve(ErrorCatalog.GameMetadataMissingCode).NextStep);

                await WriteErrorAsync(
                    context,
                    metrics,
                    logger,
                    StatusCodes.Status422UnprocessableEntity,
                    error,
                    exception.FieldName)
                    .ConfigureAwait(false);
            }
            catch (GamePlayerRangeException exception)
            {
                var error = new ApiError(
                    ErrorCatalog.GameMetadataMissingCode,
                    exception.Message,
                    ErrorCatalog.Resolve(ErrorCatalog.GameMetadataMissingCode).Cause,
                    ErrorCatalog.Resolve(ErrorCatalog.GameMetadataMissingCode).NextStep);

                await WriteErrorAsync(
                    context,
                    metrics,
                    logger,
                    StatusCodes.Status422UnprocessableEntity,
                    error,
                    null)
                    .ConfigureAwait(false);
            }
            catch (GameNotFoundException exception)
            {
                await WriteErrorAsync(
                    context,
                    metrics,
                    logger,
                    StatusCodes.Status404NotFound,
                    ErrorCatalog.Resolve(ErrorCatalog.GameNotFoundCode),
                    exception.GameId)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The exception never reaches the message of the response: the operator
                // finds it in the log, correlated by its operationId (AD-15).
                logger.LogError(
                    exception,
                    "No se pudo guardar el videojuego del catálogo: la persistencia ha fallado.");

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
    /// <param name="logger">Log of the administration of the catalog.</param>
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
                "Se rechaza la petición de administración del catálogo con el código {Code} y el estado {StatusCode}.",
                error.Code,
                statusCode);
        }
        else
        {
            logger.LogWarning(
                "Se rechaza la petición de administración del catálogo con el código {Code}, el estado {StatusCode} y el campo {Field}.",
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
