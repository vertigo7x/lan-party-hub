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
                client.LastSeenAt.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture)))
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
/// Las columnas de juegos instalados, descargas activas, progreso de las instalaciones
/// y errores registrados las rellenan las historias 9.2 y 9.3: esta historia reserva
/// su encabezado, pero no inventa un dato que el servidor todavía no tiene (FR-10).
/// </para>
/// </summary>
/// <param name="ClientId">Opaque UUID v7 identifier of the client.</param>
/// <param name="MachineName">Effective name of the team.</param>
/// <param name="ConnectionStateText">Text of the state of the connection.</param>
/// <param name="ClientVersion">Version of the binary of the client.</param>
/// <param name="ProtocolVersion">Protocol version that the client speaks.</param>
/// <param name="LastSeenAt">Instant of the last contact, in UTC ISO-8601.</param>
public sealed record ClientRow(
    string ClientId,
    string MachineName,
    string ConnectionStateText,
    string ClientVersion,
    string ProtocolVersion,
    string LastSeenAt);
