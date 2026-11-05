using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Domain;

namespace LANPartyHub.Client.Application;

/// <summary>
/// Outcome of the startup of the client (AD-9, AD-11, AD-17, AD-23, AD-24).
/// </summary>
public sealed record ClientStartupResult
{
    /// <summary>Identity of the client, emitted once and persisted (AD-23).</summary>
    public required ClientIdentity Identity { get; init; }

    /// <summary>
    /// Layout of the data folder of the client, as the live INSTANCE.
    ///
    /// <para>
    /// La instancia se comparte con las composiciones que la necesitan, porque el
    /// directorio de instalación es el único confinado y su cambio tiene que surtir
    /// efecto INMEDIATO: una composición con su propia copia del layout confinaría la
    /// raíz anterior después de que el jugador la moviera (AD-9).
    /// </para>
    /// </summary>
    public required IClientDataLayout Layout { get; init; }

    /// <summary>Absolute path of the data folder of the client.</summary>
    public required string DataFolder { get; init; }

    /// <summary>Absolute path of the installation root, the only confined directory (AD-9).</summary>
    public required string InstallDirectory { get; init; }

    /// <summary>Absolute path of the folder that holds the partial downloads.</summary>
    public required string DownloadsDirectory { get; init; }

    /// <summary>Absolute path of the cache of the signed manifests.</summary>
    public required string ManifestsDirectory { get; init; }

    /// <summary>Absolute path of the SQLite file of the client.</summary>
    public required string StateDatabasePath { get; init; }

    /// <summary>Address of the event server, or <c>null</c> when no source fixes it.</summary>
    public string? ServerAddress { get; init; }

    /// <summary>Theme of the interface of the client.</summary>
    public required string Theme { get; init; }

    /// <summary>
    /// Number of downloads that the client runs at the same time (AD-14, AC10).
    /// </summary>
    public int MaxConcurrentDownloads { get; init; } = 2;

    /// <summary>
    /// Number of connections that the client opens in total (AD-14, AC10).
    /// </summary>
    public int MaxTotalConnections { get; init; } = 4;

    /// <summary>Result of the reconciliation of the persisted progress.</summary>
    public required DownloadProgressReconciliation Reconciliation { get; init; }

    /// <summary>
    /// Settings of the client, ready to be used by the interface of the player. The
    /// composition that builds them lives as long as the startup.
    /// </summary>
    public ClientSettings? Settings { get; init; }
}
