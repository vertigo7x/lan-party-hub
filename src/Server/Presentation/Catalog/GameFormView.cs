using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LANPartyHub.Server.Domain.Catalog;

namespace LANPartyHub.Server.Presentation.Catalog;

/// <summary>
/// Window of the form of the alta and the modification of a videogame (AC4).
///
/// <para>
/// The window is built in code, like the whole interface of the product: the repository
/// has no XAML file (AD-1). The form marks as obligatory exactly the fields that FR-1
/// demands, the launch path and the version go in a monospaced font, and the size is
/// written in exact bytes and presented as the derived decimal value (AD-7, AC4).
/// </para>
/// <para>
/// The fonts are declared here and not in a system of tokens, because that system belongs
/// to the Epic 11, which is later. What this window reproduces is what the acceptance
/// criterion demands of the literals and of the derived size (AC4).
/// </para>
/// </summary>
public sealed class GameFormView : Window
{
    /// <summary>
    /// Font of the literals: the launch path, the version and the numeric value of the
    /// size. It is monospaced because those values are compared between them and they
    /// have to be aligned in column (AC4).
    /// </summary>
    public static readonly FontFamily MonospaceFontFamily =
        new("Cascadia Mono, Consolas, Courier New, monospace");

    /// <summary>
    /// Font of the small text that accompanies the literals, and among them the unit of
    /// the size (AC4).
    /// </summary>
    public static readonly FontFamily MetaFontFamily =
        new("Segoe UI, Arial, Helvetica, sans-serif");

    /// <summary>
    /// Size of the monospaced font of the literals (AC4).
    /// </summary>
    public const double MonospaceFontSize = 14;

    /// <summary>
    /// Size of the small text of the metadata (AC4).
    /// </summary>
    public const double MetaFontSize = 12;

    /// <summary>
    /// Creates the window of the form of the alta and the modificación of a videogame.
    /// </summary>
    /// <param name="viewModel">View model of the form.</param>
    public GameFormView(GameFormViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        Title = viewModel.IsModification ? "Editar videojuego" : "Registrar videojuego";
        Width = 640;
        Height = 720;

        ViewModel = viewModel;

        var name = Field("Nombre", viewModel.Name, GameRegistration.FieldNames.Name);
        var description = Field("Descripción", viewModel.Description, GameRegistration.FieldNames.Description);
        var categories = Field("Categorías", viewModel.Categories, GameRegistration.FieldNames.Categories);
        var tags = Field("Etiquetas", viewModel.Tags, GameRegistration.FieldNames.Tags);
        var coverImage = Field("Carátula", viewModel.CoverImage, GameRegistration.FieldNames.CoverImage);
        var images = Field("Imágenes", viewModel.Images, GameRegistration.FieldNames.Images);

        var version = new TextBox
        {
            PlaceholderText = "Versión",
            Text = viewModel.Version,
            FontFamily = MonospaceFontFamily,
            FontSize = MonospaceFontSize,
        };

        var sizeBytes = new TextBox
        {
            PlaceholderText = "Tamaño en bytes exactos",
            Text = viewModel.SizeBytes > 0
                ? viewModel.SizeBytes.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : string.Empty,
        };

        // The size is written in exact bytes and it is presented as the derived decimal
        // value: the number in the monospaced token of sizes and the unit in the small
        // token of metadata (AD-7, AC4).
        var sizeValue = new TextBlock
        {
            Text = viewModel.SizeValue,
            FontFamily = MonospaceFontFamily,
            FontSize = MonospaceFontSize,
        };

        var sizeUnit = new TextBlock
        {
            Text = viewModel.SizeUnit,
            FontFamily = MetaFontFamily,
            FontSize = MetaFontSize,
        };

        var sizeLine = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children =
            {
                sizeValue,
                sizeUnit,
            },
        };

        var minPlayers = Field("Jugadores mínimo", viewModel.MinPlayers.ToString(System.Globalization.CultureInfo.InvariantCulture), GameRegistration.FieldNames.MinPlayers);
        var maxPlayers = Field("Jugadores máximo", viewModel.MaxPlayers.ToString(System.Globalization.CultureInfo.InvariantCulture), GameRegistration.FieldNames.MaxPlayers);
        var operatingSystem = Field("Sistema operativo", viewModel.OperatingSystem, GameRegistration.FieldNames.OperatingSystem);
        var distributionType = Field("Tipo de distribución", viewModel.DistributionType, GameRegistration.FieldNames.DistributionType);
        var installationType = Field("Tipo de instalación", viewModel.InstallationType, GameRegistration.FieldNames.InstallationType);

        var launchExecutablePath = new TextBox
        {
            PlaceholderText = "Ruta de lanzamiento",
            Text = viewModel.LaunchExecutablePath,
            FontFamily = MonospaceFontFamily,
            FontSize = MonospaceFontSize,
        };

        var launchArguments = Field("Argumentos de lanzamiento", viewModel.LaunchArguments, GameRegistration.FieldNames.LaunchConfiguration);
        var launchWorkingDirectory = new TextBox
        {
            PlaceholderText = "Directorio de trabajo",
            Text = viewModel.LaunchWorkingDirectory,
            FontFamily = MonospaceFontFamily,
            FontSize = MonospaceFontSize,
        };

        var dependencies = Field("Dependencias", viewModel.Dependencies, GameRegistration.FieldNames.Dependencies);
        var license = Field("Licencia", viewModel.License, GameRegistration.FieldNames.License);
        var restrictions = Field("Restricciones de distribución", viewModel.DistributionRestrictions, GameRegistration.FieldNames.DistributionRestrictions);

        var save = new Button { Content = "Guardar" };
        var cancel = new Button { Content = "Cancelar" };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };

        save.Click += (_, _) => status.Text = "El guardado del videojuego lo confirma el servidor.";
        cancel.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 8,
            Children =
            {
                new TextBlock
                {
                    Text = Title,
                    FontSize = 22,
                    FontWeight = FontWeight.SemiBold,
                },
                new TextBlock
                {
                    Text = "Los campos marcados con * son obligatorios.",
                    FontFamily = MetaFontFamily,
                    FontSize = MetaFontSize,
                    TextWrapping = TextWrapping.Wrap,
                },
                Label("Nombre *"),
                name,
                Label("Descripción *"),
                description,
                Label("Categorías *"),
                categories,
                Label("Etiquetas *"),
                tags,
                Label("Carátula *"),
                coverImage,
                Label("Imágenes *"),
                images,
                Label("Versión *"),
                version,
                Label("Tamaño en bytes exactos *"),
                sizeBytes,
                sizeLine,
                Label("Jugadores mínimo *"),
                minPlayers,
                Label("Jugadores máximo *"),
                maxPlayers,
                Label("Sistema operativo *"),
                operatingSystem,
                Label("Tipo de distribución *"),
                distributionType,
                Label("Tipo de instalación *"),
                installationType,
                Label("Configuración de lanzamiento *"),
                Label("Ruta de lanzamiento *"),
                launchExecutablePath,
                Label("Argumentos de lanzamiento *"),
                launchArguments,
                Label("Directorio de trabajo *"),
                launchWorkingDirectory,
                Label("Dependencias *"),
                dependencies,
                Label("Licencia *"),
                license,
                Label("Restricciones de distribución *"),
                restrictions,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        save,
                        cancel,
                    },
                },
                status,
            },
        };
    }

    /// <summary>View model of the form.</summary>
    public GameFormViewModel ViewModel { get; }

    /// <summary>
    /// Builds a labelled field of the form. The label carries the mark of the obligation
    /// when FR-1 demands the field (AC4).
    /// </summary>
    /// <param name="label">Label of the field.</param>
    /// <param name="value">Value of the field.</param>
    /// <param name="fieldName">Name of the field, as the domain names it.</param>
    /// <returns>The control of the field.</returns>
    private static TextBox Field(string label, string value, string fieldName)
    {
        var box = new TextBox
        {
            PlaceholderText = label.TrimEnd('*').Trim(),
            Text = value,
        };

        ToolTip.SetTip(box, GameFormViewModel.LabelFor(label, fieldName));

        return box;
    }

    /// <summary>
    /// Builds the label of a field. The mark of the obligation goes only on the fields
    /// that FR-1 demands (AC4).
    /// </summary>
    /// <param name="label">Label of the field.</param>
    /// <returns>The control of the label.</returns>
    private static TextBlock Label(string label) => new()
    {
        Text = label,
        FontFamily = MetaFontFamily,
        FontSize = MetaFontSize,
    };
}
