using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LANPartyHub.Server.Application.Catalog;
using LANPartyHub.Server.Domain.Catalog;
using LANPartyHub.Shared.Contracts;

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
/// <para>
/// The button of the save hands the videogame to the use case of the catalog: the server
/// and its interface live in the same process (AD-1), so the call is a direct call to the
/// use case and not a call over the network (AD-5).
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

    private readonly TextBox _name;
    private readonly TextBox _description;
    private readonly TextBox _categories;
    private readonly TextBox _tags;
    private readonly TextBox _coverImage;
    private readonly TextBox _images;
    private readonly TextBox _version;
    private readonly TextBox _sizeBytes;
    private readonly TextBox _minPlayers;
    private readonly TextBox _maxPlayers;
    private readonly TextBox _operatingSystem;
    private readonly TextBox _distributionType;
    private readonly TextBox _installationType;
    private readonly TextBox _launchExecutablePath;
    private readonly TextBox _launchArguments;
    private readonly TextBox _launchWorkingDirectory;
    private readonly TextBox _dependencies;
    private readonly TextBox _license;
    private readonly TextBox _distributionRestrictions;
    private readonly TextBlock _sizeValue;
    private readonly TextBlock _status;

    /// <summary>
    /// Creates the window of the form of the alta and la modificación of a videogame.
    /// </summary>
    /// <param name="viewModel">View model of the form.</param>
    /// <param name="registerGame">Use case of the registration of a videogame.</param>
    /// <param name="modifyGame">Use case of the modification of a videogame.</param>
    public GameFormView(
        GameFormViewModel viewModel,
        RegisterGame registerGame,
        ModifyGame modifyGame)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(registerGame);
        ArgumentNullException.ThrowIfNull(modifyGame);

        Title = viewModel.IsModification ? "Editar videojuego" : "Registrar videojuego";
        Width = 640;
        Height = 720;

        ViewModel = viewModel;

        _name = Field("Nombre", viewModel.Name, GameRegistration.FieldNames.Name);
        _description = Field("Descripción", viewModel.Description, GameRegistration.FieldNames.Description);
        _categories = Field("Categorías", viewModel.Categories, GameRegistration.FieldNames.Categories);
        _tags = Field("Etiquetas", viewModel.Tags, GameRegistration.FieldNames.Tags);
        _coverImage = Field("Carátula", viewModel.CoverImage, GameRegistration.FieldNames.CoverImage);
        _images = Field("Imágenes", viewModel.Images, GameRegistration.FieldNames.Images);

        _version = new TextBox
        {
            PlaceholderText = "Versión",
            Text = viewModel.Version,
            FontFamily = MonospaceFontFamily,
            FontSize = MonospaceFontSize,
        };

        _sizeBytes = new TextBox
        {
            PlaceholderText = "Tamaño en bytes exactos",
            Text = viewModel.SizeBytes > 0
                ? viewModel.SizeBytes.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : string.Empty,
        };

        // The size is written in exact bytes and it is presented as the derived decimal
        // value: the number in the monospaced token of sizes and the unit in the small
        // token of metadata (AD-7, AC4).
        _sizeValue = new TextBlock
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
                _sizeValue,
                sizeUnit,
            },
        };

        _minPlayers = Field("Jugadores mínimo", viewModel.MinPlayers.ToString(System.Globalization.CultureInfo.InvariantCulture), GameRegistration.FieldNames.MinPlayers);
        _maxPlayers = Field("Jugadores máximo", viewModel.MaxPlayers.ToString(System.Globalization.CultureInfo.InvariantCulture), GameRegistration.FieldNames.MaxPlayers);
        _operatingSystem = Field("Sistema operativo", viewModel.OperatingSystem, GameRegistration.FieldNames.OperatingSystem);
        _distributionType = Field("Tipo de distribución", viewModel.DistributionType, GameRegistration.FieldNames.DistributionType);
        _installationType = Field("Tipo de instalación", viewModel.InstallationType, GameRegistration.FieldNames.InstallationType);

        _launchExecutablePath = new TextBox
        {
            PlaceholderText = "Ruta de lanzamiento",
            Text = viewModel.LaunchExecutablePath,
            FontFamily = MonospaceFontFamily,
            FontSize = MonospaceFontSize,
        };

        _launchArguments = Field("Argumentos de lanzamiento", viewModel.LaunchArguments, GameRegistration.FieldNames.LaunchConfiguration);
        _launchWorkingDirectory = new TextBox
        {
            PlaceholderText = "Directorio de trabajo",
            Text = viewModel.LaunchWorkingDirectory,
            FontFamily = MonospaceFontFamily,
            FontSize = MonospaceFontSize,
        };

        _dependencies = Field("Dependencias", viewModel.Dependencies, GameRegistration.FieldNames.Dependencies);
        _license = Field("Licencia", viewModel.License, GameRegistration.FieldNames.License);
        _distributionRestrictions = Field("Restricciones de distribución", viewModel.DistributionRestrictions, GameRegistration.FieldNames.DistributionRestrictions);

        var save = new Button { Content = "Guardar" };
        var cancel = new Button { Content = "Cancelar" };
        _status = new TextBlock { TextWrapping = TextWrapping.Wrap };

        save.Click += async (_, _) => await SaveAsync(registerGame, modifyGame).ConfigureAwait(true);
        cancel.Click += (_, _) => Close();

        // The size that is typed is reflected in the derived value as it is written, so the
        // organizer sees the value that the catalog will publish (AD-7, AC4).
        _sizeBytes.TextChanged += (_, _) => RefreshSizeValue();

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
                _name,
                Label("Descripción *"),
                _description,
                Label("Categorías *"),
                _categories,
                Label("Etiquetas *"),
                _tags,
                Label("Carátula *"),
                _coverImage,
                Label("Imágenes *"),
                _images,
                Label("Versión *"),
                _version,
                Label("Tamaño en bytes exactos *"),
                _sizeBytes,
                sizeLine,
                Label("Jugadores mínimo *"),
                _minPlayers,
                Label("Jugadores máximo *"),
                _maxPlayers,
                Label("Sistema operativo *"),
                _operatingSystem,
                Label("Tipo de distribución *"),
                _distributionType,
                Label("Tipo de instalación *"),
                _installationType,
                Label("Configuración de lanzamiento *"),
                Label("Ruta de lanzamiento *"),
                _launchExecutablePath,
                Label("Argumentos de lanzamiento *"),
                _launchArguments,
                Label("Directorio de trabajo *"),
                _launchWorkingDirectory,
                Label("Dependencias *"),
                _dependencies,
                Label("Licencia *"),
                _license,
                Label("Restricciones de distribución *"),
                _distributionRestrictions,
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
                _status,
            },
        };
    }

    /// <summary>View model of the form.</summary>
    public GameFormViewModel ViewModel { get; }

    /// <summary>
    /// The videogame that the form has registered or modified, or <c>null</c> when the
    /// form has not been saved yet.
    /// </summary>
    public GameDescriptor? SavedGame { get; private set; }

    /// <summary>
    /// Reads the fields of the form and hands the videogame to the use case of the
    /// catalog. The rejection of the domain is shown in the window, and it is the text of
    /// the domain that names the field that fails (AD-25).
    /// </summary>
    /// <param name="registerGame">Use case of the registration of a videogame.</param>
    /// <param name="modifyGame">Use case of the modification of a videogame.</param>
    /// <returns>A task that completes when the videogame has been saved.</returns>
    private async Task SaveAsync(RegisterGame registerGame, ModifyGame modifyGame)
    {
        try
        {
            ReadFields();

            var metadata = ViewModel.ToDescriptor();

            SavedGame = ViewModel.IsModification
                ? await modifyGame
                    .ExecuteAsync(ViewModel.GameId!, metadata, CancellationToken.None)
                    .ConfigureAwait(true)
                : await registerGame
                    .ExecuteAsync(metadata, CancellationToken.None)
                    .ConfigureAwait(true);

            _status.Text = ViewModel.IsModification
                ? $"El videogame «{SavedGame.Name}» se ha modificado con la versión {SavedGame.Version}."
                : $"El videogame «{SavedGame.Name}» se ha registrado con el identificador {SavedGame.GameId}.";

            Close();
        }
        catch (GameMetadataMissingException exception)
        {
            _status.Text = exception.Message;
        }
        catch (GamePlayerRangeException exception)
        {
            _status.Text = exception.Message;
        }
        catch (GameNotFoundException exception)
        {
            _status.Text = exception.Message;
        }
        catch (Exception exception)
        {
            // The exception never reaches the interface as a stack trace: the organizer
            // sees a line and the operator finds the detail in the log (AD-15).
            _status.Text = $"No se pudo guardar el videogame: {exception.Message}";
        }
    }

    /// <summary>
    /// Reads the fields of the form into the view model. The size is read as exact bytes
    /// and it is never rounded on the way in (AD-7).
    /// </summary>
    private void ReadFields()
    {
        ViewModel.Name = _name.Text ?? string.Empty;
        ViewModel.Description = _description.Text ?? string.Empty;
        ViewModel.Categories = _categories.Text ?? string.Empty;
        ViewModel.Tags = _tags.Text ?? string.Empty;
        ViewModel.CoverImage = _coverImage.Text ?? string.Empty;
        ViewModel.Images = _images.Text ?? string.Empty;
        ViewModel.Version = _version.Text ?? string.Empty;
        ViewModel.SizeBytes = ParseSizeBytes(_sizeBytes.Text);
        ViewModel.MinPlayers = ParseInt(_minPlayers.Text, ViewModel.MinPlayers);
        ViewModel.MaxPlayers = ParseInt(_maxPlayers.Text, ViewModel.MaxPlayers);
        ViewModel.OperatingSystem = _operatingSystem.Text ?? string.Empty;
        ViewModel.DistributionType = _distributionType.Text ?? string.Empty;
        ViewModel.InstallationType = _installationType.Text ?? string.Empty;
        ViewModel.LaunchExecutablePath = _launchExecutablePath.Text ?? string.Empty;
        ViewModel.LaunchArguments = _launchArguments.Text ?? string.Empty;
        ViewModel.LaunchWorkingDirectory = _launchWorkingDirectory.Text ?? string.Empty;
        ViewModel.Dependencies = _dependencies.Text ?? string.Empty;
        ViewModel.License = _license.Text ?? string.Empty;
        ViewModel.DistributionRestrictions = _distributionRestrictions.Text ?? string.Empty;
    }

    /// <summary>
    /// Reflects in the derived value the size that is being typed.
    /// </summary>
    private void RefreshSizeValue()
    {
        var bytes = ParseSizeBytes(_sizeBytes.Text);

        ViewModel.SizeBytes = bytes;

        _sizeValue.Text = Presentation.Formatting.SizeFormatter.FormatValue(bytes);
    }

    /// <summary>
    /// Reads a size in exact bytes. A text that is not a number is read as zero, and the
    /// domain rejects it as a missing metadata (AD-1, AD-7).
    /// </summary>
    /// <param name="text">Text of the field of the size.</param>
    /// <returns>The exact size in bytes.</returns>
    private static long ParseSizeBytes(string? text) =>
        long.TryParse(
            text?.Trim(),
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out var bytes)
            ? bytes
            : 0;

    /// <summary>
    /// Reads a number. A text that is not a number keeps the value that the view model
    /// already had, and the domain rejects the incoherent one (AD-1).
    /// </summary>
    /// <param name="text">Text of the field.</param>
    /// <param name="fallback">Value that the field keeps when the text is not a number.</param>
    /// <returns>The number of the field.</returns>
    private static int ParseInt(string? text, int fallback) =>
        int.TryParse(
            text?.Trim(),
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out var value)
            ? value
            : fallback;

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
