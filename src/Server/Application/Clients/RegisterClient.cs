using LANPartyHub.Server.Application.Ports;
using LANPartyHub.Server.Domain.Clients;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging;
using System.Text;

namespace LANPartyHub.Server.Application.Clients;

/// <summary>
/// Use case of the registration of a client of the event (AD-30, AD-31, AD-32).
///
/// <para>
/// El alta, la reconexión y el renombrado ocurren en esta ÚNICA operación de
/// registro: el cliente llama con su identificador técnico, el nombre propuesto —el
/// efectivo que ya tiene en una reconexión, el nuevo en un renombrado— y sus
/// versiones (AD-32).
/// </para>
/// <para>
/// El ORDEN es normativo. Primero se valida la FORMA del nombre: un nombre mal
/// formado se rechaza y el cliente queda sin registrar y no puede operar (AD-30). Si
/// la forma es buena, la fila del cliente se busca por su <c>clientId</c>: si existe,
/// es una RECONEXIÓN y el servidor conserva la fila y el nombre, porque un cliente
/// reconectado nunca colisiona consigo mismo (AD-30, AD-32). Si no existe, el
/// nombre se busca por su FORMA CANÓNICA: si otro <c>clientId</c> ya lo usa, hay
/// COLISIÓN y el servidor asigna el nombre efectivo derivado (AD-31).
/// </para>
/// <para>
/// La colisión NO es un error: el registro tiene éxito con el nombre efectivo y el
/// indicador de renombrado en <c>true</c>, y el <c>clientId</c> de nadie cambia
/// (AD-30). El rechazo por nombre inválido y la colisión se registran en el log
/// estructurado con <c>code</c> DISTINTOS entre sí, de modo que los dos hechos sean
/// diagnosticables y distinguibles (AD-15, AD-25).
/// </para>
/// <para>
/// El caso de uso depende del puerto declarado en esta capa y nunca de un adaptador,
/// así que puede ejercitarse sin una base de datos (AD-1).
/// </para>
/// </summary>
public sealed class RegisterClient
{
    /// <summary>
    /// Category of the log of the registration of the clients.
    /// </summary>
    public const string LoggerCategory = "LANPartyHub.Server.Clients";

    private readonly IClientRegistrationRepository _repository;
    private readonly TimeProvider _clock;
    private readonly ILogger<RegisterClient> _logger;

    /// <summary>Creates the use case of the registration of a client.</summary>
    /// <param name="repository">Port of the registry of the clients.</param>
    /// <param name="clock">Source of the instant of the contact with the server.</param>
    /// <param name="logger">Log of the registration.</param>
    public RegisterClient(
        IClientRegistrationRepository repository,
        TimeProvider clock,
        ILogger<RegisterClient> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Registers the client, and answers with the effective name under which its row
    /// is registered (AD-32).
    ///
    /// <param name="clientId">Canonical identifier of the client.</param>
    /// <param name="proposedMachineName">Name proposed by the client.</param>
    /// <param name="clientVersion">Version of the binary of the client.</param>
    /// <param name="protocolVersion">Protocol version that the client speaks.</param>
    /// <param name="connectionState">Connection state that the client declares.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The outcome of the registration.</returns>
    /// <exception cref="MachineNameInvalidException">
    /// The proposed name does not satisfy the rules of shape of AD-30.
    /// </exception>
    public async Task<ClientRegistrationOutcome> ExecuteAsync(
        string clientId,
        string? proposedMachineName,
        string clientVersion,
        int protocolVersion,
        ConnectionState connectionState,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

        // AD-30: la forma del nombre se comprueba ANTES de tocar la fila, porque un
        // nombre mal formado deja al cliente sin registrar y sin poder operar.
        //
        // La forma se comprueba sobre el texto ORIGINAL: recortar antes la escondería,
        // y un nombre con un espacio inicial o final tiene que ser rechazado (AD-30).
        var proposed = proposedMachineName ?? string.Empty;

        if (!MachineNamePolicy.IsValid(proposed))
        {
            var reason = MachineNamePolicy.DescribeShapeFailure(proposed) ?? string.Empty;

            // AD-15, AD-30: el rechazo y la colisión se registran con `code` DISTINTOS
            // entre sí, para que los dos hechos sean diagnosticables y distinguibles. El
            // código viaja en el CAMPO ESTRUCTURADO de la entrada y no en el texto del
            // mensaje, porque es un identificador y no el texto que el operador lee
            // primero (AD-25).
            using (_logger.BeginScope(new Dictionary<string, object?>
            {
                ["code"] = ErrorCatalog.MachineNameInvalidCode,
                ["clientId"] = clientId,
            }))
            {
                _logger.LogWarning(
                    "Se rechaza el registro del cliente porque el nombre propuesto no cumple las reglas de forma: {Reason}.",
                    reason);
            }

            throw new MachineNameInvalidException(proposed, reason);
        }

        // El nombre que se guarda y que se deriva es el ya recortado: la regla 1 de
        // AD-30 dice que el nombre se examina una vez recortado, y el `stem` de la
        // derivación es precisamente ese texto recortado (AD-31).
        var stem = proposed.Trim();

        var now = _clock.GetUtcNow().ToUniversalTime();

        // AD-30, AD-32: la fila se busca por el identificador TÉCNICO. Si ya existe,
        // es una reconexión: el servidor conserva la fila y el nombre y solo actualiza
        // el estado de conexión, el instante del último contacto y la versión.
        // AD-30: la colisión se busca por FORMA CANÓNICA del nombre propuesto, porque
        // la unicidad de la etiqueta se compara en esa forma y no en el texto guardado.
        var canonical = MachineNamePolicy.Canonicalize(stem);

        // AD-30, AD-32: la fila se busca por el identificador TÉCNICO. Si ya existe,
        // es una reconexión o un renombrado: el servidor conserva la fila y el
        // clientId, y solo actualiza el estado de conexión, el instante del último
        // contacto y la versión.
        var existing = await _repository
            .FindByClientIdAsync(clientId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            // AD-30, AD-32: si el cliente propone el MISMO nombre que ya tiene, es una
            // RECONEXIÓN: el servidor conserva la fila y el nombre y solo actualiza el
            // estado de conexión, el instante del último contacto y la versión. Así un
            // cliente reconectado nunca colisiona consigo mismo.
            //
            // La comparación es por FORMA CANÓNICA, porque el cliente que se reconecta
            // se registra con su nombre efectivo y lo que se compara es la forma
            // canónica del nombre, no su texto (AD-30).
            //
            // Si propone un nombre DISTINTO, es un RENOMBRADO: el servidor aplica la
            // misma regla que en el alta —lo adopta si está libre y lo deriva si
            // colisiona— y el `clientId` NO cambia en ningún caso (AD-30, AD-32).
            var isReconnection = string.Equals(
                MachineNamePolicy.Canonicalize(existing.MachineName),
                canonical,
                StringComparison.Ordinal);

            if (isReconnection)
            {
                var reconnected = new ClientRegistrationRow(
                    existing.ClientId,
                    existing.MachineName,
                    clientVersion,
                    protocolVersion,
                    connectionState,
                    now);

                await _repository.SaveAsync(reconnected, cancellationToken).ConfigureAwait(false);

                _logger.LogInformation(
                    "El cliente {ClientId} vuelve a registrarse y conserva su nombre efectivo {MachineName}.",
                    existing.ClientId,
                    existing.MachineName);

                return new ClientRegistrationOutcome(
                    reconnected.ClientId,
                    reconnected.MachineName,
                    reconnected.ConnectionState,
                    reconnected.LastSeenAt,
                    Renamed: false);
            }

            var renamedName = stem;
            var wasRenamed = false;

            // AD-30: la colisión del nombre nuevo se busca por FORMA CANÓNICA, y solo
            // entre los clientes que NO son este mismo cliente.
            var holderOfNewName = await _repository
                .FindByCanonicalMachineNameAsync(canonical, clientId, cancellationToken)
                .ConfigureAwait(false);

            if (holderOfNewName is not null)
            {
                // AD-31: la colisión NO es un error. El servidor deriva el nombre
                // efectivo y lo persiste, y el `clientId` del cliente NO cambia.
                renamedName = MachineNamePolicy.Derive(stem, clientId);
                wasRenamed = true;

                // AD-15, AD-30: la colisión se registra con SU PROPIO código, distinto
                // del del rechazo, y el código viaja en el campo estructurado de la
                // entrada (AD-25).
                using (_logger.BeginScope(new Dictionary<string, object?>
                {
                    ["code"] = ErrorCatalog.MachineNameCollisionCode,
                    ["clientId"] = clientId,
                    ["holderClientId"] = holderOfNewName.ClientId,
                    ["machineName"] = renamedName,
                }))
                {
                    _logger.LogWarning(
                        "El nombre propuesto del equipo ya lo usa otro cliente, así que el servidor le asigna un nombre único derivado.");
                }
            }

            var renamedRow = new ClientRegistrationRow(
                existing.ClientId,
                renamedName,
                clientVersion,
                protocolVersion,
                connectionState,
                now);

            await _repository.SaveAsync(renamedRow, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "El cliente {ClientId} renombra su equipo: el nombre efectivo pasa a ser {MachineName}.",
                existing.ClientId,
                renamedName);

            return new ClientRegistrationOutcome(
                renamedRow.ClientId,
                renamedRow.MachineName,
                renamedRow.ConnectionState,
                renamedRow.LastSeenAt,
                wasRenamed);
        }

        // AD-30: la colisión del nombre propuesto se busca por FORMA CANÓNICA, y solo
        // entre los clientes que NO son este mismo cliente.
        var holder = await _repository
            .FindByCanonicalMachineNameAsync(canonical, clientId, cancellationToken)
            .ConfigureAwait(false);

        var effectiveName = stem;
        var renamed = false;

        if (holder is not null)
        {
            // AD-31: la colisión NO es un error. El servidor deriva el nombre efectivo
            // y lo persiste, y el `clientId` del cliente NO cambia.
            effectiveName = MachineNamePolicy.Derive(stem, clientId);
            renamed = true;

            // AD-15, AD-30: la colisión se registra con SU PROPIO código, distinto
            // del del rechazo, y el código viaja en el campo estructurado de la
            // entrada (AD-25).
            using (_logger.BeginScope(new Dictionary<string, object?>
            {
                ["code"] = ErrorCatalog.MachineNameCollisionCode,
                ["clientId"] = clientId,
                ["holderClientId"] = holder.ClientId,
                ["machineName"] = effectiveName,
            }))
            {
                _logger.LogWarning(
                    "El nombre propuesto del equipo ya lo usa otro cliente, así que el servidor le asigna un nombre único derivado.");
            }
        }

        var row = new ClientRegistrationRow(
            clientId,
            effectiveName,
            clientVersion,
            protocolVersion,
            connectionState,
            now);

        await _repository.SaveAsync(row, cancellationToken).ConfigureAwait(false);

        return new ClientRegistrationOutcome(
            row.ClientId,
            row.MachineName,
            row.ConnectionState,
            row.LastSeenAt,
            renamed);
    }
}

/// <summary>
/// Outcome of the registration of a client (AD-32).
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
