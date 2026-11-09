namespace LANPartyHub.Client.Application.Ports;

/// <summary>
/// Access to the EFFECTIVE name of the team, the one that the server assigned and
/// under which the row of the client is registered (AD-30, AD-31).
///
/// <para>
/// El puerto es ESTRECHO y vive aparte del acceso general a la base de datos del
/// cliente, porque el nombre efectivo solo lo necesita la historia del registro y
/// ningún otro caso de uso lo toca (AD-1).
/// </para>
/// <para>
/// El nombre efectivo se PERSISTE, porque es el único con el que la fila del cliente
/// queda registrada: proponer en la reconexión siguiente el nombre que colisionó
/// haría que el servidor asignara un nombre derivado distinto en cada reconexión
/// (AD-30, AD-31).
/// </para>
/// </summary>
public interface IClientEffectiveNameStore
{
    /// <summary>
    /// Reads the effective name of the team, or <c>null</c> when the client has not
    /// registered yet (AD-30).
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The effective name of the team, or <c>null</c>.</returns>
    Task<string?> LoadEffectiveMachineNameAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Persists the effective name of the team (AD-30, AD-31).
    /// </summary>
    /// <param name="machineName">Effective name of the team.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the name is persisted.</returns>
    Task SaveEffectiveMachineNameAsync(string machineName, CancellationToken cancellationToken);
}
