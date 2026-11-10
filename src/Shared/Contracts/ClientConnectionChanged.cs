namespace LANPartyHub.Shared.Contracts;

/// <summary>
/// Connection state pushed by the client to the server ALONE (AD-18, AD-29, AD-32).
///
/// <para>
/// La forma EXACTA es el identificador del cliente y su estado de conexión, y NADA más:
/// el evento NO lleva juegos instalados ni operaciones en curso (AD-18, AD-22).
/// </para>
/// <para>
/// El evento existe para el caso en el que el cliente NO puede sostener una
/// INSTANTÁNEA COMPLETA de su estado —porque su almacén local no se abre— y sí puede
/// declarar, en cambio, si está conectado o desconectado. Publicar una instantánea
/// vacía en ese caso BORRARÍA por error la proyección que el servidor ya tiene
/// (AD-22), así que el cliente declara SOLO la conexión y el servidor SOLO actualiza
/// el <c>connectionState</c> de la fila (AD-29, AD-32).
/// </para>
/// <para>
/// El cliente que publica es SIEMPRE el de la fila: el servidor no acepta una
/// publicación que nombre a otro cliente, porque el estado de un equipo es el suyo y
/// no el de otro (AD-29). Un cliente que NO está registrado ignora la publicación: el
/// alta del equipo es la operación de registro (AD-30, AD-32).
/// </para>
/// </summary>
/// <param name="ClientId">Opaque UUID v7 identifier emitted once by the client (AD-23).</param>
/// <param name="ConnectionState">Connection state of the client.</param>
public sealed record ClientConnectionChanged(
    string ClientId,
    ConnectionState ConnectionState);
