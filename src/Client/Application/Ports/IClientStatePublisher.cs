using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Client.Application.Ports;

/// <summary>
/// Publication of the state of the connection of the client (AD-18, AD-22, AD-29).
///
/// <para>
/// La publicación va en el hub del servidor, que es el canal sancionado por AD-18 y
/// AD-29 para el estado que declara el cliente. El servidor la PERSISTE, y así la
/// tabla de clientes del organizador refleja la conexión y la desconexión de cada
/// equipo (AD-29, FR-10).
/// </para>
/// </summary>
public interface IClientStatePublisher
{
    /// <summary>
    /// Publishes the state of the connection of the client.
    /// </summary>
    /// <param name="state">State that the client declares.</param>
    /// <param name="cancellationToken">Token that cancels the publication.</param>
    /// <returns>A task that completes when the publication has been attempted.</returns>
    Task PublishAsync(ClientStateChanged state, CancellationToken cancellationToken);
}

/// <summary>
/// Publication of the ERRORS that the client registered (AD-15, AD-18, AD-22, FR-10).
///
/// <para>
/// La publicación va en el MISMO hub del servidor, que es el canal sancionado por AD-18,
/// pero el puerto vive aparte del del ESTADO porque sus consumidores son distintos: el
/// estado se publica periódicamente y el error se publica UNA VEZ por fallo (AD-18).
/// </para>
/// <para>
/// El payload es el CONTRATO EXACTO <see cref="ClientErrorReported"/>: el identificador
/// del cliente, el de la operación y el error con sus cuatro partes. El cliente NO acuña
/// el identificador del error: el servidor lo deriva del reporte, así que republicar el
/// mismo error no duplica el diagnóstico (AD-18, AD-22).
/// </para>
/// <para>
/// La publicación es un COMPLEMENTO: un fallo del hub no puede impedir que el error
/// quede persistido en la base de datos del cliente (AD-1, AD-11).
/// </para>
/// </summary>
public interface IClientErrorPublisher
{
    /// <summary>
    /// Publishes the ERROR that the client registered (AD-18, AD-22).
    /// </summary>
    /// <param name="reported">Error that the client declares.</param>
    /// <param name="cancellationToken">Token that cancels the publication.</param>
    /// <returns>A task that completes when the publication has been attempted.</returns>
    Task PublishErrorAsync(ClientErrorReported reported, CancellationToken cancellationToken);
}
