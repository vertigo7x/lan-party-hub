using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LANPartyHub.Client.Application.GameLauncher;
using LANPartyHub.Client.Presentation.Design;
using LANPartyHub.Client.Presentation.GameLauncher;

namespace LANPartyHub.Client.Presentation.Library;

/// <summary>
/// The Biblioteca of the player (FR-9, AC1, AC2, AC3, AC8, UX-DR-17, UX-DR-18,
/// UX-DR-28).
///
/// <para>
/// La Biblioteca lista las instalaciones del cliente y les aplica la MISMA tabla de
/// acciones que la ficha, con el motivo VISIBLE del bloqueo junto a la etiqueta de
/// «Jugar» (AC1, AC2, AC3, UX-DR-18).
/// </para>
/// <para>
/// El estado vacío dice «No tienes juegos instalados todavía.» y ofrece «Ver catálogo»,
/// así que ningún estado vacío queda sin salida accionable (AC8, UX-DR-28).
/// </para>
/// </summary>
public sealed class LibrarySurface : UserControl
{
    private readonly LibrarySurfaceViewModel _viewModel;
    private readonly TextBlock _emptyText;
    private readonly Button _emptyAction;
    private readonly StackPanel _list;
    private readonly StackPanel _root;
    private readonly Action<string>? _onPlay;
    private readonly Action<string, PlayabilityAction>? _onPrimary;
    private readonly Action<string>? _onSecondary;

    /// <summary>
    /// Creates the Biblioteca of the player.
    /// </summary>
    /// <param name="viewModel">View model of the Biblioteca.</param>
    /// <param name="onPlay">
    /// Action that the player triggers with «Jugar», or <c>null</c> when the Biblioteca
    /// is built without it (AC1).
    /// </param>
    /// <param name="onPrimary">
    /// Action of the PRIMARY button of the table of actions, or <c>null</c> when the
    /// Biblioteca is built without it (AC1, AC2).
    ///
    /// <para>
    /// La primaria es POLIMÓRFICA: es «Reparar» o «Descargar de nuevo» según la
    /// decisión, así que la acción recibe ALSO WHICH primary action was pressed, so the
    /// wiring registers the RIGHT intention (AC1, AC2).
    /// </para>
    /// </param>
    /// <param name="onSecondary">
    /// Action of the SECONDARY button of the table of actions, or <c>null</c> when the
    /// Biblioteca is built without it (AC1, AC2).
    /// </param>
    /// <param name="onViewCatalog">
    /// Action that the player triggers with «Ver catálogo» when the Biblioteca is empty,
    /// or <c>null</c> when the Biblioteca is built without it (AC8).
    /// </param>
    public LibrarySurface(
        LibrarySurfaceViewModel viewModel,
        Action<string>? onPlay = null,
        Action<string, PlayabilityAction>? onPrimary = null,
        Action<string>? onSecondary = null,
        Action? onViewCatalog = null)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        _viewModel = viewModel;

        // AC1, AC2: la fila de la Biblioteca lleva la MISMA tabla de acciones que la
        // ficha, así que sus delegates se GUARDAN y se REENVÍAN a cada fila: sin ellos
        // «Jugar», «Reparar»/«Descargar de nuevo» y «Diagnosticar» serían pulsaciones
        // MUERTAS (AC1, AC2).
        _onPlay = onPlay;
        _onPrimary = onPrimary;
        _onSecondary = onSecondary;

        _emptyText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(DesignTokens.InkSecondary),
            IsVisible = false,
        };

        _emptyAction = new Button
        {
            Content = LibrarySurfaceViewModel.ViewCatalogActionText,
            Height = DesignTokens.FieldInputHeight,
            HorizontalAlignment = HorizontalAlignment.Left,
            IsVisible = false,
        };

        _list = new StackPanel { Spacing = 12 };

        _root = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = "Biblioteca",
                    FontSize = DesignTokens.TitleFontSize,
                    FontWeight = FontWeight.SemiBold,
                },
                _emptyText,
                _emptyAction,
                _list,
            },
        };

        _emptyAction.Click += (_, _) => onViewCatalog?.Invoke();

        Content = _root;

        _viewModel.Changed += Render;

        Render();
    }

    /// <summary>
    /// The text of the empty state, so it can be inspected from the outside (AC8).
    /// </summary>
    public TextBlock EmptyText => _emptyText;

    /// <summary>
    /// The action of the empty state, so it can be inspected and driven from the outside
    /// (AC8).
    /// </summary>
    public Button EmptyAction => _emptyAction;

    /// <summary>
    /// The list of the installed videogames, so it can be inspected from the outside
    /// (AC1, AC2).
    /// </summary>
    public StackPanel List => _list;

    /// <summary>
    /// Replaces the installations that the client registered, con la decisión sobre su
    /// jugabilidad (AC1, AC2, AC8).
    ///
    /// <para>
    /// La lectura es LOCAL: las instalaciones pertenecen al cliente y la Biblioteca nunca
    /// va al servidor por ellas (AD-5, AD-22).
    /// </para>
    /// </summary>
    /// <param name="installations">
    /// Installations that the client registered, with the decision about their
    /// playability.
    /// </param>
    public void ApplyInstallations(IReadOnlyList<InstalledGamePlayability> installations)
    {
        _viewModel.ApplyInstallations(installations);

        Render();
    }

    /// <summary>
    /// Renders the state of the view model in the Biblioteca (AC1, AC2, AC3, AC8).
    /// </summary>
    private void Render()
    {
        _list.Children.Clear();

        foreach (var entry in _viewModel.Entries)
        {
            _list.Children.Add(BuildEntry(entry));
        }

        // AC8, UX-DR-28: el estado vacío dice exactamente su texto y ofrece su salida
        // accionable.
        _emptyText.Text = _viewModel.EmptyMessage;

        _emptyText.IsVisible = _viewModel.IsEmpty;

        _emptyAction.Content = _viewModel.EmptyActionText;

        _emptyAction.IsVisible = _viewModel.IsEmpty;
    }

    /// <summary>
    /// Builds the row of an installed videogame: su estado y su tabla de acciones (AC1,
    /// AC2).
    /// </summary>
    /// <param name="entry">Row of the Biblioteca.</param>
    /// <returns>The control of the row.</returns>
    private Control BuildEntry(LibraryEntryViewModel entry) => new StackPanel
    {
        Spacing = 8,
        Children =
        {
            new TextBlock
            {
                Text = entry.InstalledVersionText,
                FontSize = DesignTokens.MetaFontSize,
                Foreground = new SolidColorBrush(DesignTokens.InkSecondary),
            },

            // AC1, AC3: la fila lleva la MISMA tabla de acciones que la ficha, con el
            // motivo visible del bloqueo junto a la etiqueta de «Jugar».
            //
            // AC1, AC2: la tabla se construye CON los delegates de la Biblioteca, así que
            // «Jugar», «Reparar»/«Descargar de nuevo» y «Diagnosticar» disparan la
            // acción que el jugador pidió y NO son pulsaciones muertas.
            new GamePlayActionsView(entry.Actions, _onPlay, _onPrimary, _onSecondary),
        },
    };
}
