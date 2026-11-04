using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Client.Application.Ports;

/// <summary>
/// Port of the publication of the progress of a download operation (AD-22, AR-3, AC5).
///
/// <para>
/// El port es declarado en la capa de Application y lo implementa el adaptador que
/// publica en el hub del estado del cliente. El coordinador depende del port y nunca
/// del adaptador, así que la forma del evento se puede ejercitar sin una red (AD-1).
/// </para>
/// <para>
/// El payload es el CONTRATO EXACTO <see cref="DownloadProgressUpdated"/>: el identificador
/// del cliente, el de la operación, los bytes transferidos, el total y el estado de la
/// máquina. El port NO amplía el contrato ni inventa campos: el contrato vive en
/// <c>Shared</c> y no crece (AD-18, AR-3).
/// </para>
/// <para>
/// La publicación NUNCA es una condición para que la descarga siga: un servidor que no
/// recibe el progreso no puede impedir que los bytes se descarguen y se persistan
/// (AD-11, AD-22).
/// </para>
/// </summary>
public interface IDownloadProgressReporter
{
    /// <summary>
    /// Publishes the progress of a download operation (AC5, AD-22).
    ///
    /// <param name="progress">Progress of the operation, with the exact shape of the contract.</param>
    /// <param name="cancellationToken">Token that cancels the publication.</param>
    /// <returns>A task that completes when the progress has been published.</returns>
    Task ReportAsync(DownloadProgressUpdated progress, CancellationToken cancellationToken);
}
