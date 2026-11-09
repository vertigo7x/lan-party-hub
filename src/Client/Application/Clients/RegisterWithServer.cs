using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Domain;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Client.Application.Clients;

/// <summary>
/// Use case of the registration of the client against the event server (AD-30, AD-32).
///
/// <para>
/// El caso de uso llama al ÚNICO punto de entrada del cliente al servidor y ADOPTA el
/// nombre efectivo que el servidor devuelve: el nombre que se propone en las
/// reconexiones siguientes es el efectivo, de modo que un cliente reconectado nunca
/// colisiona consigo mismo (AD-30, AD-32).
/// </para>
/// <para>
/// Un rechazo por nombre mal formado NO se reintenta con el mismo nombre: el rechazo
/// significa que el cliente queda SIN REGISTRAR y no puede operar, y el siguiente
/// paso lo dice el propio error (AD-30, AD-32).
/// </para>
/// <para>
/// El caso de uso depende del puerto declarado en esta capa y nunca de un adaptador,
/// así que puede ejercitarse sin una red (AD-1).
/// </para>
/// </summary>
public sealed class RegisterWithServer
{
    private readonly IClientRegistrationClient _registrationClient;
    private readonly IClientEffectiveNameStore _nameStore;
    private readonly IClientVersionProvider _versions;
    private readonly ILogger<RegisterWithServer> _logger;

    /// <summary>Creates the use case of the registration against the server.</summary>
    /// <param name="registrationClient">Port of the registration against the server.</param>
    /// <param name="nameStore">Access to the effective name of the team.</param>
    /// <param name="versions">Versions that the client declares in every request.</param>
    /// <param name="logger">Log of the registration.</param>
    public RegisterWithServer(
        IClientRegistrationClient registrationClient,
        IClientEffectiveNameStore nameStore,
        IClientVersionProvider versions,
        ILogger<RegisterWithServer> logger)
    {
        ArgumentNullException.ThrowIfNull(registrationClient);
        ArgumentNullException.ThrowIfNull(nameStore);
        ArgumentNullException.ThrowIfNull(versions);
        ArgumentNullException.ThrowIfNull(logger);

        _registrationClient = registrationClient;
        _nameStore = nameStore;
        _versions = versions;
        _logger = logger;
    }

    /// <summary>
    /// Reads the EFFECTIVE name of the team that the server assigned, or <c>null</c>
    /// when the client has not registered yet (AD-30, AD-31).
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The effective name of the team, or <c>null</c>.</returns>
    public Task<string?> LoadEffectiveNameAsync(CancellationToken cancellationToken) =>
        _nameStore.LoadEffectiveMachineNameAsync(cancellationToken);

    /// <summary>
    /// Registers the client against the server and persists the effective name of the
    /// team (AD-30, AD-32).
    /// </summary>
    /// <param name="identity">Identity of the client.</param>
    /// <param name="proposedName">Name proposed for the team.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The outcome of the registration.</returns>
    public async Task<ClientRegistrationOutcome> ExecuteAsync(
        ClientIdentity identity,
        string proposedName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(proposedName);

        var outcome = await _registrationClient
            .RegisterAsync(
                identity.CanonicalId,
                proposedName,
                _versions.ClientVersion,
                _versions.ProtocolVersion,
                cancellationToken)
            .ConfigureAwait(false);

        // AD-30, AD-31: el nombre efectivo es el único bajo el que la fila queda
        // registrada, así que el cliente lo ADOPTA y lo propone en las reconexiones
        // siguientes. Sin esto, el cliente volvería a proponer el nombre que colisionó
        // y el servidor le asignaría un nombre derivado distinto en cada reconexión.
        var effective = await _nameStore
            .LoadEffectiveMachineNameAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!string.Equals(effective, outcome.MachineName, StringComparison.Ordinal))
        {
            await _nameStore
                .SaveEffectiveMachineNameAsync(outcome.MachineName, cancellationToken)
                .ConfigureAwait(false);
        }

        if (outcome.Renamed)
        {
            _logger.LogInformation(
                "El nombre del equipo fue renombrado por el servidor: ahora es {MachineName}.",
                outcome.MachineName);
        }
        else
        {
            _logger.LogInformation(
                "El cliente {ClientId} quedó registrado con el nombre {MachineName}.",
                outcome.ClientId,
                outcome.MachineName);
        }

        return outcome;
    }
}

/// <summary>
/// Answer of the registration of the client against the event server (AD-32).
///
/// <para>
/// El <c>machineName</c> que viaja es SIEMPRE el nombre efectivo bajo el que la fila
/// queda registrada, y <c>renamed</c> vale <c>true</c> únicamente cuando el servidor
/// resolvió una colisión y el nombre devuelto difiere del propuesto (AD-32).
/// </para>
/// </summary>
/// <param name="ClientId">Canonical identifier of the client.</param>
/// <param name="MachineName">Effective name of the team.</param>
/// <param name="ConnectionState">Connection state that the row holds.</param>
/// <param name="LastSeenAt">Instant of the contact, in UTC.</param>
/// <param name="Renamed">Whether the server resolved a collision with a derived name.</param>
public sealed record ClientRegistrationOutcome(
    string ClientId,
    string MachineName,
    ConnectionState ConnectionState,
    DateTimeOffset LastSeenAt,
    bool Renamed);
