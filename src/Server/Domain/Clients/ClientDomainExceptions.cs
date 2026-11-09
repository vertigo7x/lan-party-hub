namespace LANPartyHub.Server.Domain.Clients;

/// <summary>
/// Base type of the exceptions of the domain of the clients (AD-1, AD-15).
///
/// <para>
/// The exceptions of the domain are typed and they live in this layer. The adapter that
/// serves the request translates them into the single error shape of the system,
/// <c>ApiError</c>, and never the other way round (AD-15, AD-25).
/// </para>
/// </summary>
public abstract class ClientDomainException : Exception
{
    /// <summary>Creates the exception of the domain of the clients.</summary>
    /// <param name="message">Message that names the concrete rule that fails.</param>
    protected ClientDomainException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// The proposed name of the team does NOT satisfy the rules of shape of AD-30.
/// </summary>
/// <para>
/// El mensaje nombra la regla concreta que falla, para que el organizador sepa qué
/// corregir sin tener que adivinar (AD-25). El código estable del catálogo que
/// responde a esta excepción también la nombra, porque la forma de <c>ApiError</c> no
/// tiene hueco para ella (AD-15, AD-25).
/// </para>
/// </summary>
public sealed class MachineNameInvalidException : ClientDomainException
{
    /// <summary>Creates the exception of a proposed name that does not satisfy the rules of shape.</summary>
    /// <param name="machineName">Proposed name of the team.</param>
    /// <param name="reason">Concrete rule of shape that the name does not satisfy.</param>
    public MachineNameInvalidException(string machineName, string reason)
        : base($"El nombre del equipo '{machineName}' no es válido: {reason}")
    {
        MachineName = machineName;
        Reason = reason;
    }

    /// <summary>Proposed name of the team that was rejected.</summary>
    public string MachineName { get; }

    /// <summary>Concrete rule of shape that the proposed name does not satisfy.</summary>
    public string Reason { get; }
}
