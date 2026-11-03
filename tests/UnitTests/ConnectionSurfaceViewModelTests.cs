using LANPartyHub.Client.Application.Connection;
using LANPartyHub.Client.Presentation.Connection;
using LANPartyHub.Shared.Contracts;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The surface of the connection of the player (AC1, AC3, AC4, UX-DR-30).
///
/// <para>
/// The tests fix the microcopy que la historia exige: el texto «No se encontró ningún
/// servidor en esta red.» y la acción primaria «Introducir dirección manualmente». La
/// acción manual se conserva también cuando la red bloquea el descubrimiento, porque la
/// vía manual es permanente y equivalente (AC3, AC4, AD-3).
/// </para>
/// </summary>
public sealed class ConnectionSurfaceViewModelTests
{
    [Fact]
    public void A_network_without_a_server_shows_the_empty_text()
    {
        var viewModel = new ConnectionSurfaceViewModel();

        viewModel.ApplyDiscovery(DiscoveryResult.Empty());

        Assert.Equal(
            "No se encontró ningún servidor en esta red.",
            viewModel.EmptyMessage);
    }

    [Fact]
    public void A_network_without_a_server_offers_the_manual_address_as_the_primary_action()
    {
        var viewModel = new ConnectionSurfaceViewModel();

        viewModel.ApplyDiscovery(DiscoveryResult.Empty());

        Assert.Equal("Introducir dirección manualmente", viewModel.PrimaryActionText);
    }

    [Fact]
    public void A_blocked_discovery_keeps_the_manual_address()
    {
        // La red bloquea el mDNS: el cliente NO falla de forma fatal y la vía manual sigue
        // operativa, así que la acción primaria se conserva (AC4, AD-3).
        var viewModel = new ConnectionSurfaceViewModel();

        viewModel.ApplyDiscovery(DiscoveryResult.Blocked());

        Assert.True(viewModel.IsDiscoveryBlocked);
        Assert.Equal("Introducir dirección manualmente", viewModel.PrimaryActionText);
        Assert.True(viewModel.ShowsEmptyState);
    }

    [Fact]
    public void A_discovery_with_a_server_shows_the_server_and_no_empty_state()
    {
        var viewModel = new ConnectionSurfaceViewModel();

        viewModel.ApplyDiscovery(DiscoveryResult.Found(
        [
            new ServerEndpoint("192.168.1.20", 5177),
        ]));

        Assert.True(viewModel.HasServers);
        Assert.False(viewModel.ShowsEmptyState);
        Assert.Equal("192.168.1.20", Assert.Single(viewModel.Servers).Address);
        Assert.Equal(5177, viewModel.Servers[0].Port);
    }

    [Fact]
    public void A_discovery_with_a_server_does_not_show_the_empty_text()
    {
        var viewModel = new ConnectionSurfaceViewModel();

        viewModel.ApplyDiscovery(DiscoveryResult.Found(
        [
            new ServerEndpoint("192.168.1.20", 5177),
        ]));

        Assert.NotEqual("No se encontró ningún servidor en esta red.", viewModel.EmptyMessage);
    }

    [Fact]
    public void A_search_that_finds_nothing_after_finding_servers_empties_the_list()
    {
        var viewModel = new ConnectionSurfaceViewModel();

        viewModel.ApplyDiscovery(DiscoveryResult.Found(
        [
            new ServerEndpoint("192.168.1.20", 5177),
        ]));

        viewModel.ApplyDiscovery(DiscoveryResult.Empty());

        Assert.False(viewModel.HasServers);
        Assert.True(viewModel.ShowsEmptyState);
    }

    [Fact]
    public void The_surface_raises_the_change_when_the_outcome_arrives()
    {
        var viewModel = new ConnectionSurfaceViewModel();
        var changes = 0;

        viewModel.Changed += () => changes++;

        viewModel.ApplyDiscovery(DiscoveryResult.Empty());

        Assert.Equal(1, changes);
    }

    [Fact]
    public void The_address_that_the_player_types_is_kept()
    {
        // La vía manual es PERMANENTE: el texto que el jugador escribe vive en la
        // superficie hasta que la conexión termine (AC1).
        var viewModel = new ConnectionSurfaceViewModel();

        viewModel.SetAddress("192.168.1.20:5177");

        Assert.Equal("192.168.1.20:5177", viewModel.Address);
    }

    [Fact]
    public void A_connection_that_opens_shows_the_state_and_the_address_in_use()
    {
        // Tras conectar, el estado es «Ancla sin verificar»: conectar NO fija el ancla
        // (AD-6, AD-27).
        var viewModel = new ConnectionSurfaceViewModel();

        viewModel.ApplyConnection(ConnectionAttempt.Succeeded(
            new ServerEndpoint("192.168.1.20", 5177)));

        Assert.Equal(ConnectionState.AnchorUnverified, viewModel.ConnectionState);
        Assert.Equal("Ancla sin verificar", viewModel.ConnectionStateText);
        Assert.Equal("Servidor en uso: 192.168.1.20:5177", viewModel.AddressInUseText);
        Assert.False(viewModel.ShowsError);
    }

    [Fact]
    public void A_connection_that_fails_shows_the_three_parts_of_the_error()
    {
        // El aviso del error muestra QUÉ falló, POR QUÉ y QUÉ hacer ahora, y el código
        // interno NO es el texto principal (AC3, UX-DR-36, AR-4).
        var error = ErrorCatalog.Resolve(ErrorCatalog.ServerAddressInvalidCode);

        var viewModel = new ConnectionSurfaceViewModel();

        viewModel.ApplyConnection(ConnectionAttempt.Failed(null, error));

        Assert.True(viewModel.ShowsError);
        Assert.NotNull(viewModel.LastError);
        Assert.Contains(error.Message, viewModel.ErrorNoticeText);
        Assert.Contains(error.Cause, viewModel.ErrorNoticeText);
        Assert.Contains(error.NextStep, viewModel.ErrorNoticeText);
        Assert.False(viewModel.ErrorNoticeText.Contains(error.Code, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_connection_that_fails_leaves_the_client_disconnected()
    {
        var viewModel = new ConnectionSurfaceViewModel();

        viewModel.ApplyConnection(ConnectionAttempt.Failed(
            null,
            ErrorCatalog.Resolve(ErrorCatalog.ServerAddressInvalidCode)));

        Assert.Equal(ConnectionState.Disconnected, viewModel.ConnectionState);
        Assert.Equal("Desconectado", viewModel.ConnectionStateText);
        Assert.Equal(string.Empty, viewModel.AddressInUseText);
    }

    [Fact]
    public void The_surface_starts_disconnected_without_an_error()
    {
        var viewModel = new ConnectionSurfaceViewModel();

        Assert.Equal(ConnectionState.Disconnected, viewModel.ConnectionState);
        Assert.False(viewModel.ShowsError);
        Assert.Equal(string.Empty, viewModel.ErrorNoticeText);
    }

    [Fact]
    public void The_surface_raises_the_change_when_the_connection_finishes()
    {
        var viewModel = new ConnectionSurfaceViewModel();
        var changes = 0;

        viewModel.Changed += () => changes++;

        viewModel.ApplyConnection(ConnectionAttempt.Succeeded(
            new ServerEndpoint("192.168.1.20", 5177)));

        Assert.Equal(1, changes);
    }

    [Fact]
    public void An_incompatible_client_shows_the_exact_text_of_the_diagnostic()
    {
        // AD-6, UX-DR-31: el texto es EXACTO y lleva las dos versiones y el siguiente
        // paso: «Este cliente (1.2) no es compatible con el servidor (1.4). Actualiza el
        // cliente.»
        var viewModel = new ConnectionSurfaceViewModel();

        viewModel.ApplyVersionIncompatibility("1.2", "1.4");

        Assert.Equal(
            "Este cliente (1.2) no es compatible con el servidor (1.4). Actualiza el cliente.",
            viewModel.IncompatibleVersionMessage);
    }

    [Fact]
    public void An_incompatible_client_is_shown_the_diagnostic()
    {
        var viewModel = new ConnectionSurfaceViewModel();

        viewModel.ApplyVersionIncompatibility("1.2", "1.4");

        Assert.True(viewModel.ShowsIncompatibleVersion);
        Assert.Equal("1.2", viewModel.IncompatibleClientVersion);
        Assert.Equal("1.4", viewModel.IncompatibleServerVersion);
    }

    [Fact]
    public void An_incompatible_client_leaves_the_connection_disconnected()
    {
        // La incompatibilidad NO es un estado: el estado queda DESCONECTADO (AD-27).
        var viewModel = new ConnectionSurfaceViewModel();

        viewModel.ApplyVersionIncompatibility("1.2", "1.4");

        Assert.Equal(ConnectionState.Disconnected, viewModel.ConnectionState);
        Assert.Equal("Desconectado", viewModel.ConnectionStateText);
    }

    [Fact]
    public void The_surface_without_incompatibility_shows_no_diagnostic()
    {
        var viewModel = new ConnectionSurfaceViewModel();

        Assert.False(viewModel.ShowsIncompatibleVersion);
        Assert.Equal(string.Empty, viewModel.IncompatibleVersionMessage);
    }

    [Fact]
    public void A_connection_that_the_server_rejected_shows_the_diagnostic()
    {
        var viewModel = new ConnectionSurfaceViewModel();

        viewModel.ApplyConnection(ConnectionAttempt.Incompatible(
            new ServerEndpoint("192.168.1.20", 5177),
            ErrorCatalog.Resolve(ErrorCatalog.ClientVersionIncompatibleCode),
            "1.2",
            "1.4"));

        Assert.True(viewModel.ShowsIncompatibleVersion);
        Assert.Equal(
            "Este cliente (1.2) no es compatible con el servidor (1.4). Actualiza el cliente.",
            viewModel.IncompatibleVersionMessage);
    }

    [Fact]
    public void The_diagnostic_never_shows_the_internal_code_as_the_primary_text()
    {
        var viewModel = new ConnectionSurfaceViewModel();

        viewModel.ApplyVersionIncompatibility("1.2", "1.4");

        Assert.False(viewModel.IncompatibleVersionMessage.Contains(
            ErrorCatalog.ClientVersionIncompatibleCode,
            StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_diagnostic_is_cleared_when_the_client_tries_again()
    {
        var viewModel = new ConnectionSurfaceViewModel();

        viewModel.ApplyVersionIncompatibility("1.2", "1.4");

        viewModel.ClearVersionIncompatibility();

        Assert.False(viewModel.ShowsIncompatibleVersion);
        Assert.Equal(string.Empty, viewModel.IncompatibleVersionMessage);
    }

    [Fact]
    public void The_surface_has_no_form_of_credentials()
    {
        // AD-6, R-MVP-2, UX-DR-31: el modelo es una LAN administrada sin autenticación, así
        // que la superficie NO pide usuario, contraseña, token ni rol.
        var viewModel = new ConnectionSurfaceViewModel();

        var properties = typeof(ConnectionSurfaceViewModel)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        foreach (var forbidden in new[]
        {
            "UserName",
            "Username",
            "Password",
            "Token",
            "Role",
            "Credential",
            "Credentials",
        })
        {
            Assert.DoesNotContain(forbidden, properties);
        }
    }
}
