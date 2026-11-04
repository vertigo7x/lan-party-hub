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
/// Surface of the client that serves the signed installation manifest of a published
/// package (AD-2, AD-8, AD-20).
///
/// <para>
/// El manifiesto firmado viaja APARTE de los bytes del paquete: el cliente lo pide ANTES
/// de descargar, valida la firma y la versión del esquema, y solo entonces crea el
/// archivo parcial (AD-8, AD-20).
/// </para>
/// <para>
/// La respuesta lleva el texto CANÓNICO del manifiesto, la firma y el identificador de la
/// clave, que son los tres elementos del sobre firmado (AD-2). Los bytes que el cliente
/// verifica son EXACTAMENTE los que la firma cubre, porque el servidor sirve el texto que
/// firmó y no una forma que el servidor recalcula (AD-2).
/// </para>
/// <para>
/// La ruta es de la superficie del cliente, así que exige el identificador del cliente y
/// está correlacionada por el middleware de la correlación (AD-15, AD-23). Todo rechazo
/// es la forma única <see cref="ApiError"/> y se cuenta en la métrica de errores por
/// código (AD-15, AD-25).
/// </para>
/// </summary>
public static class PackageManifestEndpoint
{
    /// <summary>Route that serves the signed manifest of a published package.</summary>
    public const string Route = "/api/v1/packages/{packageId}/manifest";

    /// <summary>
    /// Version of the schema of the answer of this endpoint (AD-5).
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
    /// Registers the service of the signed manifests of the packages in the application.
    /// </summary>
    /// <param name="app">Application of the server.</param>
    public static void Map(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet(Route, async (
            HttpContext context,
            GetSignedManifest getSignedManifest,
            IMetricsCounters metrics,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("LANPartyHub.Server.PackageManifest");

            var packageId = context.Request.RouteValues["packageId"] as string;

            if (string.IsNullOrWhiteSpace(packageId))
            {
                await WriteErrorAsync(
                    context,
                    metrics,
                    logger,
                    StatusCodes.Status400BadRequest,
                    ErrorCatalog.Resolve(ErrorCatalog.DownloadPackageMissingCode))
                    .ConfigureAwait(false);

                return;
            }

            SignedManifestRow? manifest;

            try
            {
                manifest = await getSignedManifest
                    .ExecuteAsync(packageId, context.RequestAborted)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // La excepción nunca alcanza el mensaje de la respuesta: el operador la
                // encuentra en el log, correlacionada por su operationId (AD-15).
                logger.LogError(
                    exception,
                    "No se pudo leer el manifiesto firmado del paquete {PackageId}.",
                    packageId);

                await WriteErrorAsync(
                    context,
                    metrics,
                    logger,
                    StatusCodes.Status500InternalServerError,
                    ErrorCatalog.Resolve(ErrorCatalog.UnknownErrorCode))
                    .ConfigureAwait(false);

                return;
            }

            if (manifest is null)
            {
                // El servidor no tiene manifiesto firmado para ese paquete: la ruta no
                // existe (AD-22).
                await WriteErrorAsync(
                    context,
                    metrics,
                    logger,
                    StatusCodes.Status404NotFound,
                    ErrorCatalog.Resolve(ErrorCatalog.DownloadPackageNotFoundCode))
                    .ConfigureAwait(false);

                return;
            }

            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "application/json; charset=utf-8";

            await context.Response
                .WriteAsync(
                    Serialize(new SignedManifestResponse(
                        SupportedSchemaVersion,
                        manifest.CanonicalManifest,
                        manifest.Signature,
                        manifest.KeyId)),
                    context.RequestAborted)
                .ConfigureAwait(false);
        });
    }

    /// <summary>
    /// Writes the single error shape of the system in the response and counts the error
    /// under its stable code (AD-15, AD-25).
    /// </summary>
    /// <param name="context">Context of the request.</param>
    /// <param name="metrics">Counters of the host.</param>
    /// <param name="logger">Log of the service of the signed manifests.</param>
    /// <param name="statusCode">Status of the response.</param>
    /// <param name="error">Error to write.</param>
    /// <returns>A task that completes when the error has been written.</returns>
    private static async Task WriteErrorAsync(
        HttpContext context,
        IMetricsCounters metrics,
        ILogger logger,
        int statusCode,
        ApiError error)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(error);

        metrics.RecordError(error.Code);

        logger.LogWarning(
            "Se rechaza la petición del manifiesto firmado con el código {Code} y el estado {StatusCode}.",
            error.Code,
            statusCode);

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";

        await context.Response
            .WriteAsync(Serialize(error), context.RequestAborted)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Answer of the service of the signed manifest of a package (AD-2, AD-5, AD-20).
    ///
    /// <para>
    /// Los tres elementos son los del SOBRE firmado: el texto canónico del manifiesto, la
    /// firma que lo cubre y el identificador de la clave que la produjo (AD-2).
    /// </para>
    /// </summary>
    /// <param name="SchemaVersion">Version of the schema of the answer that the server speaks.</param>
    /// <param name="Manifest">Canonical JSON text of the manifest, as it was signed.</param>
    /// <param name="Signature">Ed25519 signature over the canonical text, in lowercase hexadecimal.</param>
    /// <param name="KeyId">Identifier of the key that produced the signature.</param>
    private sealed record SignedManifestResponse(
        int SchemaVersion,
        string Manifest,
        string Signature,
        string KeyId);
}
