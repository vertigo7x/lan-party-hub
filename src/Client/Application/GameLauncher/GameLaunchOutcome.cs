using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Client.Application.GameLauncher;

/// <summary>
/// The outcome of a launch of a videogame (AD-19, FR-9, AC1).
///
/// <para>
/// El resultado distingue el RECHAZO —que no deja ningún proceso en marcha— del LANZAMIENTO
/// que sí ocurrió (AC1).
/// </para>
/// <para>
/// El rechazo lleva el `ApiError` COMPLETO del catálogo, y la CAUSA nombra la ruta
/// EXACTA cuando el problema es de ruta (AD-15, AD-25).
/// </para>
/// </summary>
public sealed record GameLaunchOutcome
{
    /// <summary>
    /// The launch was REJECTED and NO process was started (AC1).
    /// </summary>
    /// <param name="error">Error of the catalog that explains the rejection.</param>
    /// <param name="reason">
    /// Human readable reason of the rejection. It names the exact path when the problem
    /// is a path (AC1).
    /// </param>
    /// <returns>The outcome of the rejection.</returns>
    public static GameLaunchOutcome Rejected(ApiError error, string reason) =>
        new() { IsRejected = true, Error = error, Reason = reason };

    /// <summary>
    /// The videogame was LAUNCHED and its process is alive (AC1, AC2).
    /// </summary>
    /// <param name="handle">Handle of the process that is running.</param>
    /// <param name="executablePath">Absolute path of the executable that was started.</param>
    /// <returns>The outcome of the launch.</returns>
    public static GameLaunchOutcome Launched(IGameProcessHandle handle, string executablePath) =>
        new() { IsRejected = false, Handle = handle, ExecutablePath = executablePath };

    /// <summary>Whether the launch was rejected before any process was started (AC1).</summary>
    public bool IsRejected { get; init; }

    /// <summary>Whether the videogame was launched (AC1).</summary>
    public bool IsLaunched => !IsRejected;

    /// <summary>
    /// Error of the catalog that explains the rejection, or <c>null</c> when the videogame
    /// was launched (AC1).
    /// </summary>
    public ApiError? Error { get; init; }

    /// <summary>
    /// Human readable reason of the rejection, or <c>null</c> when the videogame was
    /// launched (AC1).
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// Handle of the process that is running, or <c>null</c> when the launch was rejected
    /// (AC1, AC2).
    /// </summary>
    public IGameProcessHandle? Handle { get; init; }

    /// <summary>
    /// Absolute path of the executable that was started, or <c>null</c> when the launch was
    /// rejected (AC1).
    /// </summary>
    public string? ExecutablePath { get; init; }
}
