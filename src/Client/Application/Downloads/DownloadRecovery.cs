using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Domain.StateMachines;
using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Client.Application.Downloads;

/// <summary>
/// Recovery of the download operations that survived a restart of the machine
/// (AD-11, AD-20, AD-24, SM-2).
///
/// <para>
/// La recuperación REHIDRATA las operaciones persistidas en operaciones VIVAS: la
/// operación conserva su <c>operationId</c> y su <c>packageId</c>, y su offset efectivo
/// es el que la reconciliación YA corrigió contra el archivo parcial del disco. La
/// rehidratación NO recalcula el offset: lo LEE, y lo lee ya corregido (AD-11).
/// </para>
/// <para>
/// La operación recuperada NO registra una intención nueva con el servidor: la intención
/// ya está registrada y volver a llamarla a <c>POST /api/v1/downloads</c> la duplicaría
/// (AC1, AD-10). La reanudación pide el rango que empieza en el offset efectivo, así que
/// el motor lo sirve con un <c>Range</c> sobre ese offset (AD-20, AD-24).
/// </para>
/// <para>
/// Una operación cuyo estado es TERMINAL no se recupera: una descarga completada, cancelada
/// o fallida no se reanuda (AD-10, AC5). Una operación en un estado que la máquina no
/// conoce se trata como no recuperable, y su registro queda en la base de datos para que
/// otra versión lo lea (AD-24).
/// </para>
/// </summary>
public static class DownloadRecovery
{
    /// <summary>
    /// States of the machine that a recovered operation can be resumed from. A terminal
    /// state is not among them (AD-10, AC5).
    /// </summary>
    /// <param name="state">State of the operation to inspect.</param>
    /// <returns><c>true</c> when the operation can be resumed.</returns>
    public static bool IsResumableState(DownloadState state) => state switch
    {
        DownloadState.Queued => true,
        DownloadState.Downloading => true,
        DownloadState.Paused => true,
        _ => false,
    };

    /// <summary>
    /// Resolves the state of a persisted operation, or <c>null</c> when the name that was
    /// persisted is not a state of the machine (AD-10, AD-24).
    /// </summary>
    /// <param name="state">Name of the persisted state.</param>
    /// <returns>The state, or <c>null</c> when it is unknown.</returns>
    public static DownloadState? ResolveState(string? state)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            return null;
        }

        return Enum.TryParse<DownloadState>(state, ignoreCase: false, out var parsed)
            ? parsed
            : null;
    }

    /// <summary>
    /// Reads the effective offset of a persisted operation. The offset is el que la
    /// reconciliación YA corrigió contra el archivo parcial, así que la recuperación NO lo
    /// recalcula: lo lee tal cual (AD-11, AD-24).
    /// </summary>
    /// <param name="operation">Persisted operation.</param>
    /// <returns>The effective offset, never negative.</returns>
    public static long ReadEffectiveOffset(PersistedDownloadOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        return Math.Max(0L, operation.BytesPersisted ?? 0L);
    }

    /// <summary>
    /// Rehydrates a persisted operation into a LIVE operation, with the effective offset
    /// sembrado y the total that was persisted (AD-11, AD-24).
    ///
    /// <param name="operation">Persisted operation to rehydrate.</param>
    /// <param name="contentUri">
    /// Absolute URL of the content of the package, which the caller resolves because the
    /// url no vive en la base de datos del cliente.
    /// </param>
    /// <param name="state">State that the operation had, already resolved.</param>
    /// <returns>The live handle of the recovered operation.</returns>
    public static DownloadOperationHandle Rehydrate(
        PersistedDownloadOperation operation,
        Uri contentUri,
        DownloadState state)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(contentUri);

        var handle = new DownloadOperationHandle(
            operation.OperationId,
            operation.PackageId,
            contentUri,
            new DownloadStateMachine(state));

        // La siembra del offset es lo que hace que la reanudación empiece en la posición
        // persistida y no en cero (AD-20, AD-24).
        handle.SetProgress(
            ReadEffectiveOffset(operation),
            operation.TotalBytes);

        return handle;
    }
}

/// <summary>
/// Outcome of the gate of the compatibility of the versions before a download starts or
/// resumes (AD-6, FR-4, AC5).
///
/// <para>
/// La incompatibilidad NO es un estado: es un DIAGNÓSTICO. El cliente incompatible no
/// descarga, no reanuda y no instala, y lo que el jugador ve es el texto con las dos
/// versiones y el siguiente paso (AD-6, AD-27).
/// </para>
/// <para>
/// Un servidor que NO responde NO es una incompatibilidad: el jugador tiene que poder
/// reintentar, así que ese caso se distingue del rechazo (AC3, UX-DR-36).
/// </para>
/// </summary>
public sealed record DownloadCompatibilityGate
{
    /// <summary>
    /// Whether the server rejected the version of the client (AD-6).
    /// </summary>
    public required bool IsIncompatible { get; init; }

    /// <summary>
    /// Whether the compatibility could not be checked because the server did not answer
    /// (AC3).
    /// </summary>
    public required bool IsReachable { get; init; }

    /// <summary>
    /// Version of the server, when it published it (AD-6).
    /// </summary>
    public string? ServerVersion { get; init; }

    /// <summary>Whether the client may download against the server.</summary>
    public bool MayDownload => !IsIncompatible;

    /// <summary>A gate that lets the download proceed.</summary>
    /// <returns>The gate of a compatible client.</returns>
    public static DownloadCompatibilityGate Compatible() => new()
    {
        IsIncompatible = false,
        IsReachable = true,
    };

    /// <summary>A gate that reports that the server rejected the version of the client.</summary>
    /// <param name="serverVersion">Version of the server, when it published it.</param>
    /// <returns>The gate of an incompatible client.</returns>
    public static DownloadCompatibilityGate Incompatible(string? serverVersion) => new()
    {
        IsIncompatible = true,
        IsReachable = true,
        ServerVersion = serverVersion,
    };

    /// <summary>A gate that reports that the compatibility could not be checked.</summary>
    /// <returns>The gate of an unreachable server.</returns>
    public static DownloadCompatibilityGate Unreachable() => new()
    {
        IsIncompatible = false,
        IsReachable = false,
    };
}
