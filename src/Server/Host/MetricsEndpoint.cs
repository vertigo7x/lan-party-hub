using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace LANPartyHub.Server.Host;

/// <summary>
/// Local metrics endpoint of the host (AD-15, NFR-10).
///
/// The endpoint lives on the same HTTP port as the API and the SignalR hub, so the server
/// does not open a single port more than the two it needs (AD-12). It publishes the transfer
/// rate, the number of active operations and the errors counted per code; the values are
/// instrumented by Story 1.4.
/// </summary>
public static class MetricsEndpoint
{
    /// <summary>Route of the metrics endpoint.</summary>
    public const string Route = "/metrics";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Serializes a snapshot with the exact shape that the endpoint publishes.
    /// </summary>
    /// <param name="snapshot">Snapshot of the metrics.</param>
    /// <returns>The JSON payload of the endpoint.</returns>
    public static string Serialize(HostMetricsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return JsonSerializer.Serialize(snapshot, SerializerOptions);
    }

    /// <summary>
    /// Registers the metrics endpoint in the application.
    /// </summary>
    /// <param name="app">Application of the server.</param>
    /// <param name="metrics">Metrics of the host.</param>
    public static void Map(WebApplication app, HostMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(metrics);

        app.MapGet(Route, () => Results.Text(Serialize(metrics.GetSnapshot()), "application/json", Encoding.UTF8));
    }
}
