using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Client.Presentation.Connection;

/// <summary>
/// Surface of the connection of the player (AC1, AC3, AC4, FR-3, UX-DR-30).
///
/// <para>
/// The surface offers the servers that the discovery found and, when there is none, the
/// state «No se encontró ningún servidor en esta red.» with the primary action «Introducir
/// dirección manualmente» (AC3, UX-DR-30).
/// </para>
/// <para>
/// The manual address is a PERMANENT and equivalent way to reach the server: the action is
/// offered in EVERY state in which there is no server to choose, and a blocked discovery is
/// NOT a fatal error (AC4, AD-3).
/// </para>
/// </summary>
public sealed class ConnectionSurface : UserControl
{
    private readonly ConnectionSurfaceViewModel _viewModel;
    private readonly ItemsControl _servers;
    private readonly TextBlock _emptyText;
    private readonly Button _manualAddress;

    /// <summary>
    /// Creates the surface of the connection.
    /// </summary>
    /// <param name="viewModel">View model of the surface.</param>
    /// <param name="onManualAddress">
    /// Action that the player triggers with «Introducir dirección manualmente», or
    /// <c>null</c> when the surface is built without it.
    /// </param>
    public ConnectionSurface(
        ConnectionSurfaceViewModel viewModel,
        Action? onManualAddress = null)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        _viewModel = viewModel;

        _servers = new ItemsControl();

        _emptyText = new TextBlock { TextWrapping = TextWrapping.Wrap };

        _manualAddress = new Button { Content = ConnectionSurfaceViewModel.ManualAddressText };

        _manualAddress.Click += (_, _) => onManualAddress?.Invoke();

        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = "Conexión",
                    FontSize = 22,
                    FontWeight = Avalonia.Media.FontWeight.SemiBold,
                },
                _servers,
                _emptyText,
                _manualAddress,
            },
        };

        _viewModel.Changed += Render;

        Render();
    }

    /// <summary>
    /// The server that the player picked, or <c>null</c> when the player has not picked
    /// any (AC1).
    /// </summary>
    public ServerEndpoint? SelectedServer { get; private set; }

    /// <summary>
    /// Renders the state of the view model in the surface (AC1, AC3, AC4).
    /// </summary>
    private void Render()
    {
        _servers.ItemsSource = _viewModel.Servers;

        // La lista de servidores y el estado vacío son EXCLUYENTES: con un servidor que
        // elegir no hay nada que anunciar (AC1).
        if (_viewModel.ShowsEmptyState)
        {
            _emptyText.Text = _viewModel.EmptyMessage;
            _emptyText.IsVisible = true;
        }
        else
        {
            _emptyText.Text = string.Empty;
            _emptyText.IsVisible = false;
        }

        // La acción manual es la acción PRIMARIA y está disponible en todos los estados
        // sin servidor, incluido el bloqueo de la red (AC3, AC4).
        _manualAddress.Content = _viewModel.PrimaryActionText;
        _manualAddress.IsVisible = _viewModel.ShowsEmptyState;
    }
}
