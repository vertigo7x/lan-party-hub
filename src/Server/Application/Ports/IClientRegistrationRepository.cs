using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Server.Application.Ports;

/// <summary>
/// Port of the registry of the clients of the event (AD-22, AD-23, AD-30).
///
/// <para>
/// The registry is owned by the server: the row of a client is keyed by its
/// <c>clientId</c> and never by its name (AD-23). The port declares the three
/// operations that the use case of the registration needs: find the row of a client
/// by its identifier, find the row that holds a given CANONICAL form of a name —which
/// is how a collision is detected— and save the row (AD-30).
/// </para>
/// <para>
/// El puerto no dice NADA de cómo se guarda la fila: el caso de uso depende de esta
/// abstracción y nunca del adaptador que la implementa (AD-1).
/// </para>
/// </summary>
public interface IClientRegistrationRepository
{
    /// <summary>
    /// Finds the row of the client that carries the given identifier.
    /// </summary>
    /// <param name="clientId">Canonical identifier of the client.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The row of the client, or <c>null</c> when the server does not know it.</returns>
    Task<ClientRegistrationRow?> FindByClientIdAsync(
        string clientId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Finds the row that holds a given CANONICAL form of a name, EXCLUDING the row of
    /// the client that is registering (AD-30).
    ///
    /// <para>
    /// La comparación es por forma canónica, no por el texto almacenado: el nombre
    /// guardado conserva su capitalización original, y lo que se compara es su forma
    /// canónica (AD-30).
    /// </para>
    /// <para>
    /// La EXCLUSIÓN del propio cliente es lo que hace que la colisión sea detectable:
    /// un cliente que se reconecta con su nombre efectivo tiene su propia fila en la
    /// forma canónica buscada, y sin la exclusión esa fila taparía la de otro cliente
    /// que use el mismo nombre (AD-30).
    /// </para>
    /// </summary>
    /// <param name="canonicalMachineName">Canonical form of the name of the team.</param>
    /// <param name="excludedClientId">Identifier of the client that is registering.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The row that holds the name, or <c>null</c> when the name is free.</returns>
    Task<ClientRegistrationRow?> FindByCanonicalMachineNameAsync(
        string canonicalMachineName,
        string excludedClientId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads every row of the registry.
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>Every registered client.</returns>
    Task<IReadOnlyList<ClientRegistrationRow>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Saves the row of the client: it writes it when it is new and it completes it
    /// when it already exists, so a row is NEVER duplicated (AD-30).
    /// </summary>
    /// <param name="row">Row of the client to save.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the row has been saved.</returns>
    Task SaveAsync(ClientRegistrationRow row, CancellationToken cancellationToken);
}

/// <summary>
/// A row of the registry of the clients, as the use case of the registration sees it
/// (AD-22, AD-23, AD-30).
///
/// <para>
/// La fila lleva el identificador técnico, el nombre EFECTIVO del equipo, las
/// versiones que el cliente declaró, el estado de conexión y el instante del último
/// contacto. El nombre que viaja es siempre el efectivo: el propuesto que colisionó
/// nunca se persiste (AD-30).
/// </para>
/// </summary>
/// <param name="ClientId">Opaque UUID v7 identifier of the client; the key of the row.</param>
/// <param name="MachineName">Effective human label of the team.</param>
/// <param name="ClientVersion">Version of the binary of the client.</param>
/// <param name="ProtocolVersion">Protocol version that the client speaks.</param>
/// <param name="ConnectionState">Current connection state of the client.</param>
/// <param name="LastSeenAt">Instant of the last contact, in UTC.</param>
public sealed record ClientRegistrationRow(
    string ClientId,
    string MachineName,
    string ClientVersion,
    int ProtocolVersion,
    ConnectionState ConnectionState,
    DateTimeOffset LastSeenAt);
