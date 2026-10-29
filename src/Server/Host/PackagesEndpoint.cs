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
    ///
    /// <para>
    /// Esta es la versión del esquema del SOBRE de la petición. La versión del esquema
    /// del MANIFIESTO es otra cosa, y es la que declara
    /// <see cref="SupportedManifestSchemaVersion"/>.
    /// </para>
    /// </summary>
    public const int SupportedSchemaVersion = 1;

    /// <summary>
    /// Version of the schema of the installation manifest that this server supports
    /// (AD-7, AD-8).
    ///
    /// <para>
    /// It is the version that the use case publishes, and the source of truth of the
    /// version of the schema of the manifest is
    /// <see cref="InstallationManifestSchema.Version"/>, so the two cannot drift apart.
    /// </para>
    /// </summary>
    public const int SupportedManifestSchemaVersion = InstallationManifestSchema.Version;

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
            //
            // The body is read into memory and not streamed, because the discriminator
            // of the operations has to be inspected before the contract is deserialized:
            // a body that declares an operation outside the closed set and a body that
            // is not well formed are two different rejections, with two different
            // stable codes, and the metric of errors per code does not mix them
            // (AD-15, AD-25).
            byte[] payload;

            try
            {
                using var buffer = new MemoryStream();

                await context.Request.Body.CopyToAsync(buffer, context.RequestAborted).ConfigureAwait(false);

                payload = buffer.ToArray();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "No se pudo leer el cuerpo de la publicación del paquete.");

                await WriteErrorAsync(
                    context,
                    metrics,
                    logger,
                    StatusCodes.Status400BadRequest,
                    ErrorCatalog.Resolve(ErrorCatalog.PackageBodyMalformedCode),
                    null)
                    .ConfigureAwait(false);

                return;
            }

            string? unknownOperation;

            try
            {
                unknownOperation = FindUnknownOperation(payload);
            }
            catch (JsonException exception)
            {
                logger.LogWarning(exception, "Se rechaza la publicación del paquete: el cuerpo no se puede leer.");

                await WriteErrorAsync(
                    context,
                    metrics,
                    logger,
                    StatusCodes.Status400BadRequest,
                    ErrorCatalog.Resolve(ErrorCatalog.PackageBodyMalformedCode),
                    null)
                    .ConfigureAwait(false);

                return;
            }

            if (unknownOperation is not null)
            {
                // La operación no pertenece al conjunto cerrado de AD-7: se rechaza antes
                // de escribir nada (AD-7, AD-8).
                logger.LogWarning(
                    "Se rechaza la publicación del paquete: el manifiesto declara la operación '{Operation}' fuera del conjunto cerrado.",
                    unknownOperation);

                await WriteErrorAsync(
                    context,
                    metrics,
                    logger,
                    StatusCodes.Status422UnprocessableEntity,
                    ErrorCatalog.Resolve(ErrorCatalog.ManifestOperationNotAllowedCode),
                    unknownOperation)
                    .ConfigureAwait(false);

                return;
            }

            try
            {
                request = JsonSerializer.Deserialize<PackageRequest>(payload, SerializerOptions);
            }
            catch (Exception exception) when (exception is JsonException or NotSupportedException)
            {
                // The body cannot be read into the contracts of Shared. The serializer
                // throws JsonException for a malformed document and NotSupportedException
                // for a polymorphic operation whose discriminator is not the first
                // property, and both are the same rejection for the organizer: a body
                // that does not have the shape the contract declares (AD-5, AD-15).
                logger.LogWarning(exception, "Se rechaza la publicación del paquete: el cuerpo no se puede leer.");

                await WriteErrorAsync(
                    context,
                    metrics,
                    logger,
                    StatusCodes.Status400BadRequest,
                    ErrorCatalog.Resolve(ErrorCatalog.PackageBodyMalformedCode),
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
                        data.ManifestIdentity,
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
            catch (PackageManifestMismatchException exception)
            {
                // El manifiesto y el descriptor son dos formas de la misma publicación, y
                // su identidad tiene que coincidir: si no coincide, la correspondencia
                // entre el manifiesto y el paquete no está firmada, y se rechaza antes de
                // escribir nada (AD-2, AC7).
                await WriteErrorAsync(
                    context,
                    metrics,
                    logger,
                    StatusCodes.Status422UnprocessableEntity,
                    ErrorCatalog.Resolve(ErrorCatalog.PackageManifestMismatchCode),
                    exception.FieldName)
                    .ConfigureAwait(false);
            }
            catch (SchemaVersionNotSupportedException exception)
            {
                // El manifiesto declara una versión de su propio esquema que este
                // servidor no entiende: se rechaza antes de escribir nada (AD-8). La
                // versión del esquema del CUERPO es otra cosa, y su rechazo se resuelve
                // antes de llegar aquí (AD-5).
                await WriteErrorAsync(
                    context,
                    metrics,
                    logger,
                    StatusCodes.Status400BadRequest,
                    ErrorCatalog.Resolve(ErrorCatalog.SchemaVersionNotSupportedCode),
                    exception.Message)
                    .ConfigureAwait(false);
            }
            catch (ArgumentException exception)
            {
                // El cuerpo lleva los datos, pero alguno de sus campos no tiene la forma
                // que el contrato declara: es un cuerpo que no se puede leer, y su
                // rechazo es un 400 con su propio código, no el 500 de la persistencia
                // (AD-5, AD-15).
                logger.LogWarning(exception, "Se rechaza la publicación del paquete: el cuerpo no tiene la forma declarada.");

                await WriteErrorAsync(
                    context,
                    metrics,
                    logger,
                    StatusCodes.Status400BadRequest,
                    ErrorCatalog.Resolve(ErrorCatalog.PackageBodyMalformedCode),
                    null)
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
    /// Finds the operation of the manifest that is outside the closed set of AD-7.
    ///
    /// <para>
    /// The body is inspected before the contract is deserialized, because the contract
    /// cannot hold an operation that it does not declare: the deserialization of such a
    /// body fails, and a failure of the deserialization that is not an operation outside
    /// the closed set has to be a different rejection (AD-7, AD-8, AD-15).
    /// </para>
    /// <para>
    /// The lookup of the properties is case-INSENSITIVE, the same way the deserializer
    /// that follows is: <c>JsonSerializerDefaults.Web</c> matches the name of a property
    /// without regard to its case, so a body that spells <c>"Manifest"</c> or
    /// <c>"Operations"</c> differently has to reach the same rejection, and the metric of
    /// errors per code does not split a single case in two (AD-15, AD-25).
    /// </para>
    /// </summary>
    /// <param name="payload">Raw body of the request.</param>
    /// <returns>
    /// The name of the first operation that is outside the closed set, or <c>null</c> when
    /// every operation of the body belongs to it.
    /// </returns>
    /// <exception cref="JsonException">The body is not a well formed JSON document.</exception>
    private static string? FindUnknownOperation(byte[] payload)
    {
        using var document = JsonDocument.Parse(payload);

        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !TryGetProperty(document.RootElement, "manifest", out var manifest)
            || manifest.ValueKind != JsonValueKind.Object
            || !TryGetProperty(manifest, "operations", out var operations)
            || operations.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var operation in operations.EnumerateArray())
        {
            if (operation.ValueKind != JsonValueKind.Object
                || !TryGetProperty(operation, "operation", out var discriminator)
                || discriminator.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var name = discriminator.GetString();

            if (!ManifestOperations.IsAllowed(name))
            {
                return name ?? string.Empty;
            }
        }

        return null;
    }

    /// <summary>
    /// Reads a property of a JSON object without regard to the case of its name, the same
    /// way the deserializer that follows reads it (<c>JsonSerializerDefaults.Web</c>).
    ///
    /// <para>
    /// When the object holds the property more than once with different spellings, the
    /// first one in the order of the document wins, because that is the order in which
    /// the deserializer resolves the ambiguity too.
    /// </para>
    /// </summary>
    /// <param name="element">Object that holds the property.</param>
    /// <param name="name">Name of the property.</param>
    /// <param name="value">The value of the property, when it is found.</param>
    /// <returns><c>true</c> when the object holds the property.</returns>
    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
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
