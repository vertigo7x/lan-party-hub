using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Server.Presentation.Clients;

/// <summary>
/// View model of the section of the clients of the event (AD-29, AC1).
///
/// <para>
/// The view model holds the clients that the server has registered and it projects
/// them onto the rows that the dense table shows. It holds no rule of the domain: the
/// validation of the name of the team belongs to the domain (AD-1).
/// </para>
/// <para>
/// El ORDEN de las filas es estable por nombre de equipo, de modo que dos lecturas
/// seguidas ordenan las filas igual y el organizador no ve las filas saltar de sitio
/// entre un refresco y el siguiente (AD-29, UX-DR-14).
/// </para>
/// <para>
/// La fila muestra la proyección REAL que el servidor tiene: los juegos instalados con
/// su versión y su estado, las descargas activas con sus bytes reales, la fase real de
/// la instalación en curso y los errores registrados (AD-22, FR-10).
/// </para>
/// </summary>
public sealed class ClientsViewModel
{
    private readonly List<ClientRegistrationView> _clients = [];

    /// <summary>Creates the view model of the section of the clients.</summary>
    public ClientsViewModel()
    {
    }

    /// <summary>
    /// Creates the view model of the section of the clients with the clients that the
    /// server has registered.
    /// </summary>
    /// <param name="clients">Clients registered in the event.</param>
    public ClientsViewModel(IEnumerable<ClientRegistrationView> clients)
    {
        ArgumentNullException.ThrowIfNull(clients);

        _clients.AddRange(clients);
    }

    /// <summary>
    /// The clients of the event, as the list shows them.
    /// </summary>
    public IReadOnlyList<ClientRegistrationView> Clients => _clients;

    /// <summary>
    /// The clients of the event as the rows of the dense table show them (AC1).
    ///
    /// <para>
    /// La fila lleva el texto del estado de conexión COPIADO de la tabla de microcopia
    /// de <c>Shared</c>, nunca uno inventado por la vista (AD-25).
    /// </para>
    /// </summary>
    public IReadOnlyList<ClientRow> Rows =>
    [
        .. _clients
            .OrderBy(client => client.MachineName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(client => client.ClientId, StringComparer.Ordinal)
            .Select(client => new ClientRow(
                client.ClientId,
                client.MachineName,
                StateDisplayMap.GetText(client.ConnectionState),
                client.ClientVersion,
                client.ProtocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
                client.LastSeenAt.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                InstalledGamesText(client),
                ActiveDownloadsText(client),
                InstallationProgressText(client),
                RegisteredErrorsText(client),
                RecentOutcomesText(client),
                RegisteredErrorDetailsText(client)))
    ];

    /// <summary>
    /// The number of clients that the server has registered.
    /// </summary>
    public int Count => _clients.Count;

    /// <summary>
    /// Replaces the clients of the event with the ones that the section shows.
    /// </summary>
    /// <param name="clients">Clients registered in the event.</param>
    public void Replace(IEnumerable<ClientRegistrationView> clients)
    {
        ArgumentNullException.ThrowIfNull(clients);

        _clients.Clear();
        _clients.AddRange(clients);
    }

    /// <summary>
    /// Composes the text of the column of the games installed: the game with the
    /// version that is installed and the TEXT of the state, que la vista NO inventa
    /// porque ya viene copiada del <c>StateDisplayMap</c> (AD-22, AD-25).
    ///
    /// <para>
    /// La ruta de la instalación NUNCA se muestra: la columna dice QUÉ tiene instalado
    /// el equipo, no dónde lo tiene (AD-22).
    /// </para>
    /// </summary>
    /// <param name="client">Client whose row is composed.</param>
    /// <returns>The text of the column.</returns>
    private static string InstalledGamesText(ClientRegistrationView client)
    {
        if (client.InstalledGames.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(
            "; ",
            client.InstalledGames.Select(game =>
                $"{game.GameId} · {game.InstalledVersion} · {game.ValidationStateText}"));
    }

    /// <summary>
    /// Composes the text of the column of the active downloads: the REAL bytes that the
    /// client transferred, y NUNCA un porcentaje inventado (AD-22).
    /// </summary>
    /// <remarks>
    /// El TOTAL declarado por el cliente es REAL aunque el cliente todavía no haya
    /// declarado los bytes transferidos: se muestra solo y NO se acompañe de un
    /// porcentaje inventado (AD-22).
    /// </remarks>
    /// <param name="client">Client whose row is composed.</param>
    /// <returns>The text of the column.</returns>
    private static string ActiveDownloadsText(ClientRegistrationView client)
    {
        var downloads = client.ActiveOperations
            .Where(operation => IsDownload(operation))
            .ToArray();

        if (downloads.Length == 0)
        {
            return string.Empty;
        }

        return string.Join(
            "; ",
            downloads.Select(operation =>
            {
                var bytes = operation.BytesTransferred is { } transferred
                    ? $"{transferred} B"
                    : string.Empty;

                var total = operation.TotalBytes is { } declared
                    ? $"/{declared} B"
                    : string.Empty;

                // El total declarado es real: se muestra aunque falten los bytes
                // transferidos, y el resultado NUNCA es un porcentaje inventado
                // (AD-22).
                var progress = bytes.Length == 0 ? total : $"{bytes}{total}";

                return $"{operation.OperationId} · {operation.StateText}{progress}";
            }));
    }

    /// <summary>
    /// Composes the text of the column of the progress of the installations: the REAL
    /// PHASE of the operation, sin ningún porcentaje ficticio (AD-22).
    /// </summary>
    /// <param name="client">Client whose row is composed.</param>
    /// <returns>The text of the column.</returns>
    private static string InstallationProgressText(ClientRegistrationView client)
    {
        var installations = client.ActiveOperations
            .Where(operation => IsInstallation(operation))
            .ToArray();

        if (installations.Length == 0)
        {
            return string.Empty;
        }

        return string.Join(
            "; ",
            installations.Select(operation =>
            {
                var phase = string.IsNullOrWhiteSpace(operation.Phase)
                    ? operation.StateText
                    : $"{operation.StateText} · {operation.Phase}";

                return $"{operation.OperationId} · {phase}";
            }));
    }

    /// <summary>
    /// Composes the text of the column of the registered errors: el código estable y el
    /// mensaje, que la vista NO inventa (AD-15, AD-25).
    /// </summary>
    /// <param name="client">Client whose row is composed.</param>
    /// <returns>The text of the column.</returns>
    private static string RegisteredErrorsText(ClientRegistrationView client)
    {
        if (client.Errors.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(
            "; ",
            client.Errors.Select(error => $"{error.Code} · {error.Message}"));
    }

    /// <summary>
    /// Composes the text of the TERMINAL RESULTS that the client reported: el
    /// identificador de la operación y el TEXTO del resultado, que es el que copió la
    /// tabla única de microcopia de <c>Shared</c> (AD-10, AD-25).
    ///
    /// <para>
    /// El resultado «Paquete retirado del catálogo» es un RESULTADO y no un fallo: la
    /// vista lo muestra con el texto de la tabla y NUNCA lo presenta como un error
    /// (AD-33, R-MVP-8).
    /// </para>
    /// </summary>
    /// <param name="client">Client whose row is composed.</param>
    /// <returns>The text of the terminal results.</returns>
    private static string RecentOutcomesText(ClientRegistrationView client)
    {
        if (client.RecentOutcomes.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(
            "; ",
            client.RecentOutcomes.Select(outcome => $"{outcome.OperationId} · {outcome.OutcomeText}"));
    }

    /// <summary>
    /// Composes the text of the DETALLE de los errores registrados: el código estable,
    /// el mensaje, la CAUSA y el SIGUIENTE PASO, que son las tres partes de la única
    /// forma de error del sistema (AD-15, AD-22).
    ///
    /// <para>
    /// La columna densa se queda con el código y el mensaje; el detalle de la fila
    /// lleva la causa y el siguiente paso, que es lo que el organizador necesita para
    /// saber si reintenta o si necesita asistencia (AD-15, UX-DR-15).
    /// </para>
    /// </summary>
    /// <param name="client">Client whose row is composed.</param>
    /// <returns>The text of the detail of the registered errors.</returns>
    private static string RegisteredErrorDetailsText(ClientRegistrationView client)
    {
        if (client.Errors.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(
            "; ",
            client.Errors.Select(error =>
                $"{error.Code} · {error.Message} · causa: {error.Cause} · siguiente paso: {error.NextStep}"));
    }

    /// <summary>
    /// Tells whether an operation is a DOWNLOAD, por el nombre del tipo de operación
    /// (AD-22).
    /// </summary>
    /// <remarks>
    /// El tipo puede llegar VACÍO o nulo desde un cliente que no lo declara, así que la
    /// comparación NO se hace sobre un valor nulo: una operación sin tipo no es ni una
    /// descarga ni una instalación, y el refresco de la tabla NO se rompe por ello
    /// (AD-22).
    /// </remarks>
    /// <param name="operation">Operation that the client reported.</param>
    /// <returns><c>true</c> when the operation is a download.</returns>
    private static bool IsDownload(ClientRegistrationView.ActiveOperation operation) =>
        string.Equals(TypeOf(operation), OperationTypeNames.Download, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Tells whether an operation is an INSTALLATION, por el nombre del tipo de
    /// operación (AD-22).
    /// </summary>
    /// <remarks>
    /// El tipo puede llegar VACÍO o nulo desde un cliente que no lo declara, así que la
    /// comparación NO se hace sobre un valor nulo: una operación sin tipo no es ni una
    /// descarga ni una instalación, y el refresco de la tabla NO se rompe por ello
    /// (AD-22).
    /// </remarks>
    /// <param name="operation">Operation that the client reported.</param>
    /// <returns><c>true</c> when the operation is an installation.</returns>
    private static bool IsInstallation(ClientRegistrationView.ActiveOperation operation) =>
        string.Equals(TypeOf(operation), OperationTypeNames.Installation, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The name of the kind of the operation, VACÍO when the client did not declare one
    /// (AD-22).
    /// </summary>
    /// <param name="operation">Operation that the client reported.</param>
    /// <returns>The name of the kind of the operation, never <c>null</c>.</returns>
    private static string TypeOf(ClientRegistrationView.ActiveOperation operation) =>
        operation.OperationType ?? string.Empty;
}

/// <summary>
/// A client of the event as the dense table of the section shows it (AC1, UX-DR-14).
///
/// <para>
/// La fila lleva los valores que la tabla muestra: el identificador técnico, el
/// NOMBRE EFECTIVO del equipo —nunca el propuesto que colisionó—, el texto del estado
/// de conexión, la versión del cliente y la versión del protocolo (AD-30).
/// </para>
/// <para>
/// Las cuatro columnas de la proyección llevan el dato REAL que el servidor tiene: los
/// juegos instalados con su versión, las descargas activas con sus bytes, la fase real
/// de la instalación en curso y los errores registrados (AD-22, FR-10).
/// </para>
/// <para>
/// El DETALLE de la fila lleva lo que la columna densa no cabe: los resultados
/// TERMINALES que el cliente reportó —con el texto de la tabla única de microcopia,
/// incluido «Paquete retirado del catálogo», que es un resultado y no un fallo
/// (AD-25, AD-33)— y el detalle de cada error registrado con su CAUSA y su SIGUIENTE
/// PASO (AD-15, UX-DR-15).
/// </para>
/// </summary>
/// <param name="ClientId">Opaque UUID v7 identifier of the client.</param>
/// <param name="MachineName">Effective name of the team.</param>
/// <param name="ConnectionStateText">Text of the state of the connection.</param>
/// <param name="ClientVersion">Version of the binary of the client.</param>
/// <param name="ProtocolVersion">Protocol version that the client speaks.</param>
/// <param name="LastSeenAt">Instant of the last contact, in UTC ISO-8601.</param>
/// <param name="InstalledGamesText">Games that the client has installed, with their version and their state.</param>
/// <param name="ActiveDownloadsText">Downloads that the client is running, with the real bytes.</param>
/// <param name="InstallationProgressText">Real phase of the installation that the client is running.</param>
/// <param name="RegisteredErrorsText">Errors that are already registered for the client, with their code and their message.</param>
/// <param name="RecentOutcomesText">
/// Terminal results that the client reported, with the text of the table of microcopy.
/// It is EMPTY when the client has not reported any (AD-25, AD-33).
/// </param>
/// <param name="RegisteredErrorDetailsText">
/// Detail of the registered errors, with their cause and their next step. It is EMPTY
/// when the client has no registered error (AD-15, AD-22).
/// </param>
public sealed record ClientRow(
    string ClientId,
    string MachineName,
    string ConnectionStateText,
    string ClientVersion,
    string ProtocolVersion,
    string LastSeenAt,
    string InstalledGamesText,
    string ActiveDownloadsText,
    string InstallationProgressText,
    string RegisteredErrorsText,
    string RecentOutcomesText,
    string RegisteredErrorDetailsText);
