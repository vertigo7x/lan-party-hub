using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using LANPartyHub.Client.Presentation.Design;
using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Client.Presentation.Connection;

/// <summary>
/// Surface of the connection of the player (AC1, AC2, AC3, AC4, FR-3, UX-DR-30).
///
/// <para>
/// The surface offers the servers that the discovery found and, when there is none, the
/// state «No se encontró ningún servidor en esta red.» with the primary action «Introducir
/// dirección manualmente» (AC3, UX-DR-30).
/// </para>
/// <para>
/// The manual address is a PERMANENT and equivalent way to reach the server: the field of
/// the address lives in the surface in EVERY state, it has a FIXED height of 36 px, the
/// focus paints a border of 2 px with the accent of the action WITHOUT changing that
/// height, and «Enter» confirms the address (AC1, AC2, UX-DR-9, UX-DR-25).
/// </para>
/// <para>
/// The error is presented with its three parts: what failed, why and what to do now. The
/// internal code NEVER is the primary text (AC3, UX-DR-36, AR-4).
/// </para>
/// </summary>
public sealed class ConnectionSurface : UserControl
{
    private readonly ConnectionSurfaceViewModel _viewModel;
    private readonly ItemsControl _servers;
    private readonly TextBlock _emptyText;
    private readonly Button _manualAddress;
    private readonly TextBlock _addressLabel;
    private readonly TextBox _addressInput;
    private readonly TextBlock _connectionState;
    private readonly TextBlock _addressInUse;
    private readonly TextBlock _errorNotice;
    private readonly Func<string?, CancellationToken, Task>? _onManualAddress;

    /// <summary>
    /// Creates the surface of the connection.
    /// </summary>
    /// <param name="viewModel">View model of the surface.</param>
    /// <param name="onManualAddress">
    /// Action that the player triggers with «Enter» in the field of the address, or
    /// <c>null</c> when the surface is built without it.
    /// </param>
    public ConnectionSurface(
        ConnectionSurfaceViewModel viewModel,
        Func<string?, CancellationToken, Task>? onManualAddress = null)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        _viewModel = viewModel;
        _onManualAddress = onManualAddress;

        _servers = new ItemsControl();

        _emptyText = new TextBlock { TextWrapping = TextWrapping.Wrap };

        _manualAddress = new Button { Content = ConnectionSurfaceViewModel.ManualAddressText };

        _manualAddress.Click += (_, _) => ConfirmAddress();

        _addressLabel = new TextBlock { Text = ConnectionSurfaceViewModel.AddressLabelText };

        // El alto del campo es FIJO: el foco NO deforma la rejilla, porque el borde del
        // foco crece dentro del alto que ya tenía (AC2, UX-DR-9).
        _addressInput = new TextBox
        {
            PlaceholderText = ConnectionSurfaceViewModel.AddressPlaceholderText,
            Height = DesignTokens.FieldInputHeight,
            VerticalContentAlignment = VerticalAlignment.Center,
        };

        _addressInput.TextChanged += (_, _) => _viewModel.SetAddress(_addressInput.Text);

        _addressInput.KeyDown += OnAddressKeyDown;

        _connectionState = new TextBlock { Text = _viewModel.ConnectionStateText };

        _addressInUse = new TextBlock { Text = _viewModel.AddressInUseText };

        _errorNotice = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
        };

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
                _addressLabel,
                _addressInput,
                _connectionState,
                _addressInUse,
                _errorNotice,
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
    /// The field of the address of the server, so the surface can be inspected and driven
    /// from the outside (AC1).
    /// </summary>
    public TextBox AddressInput => _addressInput;

    /// <summary>
    /// Renders the state of the view model in the surface (AC1, AC2, AC3, AC4).
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

        _connectionState.Text = _viewModel.ConnectionStateText;
        _addressInUse.Text = _viewModel.AddressInUseText;

        // El aviso del error muestra las TRES partes y NUNCA el código interno como texto
        // principal (AC3, UX-DR-36, AR-4).
        _errorNotice.Text = _viewModel.ErrorNoticeText;
        _errorNotice.IsVisible = _viewModel.ShowsError;
    }

    /// <summary>
    /// Confirms the address that the player typed when the focus is in the field and the
    /// key is «Enter» (AC1, UX-DR-9, UX-DR-25).
    /// </summary>
    /// <param name="args">Event of the key.</param>
    private void OnAddressKeyDown(object? sender, KeyEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Key != Key.Enter)
        {
            return;
        }

        args.Handled = true;

        ConfirmAddress();
    }

    /// <summary>
    /// Confirms the address that the player typed (AC1).
    /// </summary>
    private void ConfirmAddress()
    {
        var address = _addressInput.Text;

        _viewModel.SetAddress(address);

        if (_onManualAddress is null)
        {
            return;
        }

        _ = _onManualAddress(address, CancellationToken.None);
    }
}
