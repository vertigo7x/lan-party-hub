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

            // The version of the schema is the first thing that is checked: a mutation
            // with a version that the server does not understand is rejected before
            // anything is written (AD-5). The exceptions of the domain state the two
            // cases, so the rule lives in one place and the adapter translates it.
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
                var metadata = request.ToDescriptor();

                // The identifier is read the same way in both places: the one of the
                // envelope, and, when the envelope does not carry it, the one that the
                // descriptor carries. A body that names the videogame only inside the
                // descriptor is a modificación, and its identifier is never discarded
                // (AD-34).
                var isModification = request.IsModification();

                var gameId = !string.IsNullOrWhiteSpace(request.GameId)
                    ? request.GameId.Trim()
                    : metadata.GameId;

                var descriptor = isModification
                    ? await modifyGame
                        .ExecuteAsync(gameId, metadata, context.RequestAborted)
                        .ConfigureAwait(false)
                    : await registerGame
                        .ExecuteAsync(metadata, context.RequestAborted)
                        .ConfigureAwait(false);

                context.Response.StatusCode = isModification
                    ? StatusCodes.Status200OK
                    : StatusCodes.Status201Created;

                context.Response.ContentType = "application/json; charset=utf-8";

                await context.Response
                    .WriteAsync(Serialize(descriptor), context.RequestAborted)
                    .ConfigureAwait(false);
            }
            catch (GamePayloadMissingException exception)
            {
                // The body carries a supported schemaVersion but no metadata at all: the
                // code of this absence is its own, so the metric of errors per code does
                // not mix it with the absence of the schemaVersion (AD-15).
                await WriteErrorAsync(
                    context,
                    metrics,
                    logger,
                    StatusCodes.Status400BadRequest,
                    ErrorCatalog.Resolve(ErrorCatalog.GamePayloadMissingCode),
                    exception.Message)
                    .ConfigureAwait(false);
            }
            catch (GameMetadataMissingException exception)
            {
                // El código del catálogo nombra el campo que falta, porque la forma de
                // ApiError es fija y no tiene hueco para el campo (AD-15, AD-25).
                var code = MetadataMissingCodeFor(exception.FieldName);

                var entry = ErrorCatalog.Resolve(code);

                var error = new ApiError(
                    code,
                    exception.Message,
                    entry.Cause,
                    entry.NextStep);

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
                // El rango inválido tiene su propio código: la métrica de errores por
                // código distingue el rango del metadato ausente (AD-15, AD-25).
                var entry = ErrorCatalog.Resolve(ErrorCatalog.GamePlayerRangeInvalidCode);

                var error = new ApiError(
                    ErrorCatalog.GamePlayerRangeInvalidCode,
                    exception.Message,
                    entry.Cause,
                    entry.NextStep);

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
                // La excepción nunca alcanza el mensaje de la respuesta: el operador
                // la encuentra en el log, correlacionada por su operationId (AD-15).
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
    /// Comprueba que el cuerpo de la mutación lleva la versión del esquema que el servidor
    /// soporta (AD-5).
    ///
    /// <para>
    /// La regla vive en el dominio y el adaptador la traduce a la forma de error de la
    /// API: un cuerpo sin <c>schemaVersion</c> y un cuerpo con una versión que no se
    /// soportan son dos casos distintos y cada uno tiene su excepción y su código
    /// (AD-15, AD-25).
    /// </para>
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
    /// The stable code of the catalog que responde al rechazo de un metadato obligatorio
    /// ausente. El código nombra el campo que falta (AD-15, AD-25).
    /// </summary>
    /// <param name="fieldName">Nombre del campo que falta.</param>
    /// <returns>El código estable del catálogo de errores.</returns>
    public static string MetadataMissingCodeFor(string fieldName) => fieldName switch
    {
        GameRegistration.FieldNames.GameId => ErrorCatalog.GameIdMissingCode,
        GameRegistration.FieldNames.Name => ErrorCatalog.GameNameMissingCode,
        GameRegistration.FieldNames.Description => ErrorCatalog.GameDescriptionMissingCode,
        GameRegistration.FieldNames.Categories => ErrorCatalog.GameCategoriesMissingCode,
        GameRegistration.FieldNames.Tags => ErrorCatalog.GameTagsMissingCode,
        GameRegistration.FieldNames.CoverImage => ErrorCatalog.GameCoverImageMissingCode,
        GameRegistration.FieldNames.Images => ErrorCatalog.GameImagesMissingCode,
        GameRegistration.FieldNames.Version => ErrorCatalog.GameVersionMissingCode,
        GameRegistration.FieldNames.SizeBytes => ErrorCatalog.GameSizeBytesMissingCode,
        GameRegistration.FieldNames.MinPlayers => ErrorCatalog.GameMinPlayersMissingCode,
        GameRegistration.FieldNames.MaxPlayers => ErrorCatalog.GameMaxPlayersMissingCode,
        GameRegistration.FieldNames.OperatingSystem => ErrorCatalog.GameOperatingSystemMissingCode,
        GameRegistration.FieldNames.DistributionType => ErrorCatalog.GameDistributionTypeMissingCode,
        GameRegistration.FieldNames.InstallationType => ErrorCatalog.GameInstallationTypeMissingCode,
        GameRegistration.FieldNames.LaunchConfiguration => ErrorCatalog.GameLaunchConfigurationMissingCode,
        GameRegistration.FieldNames.Dependencies => ErrorCatalog.GameDependenciesMissingCode,
        GameRegistration.FieldNames.License => ErrorCatalog.GameLicenseMissingCode,
        GameRegistration.FieldNames.DistributionRestrictions => ErrorCatalog.GameDistributionRestrictionsMissingCode,
        _ => ErrorCatalog.GameMetadataMissingCode,
    };

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
