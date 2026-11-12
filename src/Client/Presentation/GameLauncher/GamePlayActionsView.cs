using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using LANPartyHub.Client.Application.GameLauncher;
using LANPartyHub.Client.Presentation.Design;

namespace LANPartyHub.Client.Presentation.GameLauncher;

/// <summary>
/// The table of the actions of a videogame, with the VISIBLE reason of the block
/// (FR-9, AC1, AC2, AC3, AC5, AC6, UX-DR-17, UX-DR-18, UX-DR-26, UX-DR-38).
///
/// <para>
/// La vista pinta lo que el view model resolvió y NO define ningún texto de estado: el
/// motivo visible y las etiquetas de las acciones salen del view model (AC4, R-MVP-7).
/// </para>
/// <para>
/// La acción «Jugar» bloqueada NO se deshabilita: con <c>IsEnabled=false</c> Avalonia la
/// saca del recorrido de <c>Tab</c>, y el control bloqueado tiene que recibir foco con
/// su indicador VISIBLE (AC5, UX-DR-26). El indicador de foco lleva el color de la
/// acción, y NO el de la insignia del estado (AC5, UX-DR-38).
/// </para>
/// <para>
/// El motivo del bloqueo aparece JUNTO a la etiqueta de «Jugar», no en un
/// <em>tooltip</em>, no sólo en el log de diagnóstico y no depende de <em>hover</em> ni
/// de foco (AC3, UX-DR-18).
/// </para>
/// </summary>
public sealed class GamePlayActionsView : UserControl
{
    private readonly GamePlayActionsViewModel _viewModel;
    private readonly Button _play;
    private readonly Grid _playCell;
    private readonly Border _playFocus;
    private readonly TextBlock _reason;
    private readonly TextBlock _consequence;
    private readonly Button _primary;
    private readonly Button _secondary;
    private readonly StackPanel _actions;

    /// <summary>
    /// Creates the table of the actions of a videogame.
    /// </summary>
    /// <param name="viewModel">View model of the table of the actions.</param>
    /// <param name="onPlay">
    /// Action that launches the videogame, or <c>null</c> when the table is built without
    /// it (AC1).
    /// </param>
    /// <param name="onPrimary">
    /// Action of the PRIMARY button, or <c>null</c> when the table is built without it
    /// (AC1, AC2).
    ///
    /// <para>
    /// La primaria es POLIMÓRFICA: es «Reparar» o «Descargar de nuevo» según la
    /// decisión, así que la acción recibe ALSO WHICH primary action was pressed, so the
    /// wiring registers the RIGHT intention (AC1, AC2).
    /// </para>
    /// </param>
    /// <param name="onSecondary">
    /// Action of the SECONDARY button, or <c>null</c> when the table is built without it
    /// (AC1, AC2).
    /// </param>
    public GamePlayActionsView(
        GamePlayActionsViewModel viewModel,
        Action<string>? onPlay = null,
        Action<string, PlayabilityAction>? onPrimary = null,
        Action<string>? onSecondary = null)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        _viewModel = viewModel;

        // AC5: la acción «Jugar» bloqueada QUEDA HABILITADA, porque con
        // `IsEnabled=false` Avalonia la saca del recorrido de `Tab` y el control bloqueado
        // tiene que recibir foco (AC5, UX-DR-26). Lo que rehusa actuar es el comando.
        //
        // El botón NO se alinea a la izquierda por su cuenta: dentro del borde del
        // indicador de foco, un `HorizontalAlignment.Left` colapsa el botón a cero
        // ANCHO, y un control sin ancho no es un destino válido del recorrido de
        // `Tab` (AC5, UX-DR-26).
        _play = new Button
        {
            Content = GamePlayActionsViewModel.PlayActionText,
            Height = DesignTokens.FieldInputHeight,
        };

        // AC5, UX-DR-38: el indicador de foco es EXPLICITO y lleva el color de la
        // acción, así que no depende del color de la insignia del estado.
        //
        // El indicador NUNCA se oculta con `IsVisible=false`: un control invisible NO se
        // mide, así que el botón «Jugar» se quedaría sin ANCHO y el recorrido de `Tab`
        // lo omitiría —justo lo contrario de lo que exige AC5 (AC5, UX-DR-26). El
        // indicador se dibuja con el borde SIN grosor cuando el botón no tiene el foco,
        // y con el grosor del token cuando lo tiene.
        _playFocus = new Border
        {
            BorderBrush = new SolidColorBrush(DesignTokens.AccentAction),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(DesignTokens.RadiusSmall),
            Padding = new Thickness(DesignTokens.FieldInputFocusBorderThickness),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = _play,
        };

        _playCell = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Children =
            {
                _playFocus,
            },
        };

        // AC3, UX-DR-18: el motivo del bloqueo se pinta JUNTO a la etiqueta de «Jugar»,
        // y no en un tooltip.
        _reason = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(DesignTokens.StateDanger),
            IsVisible = false,
        };

        // AC1, UX-DR-20: la CONSECUENCIA de la reparación se NOMBRA junto a la acción
        // «Reparar», sin modal: el jugador sabe lo que va a pasar antes de que ocurra.
        _consequence = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(DesignTokens.InkSecondary),
            IsVisible = false,
        };

        // La primaria y la secundaria son SEAMS habilitadas: su ejecución vive en 8.3 y
        // en 5.x/6.x, y aquí el puerto registra la intención (AC1, AC2).
        _primary = new Button
        {
            Height = DesignTokens.FieldInputHeight,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        _secondary = new Button
        {
            Content = GamePlayActionsViewModel.DiagnoseActionText,
            Height = DesignTokens.FieldInputHeight,
            HorizontalAlignment = HorizontalAlignment.Left,
            IsVisible = false,
        };

        _actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = DesignTokens.Spacing2,
            Children =
            {
                _playCell,
                _primary,
                _secondary,
            },
        };

        // AC6: el comando REHUSA actuar cuando la instalación no está certificada, así
        // que `Enter` sobre «Jugar» bloqueada no produce ningún cambio de estado, no
        // inicia el juego y no dispara descarga, instalación ni reparación implícita.
        // El botón queda HABILITADO para que `Tab` lo alcance (AC5, UX-DR-26).
        _play.Click += (_, _) => InvokePlay(onPlay);

        _primary.Click += (_, _) => onPrimary?.Invoke(viewModel.GameId, viewModel.PrimaryAction);

        _secondary.Click += (_, _) => onSecondary?.Invoke(viewModel.GameId);

        // AC5, UX-DR-26: el indicador de foco aparece cuando el control RECIBE el foco, y
        // no cuando el puntero pasa por encima. El indicador cambia de GROSOR y no de
        // visibilidad: un control oculto no se mide y el botón se quedaría sin ancho
        // (AC5).
        _play.GotFocus += (_, _) => ShowFocusIndicator(true);

        _play.LostFocus += (_, _) => ShowFocusIndicator(false);

        Content = new StackPanel
        {
            Spacing = DesignTokens.Spacing2,
            Children =
            {
                _actions,
                _consequence,
                _reason,
            },
        };

        _viewModel.Changed += Render;

        Render();
    }

    /// <summary>
    /// The action «Jugar», so it can be inspected and driven from the outside (AC1, AC5).
    /// </summary>
    public Button PlayButton => _play;

    /// <summary>
    /// The indicator of the focus of the action «Jugar», so it can be inspected from the
    /// outside (AC5, UX-DR-38).
    /// </summary>
    public Border PlayFocusIndicator => _playFocus;

    /// <summary>
    /// The VISIBLE reason of the block, so it can be inspected from the outside (AC3,
    /// UX-DR-18).
    /// </summary>
    public TextBlock ReasonText => _reason;

    /// <summary>
    /// The VISIBLE consequence of the repair, so it can be inspected from the outside
    /// (AC1, UX-DR-20).
    /// </summary>
    public TextBlock ConsequenceText => _consequence;

    /// <summary>
    /// The PRIMARY action of the surface, so it can be inspected and driven from the
    /// outside (AC1, AC2).
    /// </summary>
    public Button PrimaryButton => _primary;

    /// <summary>
    /// The SECONDARY action of the surface, so it can be inspected and driven from the
    /// outside (AC1, AC2).
    /// </summary>
    public Button SecondaryButton => _secondary;

    /// <summary>
    /// The row of the actions, so it can be inspected from the outside (AC1).
    /// </summary>
    public StackPanel ActionsRow => _actions;

    /// <summary>
    /// The view model of the table of the actions, so it can be inspected and driven from
    /// the outside (AC1, AC2).
    /// </summary>
    public GamePlayActionsViewModel Actions => _viewModel;

    /// <summary>
    /// Shows or hides the indicator of the focus, cambiando el GROSOR del borde y no su
    /// visibilidad (AC5, UX-DR-38).
    ///
    /// <para>
    /// El indicador NUNCA se oculta con <c>IsVisible=false</c>: un control invisible no se
    /// mide, así que el botón «Jugar» se quedaría sin ANCHO y el recorrido de <c>Tab</c>
    /// lo omitiría (AC5, UX-DR-26).
    /// </para>
    /// </summary>
    /// <param name="visible">Whether the button has the focus.</param>
    private void ShowFocusIndicator(bool visible)
    {
        _playFocus.BorderThickness = new Thickness(visible ? DesignTokens.FieldInputFocusBorderThickness : 0);
    }

    /// <summary>
    /// Invokes the action of «Jugar» SÓLO when the installation is certified (AC1,
    /// AC6).
    ///
    /// <para>
    /// La acción bloqueada NO hace nada: el `Click` de Avalonia llega igual —el botón
    /// está habilitado para que `Tab` lo alcance—, así que el rechazo vive AQUÍ, en el
    /// punto donde la pulsación se despacha (AC6, UX-DR-26).
    /// </para>
    /// </summary>
    /// <param name="onPlay">Action that launches the videogame, or <c>null</c>.</param>
    private void InvokePlay(Action<string>? onPlay)
    {
        // AC6: con la instalación NO certificada no se inicia el juego, no se descarga,
        // no se instala y no se repara: el estado no cambia.
        if (_viewModel.IsBlocked)
        {
            return;
        }

        onPlay?.Invoke(_viewModel.GameId);
    }

    /// <summary>
    /// Renders the state of the view model in the table of the actions (AC1, AC2, AC3).
    /// </summary>
    private void Render()
    {
        // La etiqueta de «Jugar» es la MISMA con la acción habilitada y con la bloqueada:
        // lo que cambia es el MOTIVO que se pinta junto a ella (AC1, AC3).
        _play.Content = GamePlayActionsViewModel.PlayActionText;

        // AC5: el botón queda HABILITADO para que `Tab` lo alcance; el comando rehusa
        // (AC6).
        _play.IsEnabled = true;

        // AC5, UX-DR-38: el lector de pantalla anuncia el nombre de la acción, su estado
        // bloqueado y el motivo visible.
        AutomationProperties.SetName(_play, _viewModel.PlayAccessibleName);

        var reason = _viewModel.ReasonText;

        _reason.Text = reason ?? string.Empty;

        _reason.IsVisible = reason is not null;

        // AC1, UX-DR-20, FR-11: la consecuencia de la primaria se NOMBRA junto a ella,
        // sin modal: el jugador sabe si la descarga se repite, si se descarga la versión
        // COMPLETA o si se sustituye la versión instalada.
        var consequence = _viewModel.ConsequenceText;

        _consequence.Text = consequence ?? string.Empty;

        _consequence.IsVisible = consequence is not null;

        // La primaria y la secundaria se pintan SIEMPRE habilitadas: son seams, y su
        // ejecución vive en otras historias (AC1, AC2).
        _primary.Content = _viewModel.PrimaryActionText;

        AutomationProperties.SetName(_primary, _viewModel.PrimaryAccessibleName);

        var secondary = _viewModel.SecondaryActionText;

        _secondary.Content = secondary ?? GamePlayActionsViewModel.DiagnoseActionText;

        _secondary.IsVisible = secondary is not null;

        AutomationProperties.SetName(
            _secondary,
            secondary ?? GamePlayActionsViewModel.DiagnoseActionText);
    }
}
