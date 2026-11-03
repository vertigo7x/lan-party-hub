using LANPartyHub.Client.Application.Ports;
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
    public TransferComposition()
    {
        // El motor pide rangos al servidor del evento con el encabezado `Range`, así que
        // comparte el cliente HTTP que estampa la versión del cliente y la del protocolo
        // en TODA petición (AD-6, AD-20).
        _httpClient = new HttpClient(new HttpClientHandler());

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
