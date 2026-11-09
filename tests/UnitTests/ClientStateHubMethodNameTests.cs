using System.Reflection;
using LANPartyHub.Client.Infrastructure.Networking;
using LANPartyHub.Server.Application.Clients;
using LANPartyHub.Server.Host;
using LANPartyHub.Shared.Contracts;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The name of the method of the hub that receives the state of the client is the SAME
/// one that the client invokes (AD-18, AD-22, AD-29).
///
/// <para>
/// SignalR registra cada método del hub con su nombre de .NET y NO recorta el sufijo
/// <c>Async</c>: un método declarado como <c>ReportClientStateAsync</c> se invoca como
/// <c>ReportClientStateAsync</c>, y un cliente que invoca otro nombre recibe «Method
/// does not exist». Por eso el nombre está FIJADO en los dos lados y esta prueba lo
/// ata: sin ella, la desconexión se pierde en silencio porque el publicador se come el
/// error (AD-16).
/// </para>
/// </summary>
public sealed class ClientStateHubMethodNameTests
{
    [Fact]
    public void The_name_that_the_client_invokes_is_the_name_of_the_method_of_the_hub()
    {
        // AD-18, AD-29: el cliente y el servidor hablan del método por el MISMO nombre.
        Assert.Equal(
            ClientStateHubPublisher.ClientStateChangedMethodName,
            CatalogHub.ClientStateChangedMethodName);
    }

    [Fact]
    public void The_hub_declares_a_method_with_exactly_the_name_that_the_client_invokes()
    {
        // SignalR resuelve el método por su nombre de .NET, así que el método declarado
        // tiene que llamarse EXACTAMENTE como el que el cliente invoca.
        var declared = typeof(CatalogHub)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => method.Name)
            .ToArray();

        Assert.Contains(
            ClientStateHubPublisher.ClientStateChangedMethodName,
            declared);
    }

    [Fact]
    public void The_method_of_the_hub_receives_only_the_payload_of_the_client()
    {
        // AD-18: el payload del cliente es el CONTRATO EXACTO del estado, y es el ÚNICO
        // argumento que el cliente envía. El caso de uso NO viaja en el payload: lo
        // inyecta el contenedor, así que un segundo parárafo rompe el enlace.
        var method = typeof(CatalogHub)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Single(candidate => candidate.Name == ClientStateHubPublisher.ClientStateChangedMethodName);

        var parameters = method.GetParameters();

        Assert.Equal([typeof(ClientStateChanged)], parameters.Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public void The_use_case_that_persists_the_state_comes_from_the_container()
    {
        // AD-1, AD-22: el caso de uso se INYECTA en el hub, así que el hub no depende
        // del adaptador que persiste el estado y el estado no se pierde.
        var constructors = typeof(CatalogHub).GetConstructors();

        var constructor = Assert.Single(constructors);

        Assert.Equal(
            typeof(ReportClientState),
            constructor.GetParameters().Single().ParameterType);
    }
}
