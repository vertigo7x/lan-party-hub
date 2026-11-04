namespace LANPartyHub.Server.Host;

/// <summary>
/// Body of the registration of the intention of a download (AD-5, AD-10).
///
/// <para>
/// El cuerpo es un tipo DEL SERVIDOR y no un contrato de <c>Shared</c>: <c>Shared</c>
/// declara exactamente veintidós contratos y ninguno es el de la intención de una
/// descarga (AD-18). El cuerpo lleva la versión del esquema que el servidor soporta, como
/// toda mutación de la API (AD-5).
/// </para>
/// </summary>
public sealed class DownloadIntentRequest
{
    /// <summary>
    /// Version of the schema of the body that the client speaks (AD-5).
    /// </summary>
    public int? SchemaVersion { get; init; }

    /// <summary>
    /// Opaque identifier of the package that the client wants to download (AD-7).
    /// </summary>
    public string? PackageId { get; init; }
}

/// <summary>
/// Answer of the registration of the intention of a download (AD-5, AD-15).
///
/// <para>
/// La respuesta lleva el identificador de la OPERACIÓN, que es la unidad de trabajo del
/// cliente: es lo que correlaciona el log de los dos lados y lo que nombra la fila del
/// cliente (AD-15, AD-24).
/// </para>
/// </summary>
/// <param name="SchemaVersion">Version of the schema of the body that the server speaks (AD-5).</param>
/// <param name="OperationId">Opaque identifier of the registered operation.</param>
/// <param name="PackageId">Opaque identifier of the package of the intention.</param>
public sealed record DownloadIntentResponse(int SchemaVersion, string OperationId, string PackageId);
