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
    public void The_name_of_the_connection_only_method_is_the_same_on_the_client_and_the_server()
    {
        // AD-18, AD-29: el evento de SOLO conexión usa el mismo nombre exacto en los
        // dos lados; de otro modo la desconexión se pierde en silencio.
        Assert.Equal(
            ClientStateHubPublisher.ClientConnectionChangedMethodName,
            CatalogHub.ClientConnectionChangedMethodName);
    }

    [Fact]
    public void The_method_of_the_connection_only_event_receives_only_its_exact_payload()
    {
        var method = typeof(CatalogHub)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Single(candidate => candidate.Name == ClientStateHubPublisher.ClientConnectionChangedMethodName);

        Assert.Equal(
            [typeof(ClientConnectionChanged)],
            method.GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public void The_use_cases_that_persist_the_state_come_from_the_container()
    {
        // AD-1, AD-22: los casos de uso se INYECTAN en el hub, así que el hub no depende
        // de los adaptadores que persisten el estado y el estado no se pierde.
        //
        // AD-18, AD-22, AD-33: el hub recibe el estado del equipo, el progreso de sus
        // operaciones, el resultado terminal de sus operaciones y el error que reporta,
        // así que los CINCO casos de uso viajan por el constructor.
        var constructors = typeof(CatalogHub).GetConstructors();

        var constructor = Assert.Single(constructors);

        var parameterTypes = constructor.GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.Equal(
            [
                typeof(ReportClientState),
                typeof(ReportClientProgress),
                typeof(ReportOperationOutcome),
                typeof(ReportClientConnection),
                typeof(ReportClientError),
            ],
            parameterTypes);
    }

    [Fact]
    public void The_name_of_the_error_method_is_the_same_on_the_client_and_the_server()
    {
        // AD-15, AD-18: el error que reporta el cliente usa el mismo nombre exacto en los
        // dos lados; de otro modo el diagnóstico se pierde en silencio.
        Assert.Equal(
            CatalogHub.ClientErrorReportedMethodName,
            nameof(ClientErrorReported));
    }

    [Fact]
    public void The_method_of_the_error_receives_only_its_exact_payload()
    {
        var method = typeof(CatalogHub)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Single(candidate => candidate.Name == CatalogHub.ClientErrorReportedMethodName);

        Assert.Equal(
            [typeof(ClientErrorReported)],
            method.GetParameters().Select(parameter => parameter.ParameterType));
    }
}
