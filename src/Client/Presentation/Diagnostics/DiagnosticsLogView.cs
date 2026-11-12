using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LANPartyHub.Client.Presentation.Design;

namespace LANPartyHub.Client.Presentation.Diagnostics;

/// <summary>
/// The VISOR de diagnóstico del cliente (AC1, AC2, AC3, AC4, NFR-7, UX-DR-41).
///
/// <para>
/// El visor muestra el registro de errores persistidos de su equipo: cada fila lleva las
/// CUATRO PARTES del error —el código estable, el mensaje, la causa y el siguiente paso— y
/// la asociación a la descarga, la instalación o el lanzamiento que la produjo (AC1,
/// AC2).
/// </para>
/// <para>
/// El visor ofrece el FILTRO por <c>operationId</c>, por <c>clientId</c> y por tipo de
/// operación, así que el jugador puede aislar el diagnóstico de una unidad de trabajo y
/// unirlo con el log del servidor, que comparte esos identificadores (AC3, AD-23).
/// </para>
/// <para>
/// AC4: el visor tiene VARIANTE CLARA con los tokens <c>-light</c> de <c>DESIGN.md</c>, y
/// el modo OSCURO es el PREDETERMINADO (UX-DR-41, UX-DR-1).
/// </para>
/// <para>
/// La vista se construye en C#, como las demás del cliente: el cliente no tiene XAML
/// (AD-1).
/// </para>
/// </summary>
public sealed class DiagnosticsLogView : UserControl
{
    private readonly DiagnosticsLogViewModel _viewModel;
    private readonly TextBox _operationIdFilter;
    private readonly TextBox _clientIdFilter;
    private readonly ComboBox _operationTypeFilter;
    private readonly TextBlock _emptyText;
    private readonly StackPanel _entries;
    private readonly Border _root;

    /// <summary>
    /// Creates the visor de diagnóstico del cliente.
    /// </summary>
    /// <param name="viewModel">View model of the visor.</param>
    public DiagnosticsLogView(DiagnosticsLogViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        _viewModel = viewModel;

        // AC3: los filtros por identificador son de TEXTO LIBRE, porque el identificador
        // es opaco y no se puede elegir de una lista (AD-23).
        _operationIdFilter = new TextBox
        {
            PlaceholderText = DiagnosticsLogViewModel.OperationIdFilterPlaceholder,
            Height = DesignTokens.FieldInputHeight,
        };

        _clientIdFilter = new TextBox
        {
            PlaceholderText = DiagnosticsLogViewModel.ClientIdFilterPlaceholder,
            Height = DesignTokens.FieldInputHeight,
        };

        // AC2: el filtro por TIPO ofrece SOLO los tipos que el registro tiene, así que
        // no ofrece un tipo que no existe (AD-25).
        _operationTypeFilter = new ComboBox
        {
            Height = DesignTokens.FieldInputHeight,
            MinWidth = 160,
        };

        _emptyText = new TextBlock
        {
            Text = DiagnosticsLogViewModel.EmptyText,
            TextWrapping = TextWrapping.Wrap,
        };

        _entries = new StackPanel { Spacing = DesignTokens.Spacing2 };

        _root = new Border
        {
            Padding = new Thickness(DesignTokens.Spacing4),
            Background = new SolidColorBrush(_viewModel.Background),
            Child = new StackPanel
            {
                Spacing = DesignTokens.Spacing2,
                Children =
                {
                    new TextBlock
                    {
                        Text = DiagnosticsLogViewModel.TitleText,
                        FontSize = DesignTokens.TitleFontSize,
                        FontWeight = DesignTokens.TitleFontWeight,
                        Foreground = new SolidColorBrush(_viewModel.PrimaryInk),
                    },

                    // AC4: el conmutador de la variante clara ofrece los tokens `-light` de
                    // `DESIGN.md`, y el modo oscuro es el predeterminado (UX-DR-41).
                    BuildLightVariantToggle(),

                    BuildFilterRow(),
                    _emptyText,
                    _entries,
                },
            },
        };

        _operationIdFilter.TextChanged += (_, _) =>
            _viewModel.OperationIdFilter = _operationIdFilter.Text ?? string.Empty;

        _clientIdFilter.TextChanged += (_, _) =>
            _viewModel.ClientIdFilter = _clientIdFilter.Text ?? string.Empty;

        _operationTypeFilter.SelectionChanged += (_, _) =>
            _viewModel.OperationTypeFilter = _operationTypeFilter.SelectedItem as string ?? string.Empty;

        Content = _root;

        _viewModel.Changed += Render;

        Render();
    }

    /// <summary>
    /// The field of the filter by <c>operationId</c>, so it can be inspected and driven
    /// from the outside (AC3).
    /// </summary>
    public TextBox OperationIdFilter => _operationIdFilter;

    /// <summary>
    /// The field of the filter by <c>clientId</c>, so it can be inspected and driven from
    /// the outside (AC3).
    /// </summary>
    public TextBox ClientIdFilter => _clientIdFilter;

    /// <summary>
    /// The selector of the filter by the KIND of operation, so it can be inspected and
    /// driven from the outside (AC2).
    /// </summary>
    public ComboBox OperationTypeFilter => _operationTypeFilter;

    /// <summary>
    /// The text of the empty state, so it can be inspected from the outside (AC1).
    /// </summary>
    public TextBlock EmptyText => _emptyText;

    /// <summary>
    /// The list of the entries of the register, so it can be inspected from the outside
    /// (AC1, AC2).
    /// </summary>
    public StackPanel Entries => _entries;

    /// <summary>
    /// The surface of the visor, so it can be inspected from the outside (AC4, UX-DR-41).
    /// </summary>
    public Border Root => _root;

    /// <summary>
    /// Builds the conmutador de la variante clara, que ofrece los tokens <c>-light</c> de
    /// <c>DESIGN.md</c> (AC4, UX-DR-41).
    /// </summary>
    /// <returns>The control of the conmutador.</returns>
    private Control BuildLightVariantToggle()
    {
        var toggle = new CheckBox
        {
            Content = DiagnosticsLogViewModel.LightVariantText,
            IsChecked = _viewModel.IsLightVariant,
        };

        toggle.IsCheckedChanged += (_, _) =>
        {
            if (toggle.IsChecked is true)
            {
                _viewModel.UseLightVariant();
            }
            else
            {
                _viewModel.UseDarkVariant();
            }
        };

        return toggle;
    }

    /// <summary>
    /// Builds the row of the FILTROS del visor (AC2, AC3).
    /// </summary>
    /// <returns>The row of the filters.</returns>
    private Control BuildFilterRow()
    {
        return new StackPanel
        {
            Spacing = DesignTokens.Spacing1,
            Children =
            {
                new TextBlock
                {
                    Text = DiagnosticsLogViewModel.FiltersLabelText,
                    FontSize = DesignTokens.MetaFontSize,
                    Foreground = new SolidColorBrush(_viewModel.SecondaryInk),
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = DesignTokens.Spacing2,
                    Children =
                    {
                        _operationIdFilter,
                        _clientIdFilter,
                        _operationTypeFilter,
                    },
                },
            },
        };
    }

    /// <summary>
    /// Renders the entries of the register in the visor (AC1, AC2, AC3, AC4).
    /// </summary>
    private void Render()
    {
        _root.Background = new SolidColorBrush(_viewModel.Background);

        _operationTypeFilter.ItemsSource = _viewModel.OperationTypes;

        _entries.Children.Clear();

        foreach (var entry in _viewModel.Entries)
        {
            _entries.Children.Add(BuildEntry(entry));
        }

        // AC1: el estado vacío dice que no hay errores registrados, así que el visor nunca
        // deja un espacio vacío sin explicación (UX-DR-28).
        _emptyText.IsVisible = _viewModel.IsEmpty;

        _emptyText.Foreground = new SolidColorBrush(_viewModel.SecondaryInk);
    }

    /// <summary>
    /// Builds the row of an error del registro: sus cuatro partes y su asociación a la
    /// operación que la produjo (AC1, AC2, AC3).
    /// </summary>
    /// <param name="entry">Entry of the register.</param>
    /// <returns>The control of the row.</returns>
    private Control BuildEntry(DiagnosticsLogEntry entry)
    {
        var code = new TextBlock
        {
            Text = entry.Code,
            FontSize = DesignTokens.MetaFontSize,
            FontFamily = new FontFamily(DesignTokens.MonoFontFamily),
            Foreground = new SolidColorBrush(_viewModel.Danger),
        };

        var message = new TextBlock
        {
            Text = entry.Message,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(_viewModel.PrimaryInk),
        };

        var cause = new TextBlock
        {
            Text = entry.Cause,
            TextWrapping = TextWrapping.Wrap,
            FontSize = DesignTokens.MetaFontSize,
            Foreground = new SolidColorBrush(_viewModel.SecondaryInk),
        };

        var nextStep = new TextBlock
        {
            Text = entry.NextStep,
            TextWrapping = TextWrapping.Wrap,
            FontSize = DesignTokens.MetaFontSize,
            Foreground = new SolidColorBrush(_viewModel.SecondaryInk),
        };

        // AC2, AC3: la fila lleva el TIPO de la operación y los dos identificadores que
        // la correlacionan con el log del servidor.
        var association = new TextBlock
        {
            Text = $"{entry.OperationType} · {entry.OperationId} · {entry.ClientId}",
            FontSize = DesignTokens.MetaFontSize,
            FontFamily = new FontFamily(DesignTokens.MonoFontFamily),
            Foreground = new SolidColorBrush(_viewModel.SecondaryInk),
        };

        return new Border
        {
            BorderBrush = new SolidColorBrush(_viewModel.HairlineBorder),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, DesignTokens.Spacing2, 0, DesignTokens.Spacing2),
            Child = new StackPanel
            {
                Spacing = DesignTokens.Spacing1,
                Children =
                {
                    code,
                    message,
                    cause,
                    nextStep,
                    association,
                },
            },
        };
    }
}
