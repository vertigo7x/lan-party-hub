using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LANPartyHub.Server.Application.Packages;
using LANPartyHub.Server.Domain.Catalog;
using LANPartyHub.Server.Domain.Packages;
using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Server.Presentation.Packages;

/// <summary>
/// Window of the form of the publication of a package (AC1).
///
/// <para>
/// The window is built in code, like the whole interface of the product: the repository
/// has no XAML file (AD-1). The form marks as obligatory exactly the fields that the
/// publication demands, the version and the identifier of the package go in a monospaced
/// font, and the size is written in exact bytes and presented as the derived decimal value
/// (AD-7, AC4).
/// </para>
/// <para>
/// The button of the publication hands the package to the use case: the server and its
/// interface live in the same process (AD-1), so the call is a direct call to the use case
/// and not a call over the network (AD-5).
/// </para>
/// <para>
/// When the publication is blocked because the videogame does not carry the license or the
/// restrictions of distribution, the form says <em>which</em> of the two is missing, and
/// not a generic error (AC1, UX-DR-44).
/// </para>
/// </summary>
public sealed class PackageFormView : Window
{
    /// <summary>
    /// Font of the literals: the identifier of the package, the version and the numeric
    /// value of the size (AC4).
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

    private readonly TextBox _gameId;
    private readonly TextBox _packageId;
    private readonly TextBox _version;
    private readonly TextBox _sizeBytes;
    private readonly TextBox _launchExecutablePath;
    private readonly TextBox _launchArguments;
    private readonly TextBox _launchWorkingDirectory;
    private readonly TextBlock _sizeValue;
    private readonly TextBlock _status;

    /// <summary>
    /// Creates the window of the form of the publication of a package.
    /// </summary>
    /// <param name="viewModel">View model of the form.</param>
    /// <param name="publishPackage">Use case of the publication of a package.</param>
    public PackageFormView(PackageFormViewModel viewModel, PublishPackage publishPackage)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(publishPackage);

        Title = "Publicar paquete";
        Width = 640;
        Height = 620;

        ViewModel = viewModel;
        _publishPackage = publishPackage;

        _gameId = Field("Videojuego", viewModel.GameId ?? string.Empty, "gameId");
        _packageId = Field("Identificador del paquete", viewModel.PackageId, "packageId");
        _version = Field("Versión", viewModel.Version, "version");
        _sizeBytes = Field("Tamaño en bytes", viewModel.SizeBytes.ToString(
            System.Globalization.CultureInfo.InvariantCulture), "sizeBytes");
        _launchExecutablePath = Field("Ruta del ejecutable", viewModel.LaunchExecutablePath, null);
        _launchArguments = Field("Argumentos", viewModel.LaunchArguments, null);
        _launchWorkingDirectory = Field("Directorio de trabajo", viewModel.LaunchWorkingDirectory, null);

        _sizeValue = new TextBlock
        {
            FontFamily = MonospaceFontFamily,
            FontSize = MonospaceFontSize,
            Text = ViewModel.SizeValue,
        };

        _status = new TextBlock { TextWrapping = TextWrapping.Wrap };

        var publish = new Button { Content = "Publicar paquete" };

        publish.Click += async (_, _) => await PublishAsync().ConfigureAwait(true);

        var main = new StackPanel
        {
            Margin = new Thickness(12),
            Spacing = 8,
            Children =
            {
                new TextBlock
                {
                    Text = "Publicar paquete",
                    FontSize = 22,
                    FontWeight = FontWeight.SemiBold,
                },
                _gameId,
                _packageId,
                _version,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 4,
                    Children =
                    {
                        _sizeValue,
                        new TextBlock
                        {
                            Text = ViewModel.SizeUnit,
                            FontFamily = MetaFontFamily,
                            FontSize = MetaFontSize,
                        },
                    },
                },
                _sizeBytes,
                _launchExecutablePath,
                _launchArguments,
                _launchWorkingDirectory,
                publish,
                _status,
            },
        };

        Content = new ScrollViewer { Content = main };
    }

    private readonly PublishPackage _publishPackage;

    /// <summary>View model of the form.</summary>
    public PackageFormViewModel ViewModel { get; }

    /// <summary>
    /// The descriptor of the package that has been published, or <c>null</c> when the
    /// publication has not succeeded.
    /// </summary>
    public PackageDescriptor? PublishedPackage { get; private set; }

    /// <summary>
    /// Reads the fields of the form and hands the package to the use case of the
    /// publication. The rejection is reflected in the form, and when the block names a
    /// concrete field, the form says which one (AC1).
    /// </summary>
    /// <returns>A task that completes when the publication has been attempted.</returns>
    private async Task PublishAsync()
    {
        ViewModel.GameId = _gameId.Text ?? string.Empty;
        ViewModel.PackageId = _packageId.Text ?? string.Empty;
        ViewModel.Version = _version.Text ?? string.Empty;
        ViewModel.SizeBytes = ParseLong(_sizeBytes.Text);
        ViewModel.LaunchExecutablePath = _launchExecutablePath.Text ?? string.Empty;
        ViewModel.LaunchArguments = _launchArguments.Text ?? string.Empty;
        ViewModel.LaunchWorkingDirectory = _launchWorkingDirectory.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(ViewModel.PackageId))
        {
            ViewModel.PackageId = Guid.CreateVersion7().ToString("N");
            _packageId.Text = ViewModel.PackageId;
        }

        _sizeValue.Text = ViewModel.SizeValue;

        try
        {
            var descriptor = await _publishPackage
                .ExecuteAsync(
                    ViewModel.GameId ?? string.Empty,
                    ViewModel.PackageId,
                    ViewModel.Version,
                    ViewModel.SizeBytes,
                    null,
                    ViewModel.ToLaunchSettings(),
                    ViewModel.ToOperations(),
                    ViewModel.ToManifestIdentity(),
                    CancellationToken.None)
                .ConfigureAwait(true);

            PublishedPackage = descriptor;

            ViewModel.ClearBlock();

            _status.Text =
                $"Paquete publicado con el identificador {descriptor.PackageId} y la versión {descriptor.Version}.";
        }
        catch (PackageLicenseMissingException exception)
        {
            // El bloqueo dice cuál de los dos campos falta, y no un error genérico (AC1).
            ViewModel.BlockFor(exception.FieldName);

            _status.Text =
                $"No se puede publicar: falta la licencia del videogame. Rellena el campo '{exception.FieldName}'.";
        }
        catch (PackageDistributionRestrictionsMissingException exception)
        {
            ViewModel.BlockFor(exception.FieldName);

            _status.Text =
                $"No se puede publicar: faltan las restricciones de distribución del videogame. "
                + $"Rellena el campo '{exception.FieldName}'.";
        }
        catch (PackageNotFoundException exception)
        {
            _status.Text = $"No se puede publicar: el videogame '{exception.GameId}' no está en el catálogo.";
        }
        catch (PackageAlreadyPublishedException exception)
        {
            _status.Text =
                $"No se puede publicar: el paquete '{exception.PackageId}' ya está publicado. "
                + "Un paquete publicado no cambia: publica una versión nueva.";
        }
        catch (PackageContentMismatchException exception)
        {
            _status.Text = $"No se puede publicar: {exception.Message}";
        }
        catch (ManifestOperationNotAllowedException exception)
        {
            _status.Text =
                $"No se puede publicar: la operación '{exception.OperationName}' no es una operación permitida.";
        }
        catch (PackageManifestMismatchException exception)
        {
            // El manifiesto y el descriptor tienen que declarar la misma publicación, y
            // el campo que no coincide se nombra (AC7).
            _status.Text =
                $"No se puede publicar: el manifiesto y el descriptor no coinciden en '{exception.FieldName}'. "
                + $"El descriptor declara '{exception.Expected}' y el manifiesto declara '{exception.Actual}'.";
        }
        catch (SchemaVersionNotSupportedException exception)
        {
            _status.Text =
                $"No se puede publicar: el manifiesto declara la versión de esquema {exception.SchemaVersion} "
                + "y este servidor no la soporta.";
        }
        catch (Exception exception)
        {
            // La excepción nunca llega a la interfaz como una traza: la organizadora ve
            // una línea y el operador encuentra el detalle en el registro (AD-15).
            _status.Text = $"No se pudo publicar el paquete: {exception.Message}";
        }
    }

    /// <summary>
    /// Builds a field of the form, with its label and the mark of the obligation when the
    /// publication demands it (AC1, AC4).
    /// </summary>
    /// <param name="label">Label of the field.</param>
    /// <param name="value">Value with which the field starts.</param>
    /// <param name="fieldName">Name of the field, when it is obligatory.</param>
    /// <returns>The control of the field.</returns>
    private static TextBox Field(string label, string value, string? fieldName)
    {
        var box = new TextBox
        {
            PlaceholderText = label,
            Text = value,
        };

        if (fieldName is null)
        {
            return box;
        }

        ToolTip.SetTip(box, PackageFormViewModel.LabelFor(label, fieldName));

        return box;
    }

    /// <summary>
    /// Reads a number of exact bytes from the text of the form. A text that is not a
    /// number leaves the value at zero, and the domain rejects it naming the field
    /// (AD-1).
    /// </summary>
    /// <param name="text">Text of the field.</param>
    /// <returns>The number of bytes.</returns>
    private static long ParseLong(string? text) =>
        long.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
}
