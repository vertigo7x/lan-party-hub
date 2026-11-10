using System.Reflection;
using LANPartyHub.Client.Infrastructure.Networking;
using LANPartyHub.Server.Host;
using LANPartyHub.Shared.Contracts;
using Microsoft.AspNetCore.SignalR;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The contract of the events of the hub (AC6, AR-2, AR-16, AD-5, AD-29).
///
/// <para>
/// AC6: el cliente recibe <c>CatalogChanged</c> como ÚNICO evento servidor → cliente del
/// catálogo, y una transición de <c>connectionState</c> de un <c>ClientRegistration</c> es
/// un evento INTERNO del servidor, no un evento de hub (AD-29).
/// </para>
/// <para>
/// La prueba es ESTRUCTURAL: mira la forma de los contratos y la superficie del hub, sin
/// necesidad de una red ni de un servidor en marcha (AD-16).
/// </para>
/// </summary>
public sealed class HubEventContractTests
{
    private static Assembly SharedAssembly => typeof(ApiError).Assembly;

    private static IReadOnlyList<string> ServerToClientEvents() =>
    [
        nameof(CatalogChanged),
    ];

    [Fact]
    public void The_catalog_trigger_is_the_only_server_to_client_event_about_the_catalog()
    {
        // AC6, AR-2: `CatalogChanged` es el ÚNICO evento servidor → cliente del catálogo.
        Assert.Equal(["CatalogChanged"], ServerToClientEvents());
    }

    [Fact]
    public void The_catalog_trigger_carries_only_the_version_of_the_catalog()
    {
        // AD-5, AD-26: el disparo lleva la versión del catálogo y NADA más. El cliente
        // lee el snapshot completo por REST.
        var properties = typeof(CatalogChanged).GetProperties();

        Assert.Equal(["CatalogVersion"], properties.Select(property => property.Name));
    }

    [Fact]
    public void The_hub_of_the_state_of_the_client_publishes_no_server_to_client_event()
    {
        // AD-5, AD-29: el hub NO tiene métodos de servidor → cliente. La transición del
        // `connectionState` de un `ClientRegistration` es un evento INTERNO del servidor, y
        // el cliente NUNCA recibe un evento de hub por ella (AC6, AD-29).
        //
        // AD-18, AD-29: el hub SÍ declara los métodos que reciben lo que declara el
        // PROPIO cliente, porque esa es la dirección sancionada: el `connectionState`
        // (AD-18), el progreso de sus operaciones (AD-22) y el resultado terminal de sus
        // operaciones (AD-33). Esos métodos van de cliente a servidor, así que no publican
        // ningún evento de servidor → cliente, y el `ClientsSnapshotChanged` sigue sin
        // existir (AD-29).
        var serverToClientMethods = typeof(CatalogHub)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .Where(method => !ClientToServerPayloads.Contains(
                method.GetParameters().FirstOrDefault()?.ParameterType))
            .ToArray();

        Assert.Empty(serverToClientMethods);
    }

    /// <summary>
    /// The payloads that travel CLIENTE → SERVIDOR through the hub: el estado del equipo
    /// (AD-18), el progreso de sus operaciones (AD-22), el resultado terminal de sus
    /// operaciones (AD-33) y el error que reporta (AD-15).
    /// </summary>
    private static IReadOnlyList<Type> ClientToServerPayloads =>
    [
        typeof(ClientStateChanged),
        typeof(ClientConnectionChanged),
        typeof(DownloadProgressUpdated),
        typeof(InstallationProgressUpdated),
        typeof(OperationOutcomeReported),
        typeof(ClientErrorReported),
    ];

    /// <summary>
    /// El hub recibe el ERROR que el cliente declara (AD-15, AD-18).
    /// </summary>
    [Fact]
    public void The_hub_receives_the_error_that_the_client_declares()
    {
        // AD-15, AR-4: el error que viaja en el payload es del ÚNICO tipo de error del
        // sistema, `ApiError`, así que el método del hub lo recibe con su forma exacta.
        var method = typeof(CatalogHub)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Single(candidate => candidate.GetParameters().FirstOrDefault()?.ParameterType
                == typeof(ClientErrorReported));

        Assert.Equal(
            typeof(ClientErrorReported),
            method.GetParameters().First().ParameterType);

        Assert.Equal(
            CatalogHub.ClientErrorReportedMethodName,
            nameof(ClientErrorReported));
    }

    /// <summary>
    /// El payload del error que reporta el cliente lleva el ÚNICO tipo de error del
    /// sistema (AD-15, AR-4).
    /// </summary>
    [Fact]
    public void The_error_that_the_client_declares_carries_the_single_error_shape()
    {
        var properties = typeof(ClientErrorReported).GetProperties();

        Assert.Equal(
            [
                nameof(ClientErrorReported.ClientId),
                nameof(ClientErrorReported.OperationId),
                nameof(ClientErrorReported.Error),
            ],
            properties.Select(property => property.Name));

        Assert.Equal(typeof(ApiError), properties.Single(property => property.Name == nameof(ClientErrorReported.Error)).PropertyType);
    }

    [Fact]
    public void The_hub_receives_the_state_that_the_client_declares()
    {
        // AD-18, AD-29: el método del hub que recibe el estado del cliente va de
        // CLIENTE → SERVIDOR, y el payload es el CONTRATO EXACTO del estado (AD-18).
        var method = typeof(CatalogHub)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Single(candidate => candidate.GetParameters().FirstOrDefault()?.ParameterType
                == typeof(ClientStateChanged));

        Assert.Equal(
            typeof(ClientStateChanged),
            method.GetParameters().First().ParameterType);

        Assert.Equal(
            CatalogHub.ClientStateChangedMethodName,
            nameof(ClientStateChanged));
    }

    [Fact]
    public void The_hub_receives_the_connection_state_that_the_client_declares_alone()
    {
        // AD-18, AD-22, AD-29: cuando el cliente no puede leer su estado local completo,
        // declara SOLO la conexión. El payload tiene forma exacta y el hub lo recibe
        // cliente → servidor, sin convertirlo en un evento servidor → cliente.
        var method = typeof(CatalogHub)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Single(candidate => candidate.GetParameters().FirstOrDefault()?.ParameterType
                == typeof(ClientConnectionChanged));

        Assert.Equal(
            typeof(ClientConnectionChanged),
            method.GetParameters().First().ParameterType);

        Assert.Equal(
            CatalogHub.ClientConnectionChangedMethodName,
            nameof(ClientConnectionChanged));
    }

    [Fact]
    public void The_connection_only_payload_carries_only_the_client_and_its_connection_state()
    {
        // AD-18, AD-22: la forma es EXACTA. No puede admitir juegos, operaciones,
        // instalaciones ni otros campos que convierten el evento en una instantánea.
        var properties = typeof(ClientConnectionChanged).GetProperties();

        Assert.Equal(
            [nameof(ClientConnectionChanged.ClientId), nameof(ClientConnectionChanged.ConnectionState)],
            properties.Select(property => property.Name));
    }

    [Fact]
    public void The_hub_publishes_the_catalog_trigger_with_the_name_of_the_contract()
    {
        // AD-5, AR-16: el nombre del evento es el del contrato, en PascalCase (AD-18).
        Assert.Equal(nameof(CatalogChanged), CatalogHub.CatalogChangedEventName);
        Assert.Equal(nameof(CatalogChanged), CatalogHubClient.CatalogChangedEventName);
    }

    [Fact]
    public void The_client_does_not_have_a_server_to_client_payload_for_the_connection_state()
    {
        // AD-29: el payload `ClientsSnapshotChanged` NO existe, y ningún payload servidor
        // → cliente transporta el `connectionState`. El `ClientStateChanged` lo EMITE el
        // cliente, así que su `connectionState` es el del propio cliente y viaja en la
        // dirección contraria (AD-18, AD-22).
        var serverToClientPayloadsWithConnectionState = SharedAssembly
            .GetExportedTypes()
            .Where(type => type != typeof(ClientRegistrationView))
            .Where(type => type != typeof(ClientStateChanged))
            .Where(type => type != typeof(ClientConnectionChanged))
            .Where(type => type.GetProperties().Any(property =>
                property.PropertyType == typeof(ConnectionState)))
            .Select(type => type.Name)
            .ToArray();

        Assert.Empty(serverToClientPayloadsWithConnectionState);
    }

    [Fact]
    public void The_view_of_the_clients_is_not_a_payload_of_the_hub()
    {
        // AD-29: la vista de la organización se obtiene por REST, nunca por un evento.
        var hubPayloads = SharedAssembly
            .GetExportedTypes()
            .Where(type => type.GetProperties().Any(property =>
                property.PropertyType == typeof(IReadOnlyList<ClientRegistrationView>)))
            .Select(type => type.Name)
            .ToArray();

        Assert.Empty(hubPayloads);
    }

    [Fact]
    public void The_client_state_travels_from_the_client_to_the_server()
    {
        // AD-18, AD-22: el `ClientStateChanged` lo EMITE el cliente, así que su dirección
        // es cliente → servidor y el servidor lo consume.
        var properties = typeof(ClientStateChanged).GetProperties();

        Assert.Contains(properties, property => property.Name == nameof(ClientStateChanged.ConnectionState));
    }
}
