namespace LANPartyHub.Client.Application.Ports;

/// <summary>
/// Aviso de que el ESTADO del cliente cambió y hay que publicarlo (AD-18, AD-22,
/// FR-10).
///
/// <para>
/// La lista de operaciones en curso del cliente cambia cuando una descarga EMPIEZA o
/// cuando TERMINA, así que el servidor tiene que recibir el estado COMPLETO en esos dos
/// momentos: si no, la tabla del organizador sigue mostrando una descarga que ya
/// terminó o no muestra la que acaba de empezar (AD-22).
/// </para>
/// <para>
/// El port es declarado en la capa de Application y lo implementa la composición del
/// REGISTRO, que es la que publica el estado. El coordinador de las descargas depende
/// del port y nunca de la composición, así que su secuencia se puede ejercitar sin una
/// red (AD-1).
/// </para>
/// <para>
/// El aviso NUNCA es una condición para que la descarga siga: un servidor que no recibe
/// el estado no puede impedir que los bytes se descarguen y se persistan (AD-11,
/// AD-22).
/// </para>
/// </summary>
public interface IClientStateChangeNotifier
{
    /// <summary>
    /// Avisa de que el estado del cliente cambió, así que hay que publicarlo (AD-22).
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the publication.</param>
    /// <returns>A task that completes when the publication has been attempted.</returns>
    Task NotifyAsync(CancellationToken cancellationToken);
}
