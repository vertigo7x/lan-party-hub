using LANPartyHub.Client.Application.Connection;
using LANPartyHub.Client.Presentation.Status;
using LANPartyHub.Shared.Contracts;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The PERSISTENT panel of the connection of the player (AC1, AC2, AC3, AC4, UX-DR-11,
/// UX-DR-23, UX-DR-29).
///
/// <para>
/// The tests fix what the panel SHOWS: el punto de presencia, el nombre del servidor, la
/// dirección en uso, la versión del servidor y la última operación terminada (AC1, AC2).
/// Con el servidor caído, el panel pasa a aviso con el contador del reintento y SIN modal
/// (AC3, AC4).
/// </para>
/// </summary>
public sealed class ConnectionStatusPanelViewModelTests
{
    private static readonly ServerEndpoint Endpoint = new("192.168.1.20", 5177);

    [Fact]
    public void The_panel_shows_the_presence_of_the_server()
    {
        // AC1: el punto de presencia es una de las cinco cosas que el panel muestra.
        var viewModel = new ConnectionStatusPanelViewModel();

        viewModel.ApplyAvailability(ServerAvailability.Reachable(
            Endpoint,
            "MESA-1",
            "1.4.0"));

        Assert.Equal(ConnectionStatusPanelViewModel.PresenceAvailableText, viewModel.PresenceText);
    }

    [Fact]
    public void The_panel_shows_the_name_of_the_server()
    {
        // AC1, UX-DR-11: el nombre del servidor es visible.
        var viewModel = new ConnectionStatusPanelViewModel();

        viewModel.ApplyAvailability(ServerAvailability.Reachable(
            Endpoint,
            "MESA-1",
            "1.4.0"));

        Assert.Equal("MESA-1", viewModel.ServerNameText);
    }

    [Fact]
    public void The_panel_shows_the_address_that_is_in_use()
    {
        // AC1, UX-DR-11: la dirección en uso es visible.
        var viewModel = new ConnectionStatusPanelViewModel();

        viewModel.ApplyAvailability(ServerAvailability.Reachable(
            Endpoint,
            "MESA-1",
            "1.4.0"));

        Assert.Equal("192.168.1.20:5177", viewModel.AddressText);
    }

    [Fact]
    public void The_panel_shows_the_version_of_the_server()
    {
        // AC1, UX-DR-11: la versión del servidor es visible.
        var viewModel = new ConnectionStatusPanelViewModel();

        viewModel.ApplyAvailability(ServerAvailability.Reachable(
            Endpoint,
            "MESA-1",
            "1.4.0"));

        Assert.Equal("1.4.0", viewModel.ServerVersionText);
    }

    [Fact]
    public void The_panel_shows_the_last_operation_that_finished()
    {
        // AC2, UX-DR-11: el panel muestra la última operación terminada CON su resultado.
        var viewModel = new ConnectionStatusPanelViewModel();

        viewModel.ApplyAvailability(ServerAvailability.Reachable(
            Endpoint,
            "MESA-1",
            "1.4.0"));

        viewModel.ApplyLastOperation("Descarga de «Call of Duty» · Completada");

        Assert.Equal("Descarga de «Call of Duty» · Completada", viewModel.LastOperationText);
    }

    [Fact]
    public void The_panel_never_shows_an_empty_space_for_the_last_operation()
    {
        // AC2, UX-DR-11: sin operaciones todavía, el panel dice exactamente eso.
        var viewModel = new ConnectionStatusPanelViewModel();

        viewModel.ApplyAvailability(ServerAvailability.Reachable(
            Endpoint,
            "MESA-1",
            "1.4.0"));

        Assert.Equal(ConnectionStatusPanelViewModel.NoOperationsYetText, viewModel.LastOperationText);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.LastOperationText));
    }

    [Fact]
    public void An_unavailable_server_shows_the_notice_that_the_catalog_is_conserved()
    {
        // AC3, UX-DR-29: el aviso dice EXACTAMENTE «Servidor no disponible. Se conserva el
        // último catálogo sincronizado.»
        var viewModel = new ConnectionStatusPanelViewModel();

        viewModel.ApplyAvailability(ServerAvailability.Unreachable(
            Endpoint,
            "MESA-1",
            "1.4.0",
            TimeSpan.FromSeconds(3)));

        Assert.True(viewModel.ShowsUnavailableNotice);
        Assert.Equal(
            "Servidor no disponible. Se conserva el último catálogo sincronizado.",
            viewModel.UnavailableNoticeText);
    }

    [Fact]
    public void An_unavailable_server_shows_the_presence_in_the_warning_state()
    {
        // AC3, AC4: el punto de presencia pasa a aviso: el texto NO es el de «Conectado».
        var viewModel = new ConnectionStatusPanelViewModel();

        viewModel.ApplyAvailability(ServerAvailability.Unreachable(
            Endpoint,
            "MESA-1",
            "1.4.0",
            TimeSpan.FromSeconds(3)));

        Assert.False(viewModel.IsAvailable);
        Assert.Equal(ConnectionStatusPanelViewModel.PresenceUnavailableText, viewModel.PresenceText);
        Assert.NotEqual(ConnectionStatusPanelViewModel.PresenceAvailableText, viewModel.PresenceText);
    }

    [Fact]
    public void An_unavailable_server_shows_the_retry_with_its_counter()
    {
        // AC4, UX-DR-29: el contador del reintento es VISIBLE: «Reintentando · 3 s».
        var viewModel = new ConnectionStatusPanelViewModel();

        viewModel.ApplyAvailability(ServerAvailability.Unreachable(
            Endpoint,
            "MESA-1",
            "1.4.0",
            TimeSpan.FromSeconds(3)));

        Assert.True(viewModel.ShowsRetryCountdown);
        Assert.Equal("Reintentando · 3 s", viewModel.RetryCountdownText);
    }

    [Fact]
    public void An_available_server_shows_no_retry_and_no_notice()
    {
        // AC1, AC4: con el servidor disponible, el panel no muestra ni el aviso ni el
        // contador del reintento.
        var viewModel = new ConnectionStatusPanelViewModel();

        viewModel.ApplyAvailability(ServerAvailability.Reachable(
            Endpoint,
            "MESA-1",
            "1.4.0"));

        Assert.False(viewModel.ShowsUnavailableNotice);
        Assert.False(viewModel.ShowsRetryCountdown);
        Assert.Equal(string.Empty, viewModel.RetryCountdownText);
    }

    [Fact]
    public void The_panel_publishes_its_change_so_the_surface_can_be_rendered()
    {
        // AC3: el panel se vuelve a pintar cuando la disponibilidad cambia.
        var viewModel = new ConnectionStatusPanelViewModel();

        var changes = 0;
        viewModel.Changed += () => changes++;

        viewModel.ApplyAvailability(ServerAvailability.Reachable(
            Endpoint,
            "MESA-1",
            "1.4.0"));

        Assert.Equal(1, changes);
    }

    [Fact]
    public void A_server_that_does_not_publish_its_name_shows_the_unknown_text()
    {
        // AC1: el panel NUNCA deja un espacio vacío.
        var viewModel = new ConnectionStatusPanelViewModel();

        viewModel.ApplyAvailability(ServerAvailability.Reachable(
            Endpoint,
            serverName: null,
            serverVersion: null));

        Assert.Equal(ConnectionStatusPanelViewModel.UnknownServerNameText, viewModel.ServerNameText);
        Assert.Equal(ConnectionStatusPanelViewModel.UnknownServerVersionText, viewModel.ServerVersionText);
    }
}
