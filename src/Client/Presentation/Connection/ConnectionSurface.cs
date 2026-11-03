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
/// <para>
/// AD-6, R-MVP-2, UX-DR-31: NO hay formulario de credenciales. El modelo es una LAN
/// administrada sin autenticación, así que la superficie no pide usuario, contraseña,
/// token ni rol: el único control de acceso del protocolo es la compatibilidad de la
/// versión.
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
    private readonly TextBlock _incompatibleNotice;
    private readonly TextBlock _expectedFingerprintLabel;
    private readonly TextBox _expectedFingerprintInput;
    private readonly Button _fixAnchor;
    private readonly TextBlock _presentedFingerprint;
    private readonly TextBlock _anchorUnverifiedNotice;
    private readonly TextBlock _anchorMismatchNotice;
    private readonly TextBlock _disabledReasonNotice;
    private readonly Func<string?, CancellationToken, Task>? _onManualAddress;
    private readonly Func<string?, CancellationToken, Task>? _onFixAnchor;

    /// <summary>
    /// Creates the surface of the connection.
    /// </summary>
    /// <param name="viewModel">View model of the surface.</param>
    /// <param name="onManualAddress">
    /// Action that the player triggers with «Enter» in the field of the address, or
    /// <c>null</c> when the surface is built without it.
    /// </param>
    /// <param name="onFixAnchor">
    /// Action that the player triggers to fix the ancla de confianza, or <c>null</c> when
    /// the surface is built without it (AC2, AD-27).
    /// </param>
    public ConnectionSurface(
        ConnectionSurfaceViewModel viewModel,
        Func<string?, CancellationToken, Task>? onManualAddress = null,
        Func<string?, CancellationToken, Task>? onFixAnchor = null)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        _viewModel = viewModel;
        _onManualAddress = onManualAddress;
        _onFixAnchor = onFixAnchor;

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

        // AD-6, UX-DR-31: el diagnóstico de la incompatibilidad de la versión, con las dos
        // versiones y el siguiente paso.
        _incompatibleNotice = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
        };

        // AD-27: el campo del fingerprint ESPERADO. El valor llega por un canal fuera de
        // banda, así que el campo lo teclea el jugador con lo que le dio el organizador
        // (AC2, AD-27).
        _expectedFingerprintLabel = new TextBlock
        {
            Text = ConnectionSurfaceViewModel.ExpectedFingerprintLabelText,
        };

        _expectedFingerprintInput = new TextBox
        {
            PlaceholderText = ConnectionSurfaceViewModel.ExpectedFingerprintPlaceholderText,
            Height = DesignTokens.FieldInputHeight,
            VerticalContentAlignment = VerticalAlignment.Center,
        };

        _expectedFingerprintInput.TextChanged += (_, _) =>
            _viewModel.SetExpectedFingerprint(_expectedFingerprintInput.Text);

        _fixAnchor = new Button { Content = "Fijar ancla de confianza" };

        _fixAnchor.Click += (_, _) => ConfirmAnchor();

        _presentedFingerprint = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New, monospace"),
        };

        // AD-27: el aviso del ancla sin verificar. Es un estado de SEGURIDAD y no un error
        // de red, y la UI lo dice de forma explícita (AC7, AD-27).
        _anchorUnverifiedNotice = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
        };

        _anchorMismatchNotice = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
        };

        // AC4, UX-DR-29: el motivo de la acción deshabilitada es VISIBLE. El jugador tiene
        // que saber por qué no puede actuar, y no solo ver un botón que no responde.
        _disabledReasonNotice = new TextBlock
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
                _incompatibleNotice,
                _expectedFingerprintLabel,
                _expectedFingerprintInput,
                _fixAnchor,
                _presentedFingerprint,
                _anchorUnverifiedNotice,
                _anchorMismatchNotice,
                _disabledReasonNotice,
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
    /// The field of the fingerprint esperado, so the surface can be inspected and driven
    /// from the outside (AC2, AD-27).
    /// </summary>
    public TextBox ExpectedFingerprintInput => _expectedFingerprintInput;

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

        // AD-6, UX-DR-31: el diagnóstico de la incompatibilidad muestra las DOS
        // versiones y el siguiente paso: «Este cliente (1.2) no es compatible con el
        // servidor (1.4). Actualiza el cliente.»
        _incompatibleNotice.Text = _viewModel.ShowsIncompatibleVersion
            ? $"{ConnectionSurfaceViewModel.IncompatibleVersionTitleText}{Environment.NewLine}" +
              $"{_viewModel.IncompatibleVersionMessage}"
            : string.Empty;

        _incompatibleNotice.IsVisible = _viewModel.ShowsIncompatibleVersion;

        // AD-27: el aviso del ancla SIN VERIFICAR dice de forma explícita que el cliente
        // no descarga ni instala nada hasta que el ancla esté fijada (AC7, AD-27).
        _anchorUnverifiedNotice.Text = _viewModel.ShowsAnchorUnverified
            ? $"{ConnectionSurfaceViewModel.AnchorUnverifiedTitleText}{Environment.NewLine}" +
              $"{ConnectionSurfaceViewModel.AnchorUnverifiedText}"
            : string.Empty;

        _anchorUnverifiedNotice.IsVisible = _viewModel.ShowsAnchorUnverified;

        // AD-27: el fingerprint que el servidor presenta se muestra JUNTO al esperado,
        // para que el jugador pueda CONTRASTARLOS antes de confirmar (AC2, AD-27).
        _presentedFingerprint.Text = _viewModel.PresentedAnchorFingerprint is null
            ? string.Empty
            : $"{ConnectionSurfaceViewModel.PresentedFingerprintLabelText}{_viewModel.PresentedAnchorFingerprint}";

        _presentedFingerprint.IsVisible = _viewModel.PresentedAnchorFingerprint is not null;

        // AC6, AD-27: el aviso del ancla que NO coincide muestra los dos fingerprints.
        _anchorMismatchNotice.Text = _viewModel.ShowsAnchorMismatch
            ? $"{ConnectionSurfaceViewModel.AnchorMismatchTitleText}{Environment.NewLine}" +
              $"{_viewModel.AnchorMismatchText}"
            : string.Empty;

        _anchorMismatchNotice.IsVisible = _viewModel.ShowsAnchorMismatch;

        // AC4, UX-DR-29: la acción que REQUIERE el servidor queda deshabilitada con su
        // motivo visible. El motivo se muestra, así que el jugador sabe por qué no puede
        // actuar.
        _fixAnchor.IsEnabled = !_viewModel.DisablesServerActions;

        _disabledReasonNotice.Text = _viewModel.ShowsDisabledReason
            ? $"{ConnectionSurfaceViewModel.DisabledReasonLabelText}{_viewModel.DisabledReasonText}"
            : string.Empty;

        _disabledReasonNotice.IsVisible = _viewModel.ShowsDisabledReason;
    }

    /// <summary>
    /// Confirms the fingerprint esperado that the player typed, so the client fixes the
    /// ancla de confianza (AC2, AD-27).
    ///
    /// <para>
    /// AD-27: la acción NO escribe nada por su cuenta. La superficie entrega el valor al
    /// caso de uso, que es el único que deriva el fingerprint de la clave que presenta el
    /// servidor, lo COMPARA con el esperado y decide. El rechazo precede a persistir
    /// (AC2, AD-27).
    /// </para>
    /// </summary>
    private void ConfirmAnchor()
    {
        var fingerprint = _expectedFingerprintInput.Text;

        _viewModel.SetExpectedFingerprint(fingerprint);

        if (_onFixAnchor is null)
        {
            return;
        }

        _ = _onFixAnchor(fingerprint, CancellationToken.None);
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
