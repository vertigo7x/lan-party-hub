using System.Text.Json;
using LANPartyHub.Server.Application.Ports;
using LANPartyHub.Shared.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace LANPartyHub.Server.Host;

/// <summary>
/// Surface that publishes the versions of the server (AD-6, AD-27).
///
/// <para>
/// The route <c>GET /api/v1/server/info</c> is the one that AD-27 names: it publishes the
/// version of the binary del servidor y la versión del protocolo que habla, para que el
/// cliente pueda compararlas con las suyas SIN interpretar texto (AD-6).
/// </para>
/// <para>
/// The route belongs to the surface of the client, so it is covered by the middleware of
/// the correlation and by the gate of the compatibility: un cliente incompatible NUNCA
/// llega a esta ruta, y el <c>426</c> del gate le dice lo que necesita para actualizarse
/// (AD-6, AD-15).
/// </para>
/// <para>
/// La respuesta es el contrato <see cref="VersionInfo"/> de <c>Shared</c>, sin tipos
/// nuevos (AD-18, AD-33). La historia 4.4 añadirá a esta misma ruta el fingerprint del
/// ancla de confianza, que el cliente tendrá que contrastar fuera de banda (AD-27).
/// </para>
/// </summary>
public static class ServerInfoEndpoint
{
    /// <summary>Route that publishes the versions of the server.</summary>
    public const string Route = "/api/v1/server/info";

    /// <summary>
    /// Header that carries the name of the server of the event.
    ///
    /// <para>
    /// El nombre viaja como CABECERA, con el mismo patrón que <c>X-Server-Version</c>, y no
    /// como un campo nuevo del contrato <see cref="VersionInfo"/>: el contrato de
    /// <c>Shared</c> no crece (AD-18, AD-33).
    /// </para>
    /// </summary>
    public const string ServerNameHeaderName = "X-Server-Name";

    /// <summary>
    /// Name of the server of the event when no source fixes it: the name of the machine
    /// that hosts it.
    ///
    /// <para>
    /// El nombre es una ETIQUETA legible para el jugador, no un identificador de protocolo:
    /// el cliente lo muestra en el panel de conexión y nada más (AC1).
    /// </para>
    /// </summary>
    public const string DefaultServerName = "Servidor de LAN Party";

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
    /// Registers the publication of the versions of the server in the application.
    /// </summary>
    /// <param name="app">Application of the server.</param>
    public static void Map(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet(Route, (HttpContext context) =>
        {
            // AD-27: el servidor PUBLICA su ancla para que el jugador la CONTRASTE con
            // la que el organizador le entregó fuera de banda. Publicarla NO la hace
            // confiable: el cliente deriva el fingerprint de la clave que llega y solo
            // persiste el ancla cuando la comparación la confirma el jugador.
            var anchor = app.Services.GetRequiredService<IManifestSigner>().GetTrustAnchor();

            var info = new VersionInfo(
                ClientVersion: string.Empty,
                ServerVersion: ServerVersion.Current,
                ProtocolVersion: ServerVersion.ProtocolVersion)
            {
                Anchor = anchor,
            };

            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "application/json; charset=utf-8";

            // La versión del servidor y el rango mínimo que soporta viajan también como
            // cabeceras, igual que en el 426, para que el cliente los lea siempre de la
            // misma forma (AD-6).
            context.Response.Headers[Infrastructure.Http.ClientVersionMiddleware.ServerVersionHeaderName] =
                ServerVersion.Current;

            context.Response.Headers[Infrastructure.Http.ClientVersionMiddleware.MinimumProtocolVersionHeaderName] =
                app.Services
                    .GetRequiredService<ServerConfiguration>()
                    .MinimumProtocolVersion
                    .ToString(System.Globalization.CultureInfo.InvariantCulture);

            // El nombre del servidor viaja como CABECERA, con el mismo patrón que la versión:
            // el panel de conexión del cliente lo muestra como etiqueta legible y el
            // contrato de Shared no crece (AD-18, AD-33).
            context.Response.Headers[ServerNameHeaderName] = ResolveServerName();

            return context.Response.WriteAsync(Serialize(info), context.RequestAborted);
        });
    }

    /// <summary>
    /// Resolves the name of the server of the event: the name of the machine that hosts it,
    /// or the default name when the machine does not publish one.
    ///
    /// <para>
    /// El nombre es una ETIQUETA legible para el jugador, nunca un identificador de
    /// protocolo: el cliente lo muestra en el panel de conexión y nada más (AC1).
    /// </para>
    /// </summary>
    /// <returns>The name of the server of the event.</returns>
    public static string ResolveServerName()
    {
        var machineName = Environment.MachineName;

        return string.IsNullOrWhiteSpace(machineName) ? DefaultServerName : machineName.Trim();
    }
}
