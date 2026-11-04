namespace LANPartyHub.Client.Application.Downloads;

/// <summary>
/// Stream that RECIBE el total del contenido cuando el motor lo declara en la
/// respuesta, para que el cliente pueda mostrar el porcentaje y el par de tamaños
/// mientras la descarga sigue en curso (AD-20, UX-DR-5).
///
/// <para>
/// El motor lee el total del <c>Content-Range</c> de la respuesta, que llega ANTES del
/// cuerpo, así que el total se conoce desde el principio del tramo (AD-20).
/// </para>
/// <para>
/// El total NO es un dato del motor que la capa de Application conoce: lo declara el
/// puerto de la transferencia a través de este contrato, que es lo que permite al
/// coordinador persistirlo sin conocer el transporte (AD-1, AD-20).
/// </para>
/// </summary>
public interface IContentTotalAware
{
    /// <summary>
    /// Declares the total size of the content, when the engine knows it (AD-20).
    /// </summary>
    /// <param name="totalBytes">Total size of the content in exact bytes.</param>
    void DeclareTotal(long totalBytes);
}
