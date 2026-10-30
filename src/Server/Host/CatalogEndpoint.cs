using System.Text;
using System.Text.Json;
using LANPartyHub.Server.Application.Catalog;
using LANPartyHub.Server.Application.Ports;
using LANPartyHub.Shared.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Server.Host;

/// <summary>
/// Surface of the client that publishes the complete snapshot of the catalog
/// (AC1, AC2, AD-5, AD-26).
///
/// <para>
/// The endpoint answers with the snapshot completo: the version of the catalog, the
/// videogames that are published and the packages that are not withdrawn. There is no delta
/// in the MVP (AD-26), so the route is the only way of reading the catalog and the client
/// applies the snapshot that it receives when its version is greater than the one it holds
/// (AC1).
/// </para>
/// <para>
/// The route belongs to the surface of the client, so it requires the identifier of the
/// client and it is correlated by the middleware of the correlation (AD-15, AD-23).
/// </para>
/// <para>
/// Every rejection is the single error shape of the system, <see cref="ApiError"/>, with
/// the stable code of the catalog of errors, and it is counted in the metric of errors per
/// code (AD-15, AD-25).
/// </para>
/// </summary>
public static class CatalogEndpoint
{
    /// <summary>Route that publishes the complete snapshot of the catalog.</summary>
    public const string Route = "/api/v1/catalog";

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
    /// Registers the publication of the snapshot of the catalog in the application.
    /// </summary>
    /// <param name="app">Application of the server.</param>
    public static void Map(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet(Route, async (
            HttpContext context,
            GetCatalogSnapshot getCatalogSnapshot,
            IMetricsCounters metrics,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("LANPartyHub.Server.Catalog");

            try
            {
                var snapshot = await getCatalogSnapshot
                    .ExecuteAsync(context.RequestAborted)
                    .ConfigureAwait(false);

                context.Response.StatusCode = StatusCodes.Status200OK;
                context.Response.ContentType = "application/json; charset=utf-8";

                await context.Response
                    .WriteAsync(Serialize(snapshot), context.RequestAborted)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // La excepción nunca alcanza el mensaje de la respuesta: el operador la
                // encuentra en el log, correlacionada por su operationId (AD-15).
                logger.LogError(
                    exception,
                    "No se pudo leer el snapshot del catálogo: la persistencia ha fallado.");

                var error = ErrorCatalog.Resolve(ErrorCatalog.UnknownErrorCode);

                metrics.RecordError(error.Code);

                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                context.Response.ContentType = "application/json; charset=utf-8";

                await context.Response
                    .WriteAsync(Serialize(error), context.RequestAborted)
                    .ConfigureAwait(false);
            }
        });
    }
}
