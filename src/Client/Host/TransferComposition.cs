using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Infrastructure;
using LANPartyHub.Client.Infrastructure.Networking;

namespace LANPartyHub.Client.Host;

/// <summary>
/// Composition of the transfer of bytes of the client (AD-4, AD-20, AD-24, AD-28).
///
/// <para>
/// The composition delivers the port <see cref="ITransferEngine"/> with EXACTLY ONE
/// implementation, <c>HttpRangeTransferEngine</c>, and it does NOT expose any selector: the
/// MVP has a single path of download and there is nothing to select (AD-4, AD-28).
/// </para>
/// <para>
/// The composition is the ONLY place that knows the adapter: the use cases depend on the
/// port and never on it (AD-1).
/// </para>
/// </summary>
public sealed class TransferComposition : IDisposable
{
    private readonly HttpClient _httpClient;

    /// <summary>Creates the composition of the transfer of bytes.</summary>
    /// <param name="headers">
    /// Source of the identifiers of correlation that the requests carry. It carries the
    /// canonical identifier of the team, which the server REQUIRES in every request of the
    /// surface of the client (AD-15, AD-23).
    /// </param>
    public TransferComposition(IClientRequestHeaders headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        // AD-6: la versión del cliente es la del binario que se está ejecutando y la
        // versión del protocolo es la constante del contrato compartido.
        var versions = new ClientBuildVersion();

        // El motor pide rangos al servidor del evento con el encabezado `Range`, así que
        // comparte el cliente HTTP que estampa la versión del cliente y la del protocolo
        // en TODA petición (AD-6, AD-20).
        //
        // AD-15, AD-23: la ruta del contenido es de la superficie del cliente y el
        // servidor rechaza con 400 la petición que no lleva el identificador del equipo.
        //
        // El manejador de cabeceras es un DelegatingHandler y necesita un manejador
        // interior que sea el que abre la conexión: sin él, TODA petición del motor falla
        // antes de salir (AD-15, AD-23).
        _httpClient = new HttpClient(
            new ClientRequestHeadersHandler(
                headers,
                versions)
            {
                InnerHandler = new HttpClientHandler(),
            });

        Engine = new HttpRangeTransferEngine(_httpClient);
    }

    /// <summary>
    /// The port of the transfer of bytes, delivered with the only implementation of the
    /// MVP.
    /// </summary>
    public ITransferEngine Engine { get; }

    /// <inheritdoc />
    public void Dispose() => _httpClient.Dispose();
}
