using LANPartyHub.Client.Application.Ports;

namespace LANPartyHub.Client.Infrastructure.Filesystem;

/// <summary>
/// Adapter of the confirmation of a privileged operation that has NO interface behind it
/// (AD-7, AC2).
///
/// <para>
/// Sin interfaz NO hay confirmación, y la operación privilegiada NO se ejecuta: la NO
/// ejecución es la que protege al jugador, así que el valor por defecto del puerto es
/// DECLINAR y no preguntar (AC2).
/// </para>
/// <para>
/// El cliente de verdad entrega el puerto con el diálogo de confirmación, así que este
/// adaptador sólo cubre el caso en que la composición se abre sin interfaz (AD-1).
/// </para>
/// </summary>
public sealed class DecliningPrivilegedOperationConfirmation : IPrivilegedOperationConfirmation
{
    /// <inheritdoc />
    public Task<bool> ConfirmAsync(
        string operation,
        string detail,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);

        // La operación privilegiada NO se ejecuta sin una confirmación explícita, y la
        // confirmación NO se hereda de ninguna sesión anterior (AC2, AC3).
        return Task.FromResult(false);
    }
}
