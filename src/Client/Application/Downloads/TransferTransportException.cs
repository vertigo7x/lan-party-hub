namespace LANPartyHub.Client.Application.Downloads;

/// <summary>
/// Failure of the TRANSPORT of the bytes of a download operation (AD-11, AD-24, SM-2).
///
/// <para>
/// Un fallo de transporte NO es una falla de la operación: el servidor no estaba, la red
/// se cayó o la respuesta tardó. La operación NO se pierde: sus bytes ya descargados
/// siguen en el parcial y su offset persistido sigue siendo válido, así que el siguiente
/// arranque reintenta el <c>Range</c> desde ese offset (AD-20, SM-2).
/// </para>
/// <para>
/// La excepción la declara la capa de APLICACIÓN porque la política de reintento es suya:
/// el adaptador de la red la lanza cuando su transporte falla, y el coordinador decide qué
/// significa para la operación. La capa de aplicación NUNCA menciona el tipo de fallo del
/// adaptador (AD-1, AD-15).
/// </para>
/// </summary>
public sealed class TransferTransportException : Exception
{
    /// <summary>Creates the failure of the transport.</summary>
    /// <param name="message">Description of what the transport could not do.</param>
    public TransferTransportException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the failure of the transport, keeping the original cause.</summary>
    /// <param name="message">Description of what the transport could not do.</param>
    /// <param name="innerException">Original failure of the transport.</param>
    public TransferTransportException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
