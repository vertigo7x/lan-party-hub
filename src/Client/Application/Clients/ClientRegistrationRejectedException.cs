using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Client.Application.Clients;

/// <summary>
/// The server REJECTED the registration of the client because its proposed name does
/// not satisfy the rules of shape (AD-30, AD-32).
///
/// <para>
/// El rechazo significa que el cliente queda SIN REGISTRAR y no puede operar, y que
/// el error lleva las tres partes de la microcopia: el mensaje, la causa y el
/// siguiente paso (AD-15, AD-25). El cliente NO reintenta con el mismo nombre: el
/// siguiente paso lo dice el propio error (AD-30).
/// </para>
/// </summary>
public sealed class ClientRegistrationRejectedException : Exception
{
    /// <summary>Creates the exception of a registration that the server rejected.</summary>
    /// <param name="error">Error of the catalog that explains the rejection.</param>
    public ClientRegistrationRejectedException(ApiError error)
        : base(error.Message)
    {
        Error = error;
    }

    /// <summary>Error of the catalog that explains the rejection.</summary>
    public ApiError Error { get; }
}
