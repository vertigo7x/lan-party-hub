using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Client.Application.Connection;

/// <summary>
/// Attempt of connecting with the event server by the address that the player typed
/// (AC1, AC3, AD-3).
///
/// <para>
/// The attempt carries the endpoint that the address described, the state of the
/// connection and, when the attempt failed, the error of the catalog with its three parts
/// (AC1, AC3, AR-4).
/// </para>
/// <para>
/// The state is the one of the contract <see cref="ConnectionState"/>: a connection that
/// opened WITHOUT fixing the trust anchor is
/// <see cref="ConnectionState.AnchorUnverified"/>, because connecting does NOT make the
/// server trustworthy (AD-6, AD-27).
/// </para>
/// <para>
/// AD-6, FR-4: la incompatibilidad de la versión NO es un estado: el estado queda
/// <see cref="ConnectionState.Disconnected"/> y el diagnóstico va aparte, con la versión
/// del cliente y la del servidor (AD-27).
/// </para>
/// </summary>
/// <param name="Endpoint">
/// Endpoint of the server, or <c>null</c> when the address was malformed and no endpoint
/// could be built.
/// </param>
/// <param name="State">State of the connection after the attempt.</param>
/// <param name="Error">
/// Error of the catalog that explains what failed, why and what to do now, or
/// <c>null</c> when the attempt succeeded.
/// </param>
/// <param name="IncompatibleClientVersion">
/// Version of the client when the server rejected it as incompatible, or <c>null</c> when
/// the attempt did not fail for that reason (AD-6).
/// </param>
/// <param name="IncompatibleServerVersion">
/// Version of the server when the server rejected the client as incompatible, or
/// <c>null</c> when the attempt did not fail for that reason (AD-6).
/// </param>
public sealed record ConnectionAttempt(
    ServerEndpoint? Endpoint,
    ConnectionState State,
    ApiError? Error,
    string? IncompatibleClientVersion = null,
    string? IncompatibleServerVersion = null,
    string? PresentedAnchorFingerprint = null,
    string? FixedAnchorFingerprint = null)
{
    /// <summary>
    /// Whether the attempt opened the connection with the server (AC1).
    /// </summary>
    public bool IsConnected => State != ConnectionState.Disconnected;

    /// <summary>
    /// Whether the server rejected the client because its version is not compatible
    /// (AD-6, FR-4).
    /// </summary>
    public bool IsVersionIncompatible => IncompatibleClientVersion is not null;

    /// <summary>
    /// Whether the server presented an ancla de confianza that is NOT the one that the
    /// client has fixed (AC6, AD-27).
    ///
    /// <para>
    /// AD-27: el ancla fijada es INMUTABLE. Una clave distinta no se sustituye: se
    /// rechaza y exige un emparejamiento fuera de banda nuevo.
    /// </para>
    /// </summary>
    public bool IsAnchorMismatch => PresentedAnchorFingerprint is not null;

    /// <summary>
    /// Builds the attempt of a connection that opened (AC1).
    /// </summary>
    /// <param name="endpoint">Endpoint of the server of the event.</param>
    /// <returns>The attempt of the connection.</returns>
    public static ConnectionAttempt Succeeded(ServerEndpoint endpoint) =>
        new(endpoint, ConnectionState.AnchorUnverified, null);

    /// <summary>
    /// Builds the attempt of a connection that did NOT open (AC3).
    /// </summary>
    /// <param name="endpoint">
    /// Endpoint of the server, or <c>null</c> when the address was malformed.
    /// </param>
    /// <param name="error">Error of the catalog that explains the failure.</param>
    /// <returns>The attempt of the connection.</returns>
    public static ConnectionAttempt Failed(ServerEndpoint? endpoint, ApiError error) =>
        new(endpoint, ConnectionState.Disconnected, error);

    /// <summary>
    /// Builds the attempt of a connection that the server rejected because the version of
    /// the client is not compatible with it (AD-6, FR-4).
    ///
    /// <para>
    /// El estado queda DESCONECTADO: el cliente incompatible no sincroniza el catálogo,
    /// no descarga y no instala (AD-6).
    /// </para>
    /// </summary>
    /// <param name="endpoint">Endpoint of the server of the event.</param>
    /// <param name="error">Error of the catalog que explica la incompatibilidad.</param>
    /// <param name="clientVersion">Version of the client that the server rejected.</param>
    /// <param name="serverVersion">Version of the server, when it published it.</param>
    /// <returns>The attempt of the connection.</returns>
    public static ConnectionAttempt Incompatible(
        ServerEndpoint endpoint,
        ApiError error,
        string clientVersion,
        string? serverVersion) =>
        new(
            endpoint,
            ConnectionState.Disconnected,
            error,
            clientVersion,
            serverVersion);

    /// <summary>
    /// Builds the attempt of a connection that the client rejected because the server
    /// presented an ancla de confianza that is NOT the one that the client has fixed
    /// (AC6, AD-27).
    ///
    /// <para>
    /// AD-27: el estado queda DESCONECTADO y el ancla fijada NO se sustituye. El
    /// emparejamiento nuevo es una operación explícita del organizador, con un fingerprint
    /// nuevo verificado fuera de banda (AC6, AD-27).
    /// </para>
    /// </summary>
    /// <param name="endpoint">Endpoint of the server of the event.</param>
    /// <param name="error">Error of the catalog that explains the rejection.</param>
    /// <param name="presentedFingerprint">Fingerprint that the server presented.</param>
    /// <param name="fixedFingerprint">Fingerprint that the client has fixed.</param>
    /// <returns>The attempt of the connection.</returns>
    public static ConnectionAttempt AnchorMismatch(
        ServerEndpoint endpoint,
        ApiError error,
        string presentedFingerprint,
        string fixedFingerprint) =>
        new(
            endpoint,
            ConnectionState.Disconnected,
            error,
            IncompatibleClientVersion: null,
            IncompatibleServerVersion: null,
            PresentedAnchorFingerprint: presentedFingerprint,
            FixedAnchorFingerprint: fixedFingerprint);
}
