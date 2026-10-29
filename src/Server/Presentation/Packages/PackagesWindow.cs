using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LANPartyHub.Server.Application.Packages;
using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Server.Presentation.Packages;

/// <summary>
/// Window of the section of the packages of the event (AD-1, AC1).
///
/// <para>
/// The window has the shape that the design fixes for the server: a left column of
/// navigation with the catalog, the clients and the packages, and a main area that holds
/// the list of the published packages and the access to the form of the publication.
/// </para>
/// <para>
/// The window is built in code, like the whole interface of the product: the repository
/// has no XAML file (AD-1).
/// </para>
/// <para>
/// The list is filled with the packages that the read port of the repository returns, and
/// the button of the publication opens the form, which hands the package to the use case:
/// the server and its interface live in the same process (AD-1).
/// </para>
/// </summary>
public sealed class PackagesWindow : Window
{
    /// <summary>
    /// Font of the literals of the list: the identifier and the version of the package
    /// (AC4).
    /// </summary>
    public static readonly FontFamily MonospaceFontFamily = PackageFormView.MonospaceFontFamily;

    /// <summary>
    /// Font of the small text of the list (AC4).
    /// </summary>
    public static readonly FontFamily MetaFontFamily = PackageFormView.MetaFontFamily;

    private readonly PublishPackage _publishPackage;
    private readonly ListPackages _listPackages;
    private readonly StackPanel _rows;
    private readonly TextBlock _summary;
    private readonly TextBlock _status;

    /// <summary>
    /// Creates the window of the section of the packages.
    /// </summary>
    /// <param name="viewModel">View model of the section.</param>
    /// <param name="publishPackage">Use case of the publication of a package.</param>
    /// <param name="listPackages">Use case that reads the published packages.</param>
    public PackagesWindow(
        PackagesViewModel viewModel,
        PublishPackage publishPackage,
        ListPackages listPackages)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(publishPackage);
        ArgumentNullException.ThrowIfNull(listPackages);

        Title = "Paquetes";
        Width = 960;
        Height = 600;

        ViewModel = viewModel;

        _publishPackage = publishPackage;
        _listPackages = listPackages;

        var navigation = new StackPanel
        {
            Margin = new Thickness(12),
            Spacing = 8,
            Width = 200,
            Children =
            {
                new TextBlock
                {
                    Text = "LAN Party Hub",
                    FontSize = 22,
                    FontWeight = FontWeight.SemiBold,
                },
                new TextBlock { Text = "Catálogo" },
                new TextBlock { Text = "Clientes" },
                new TextBlock { Text = "Paquetes" },
            },
        };

        _rows = new StackPanel
        {
            Spacing = 8,
        };

        var publish = new Button { Content = "Publicar paquete" };
        var refresh = new Button { Content = "Actualizar paquetes" };
        _summary = new TextBlock
        {
            FontFamily = MetaFontFamily,
            FontSize = PackageFormView.MetaFontSize,
        };
        _status = new TextBlock { TextWrapping = TextWrapping.Wrap };

        publish.Click += async (_, _) => await OpenFormAsync().ConfigureAwait(true);
        refresh.Click += async (_, _) => await RefreshAsync().ConfigureAwait(true);

        var main = new StackPanel
        {
            Margin = new Thickness(12),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = "Paquetes",
                    FontSize = 22,
                    FontWeight = FontWeight.SemiBold,
                },
                _summary,
                new ScrollViewer
                {
                    Content = _rows,
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        publish,
                        refresh,
                    },
                },
                _status,
            },
        };

        Content = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(224) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            },
            Children =
            {
                navigation,
                main,
            },
        };

        Grid.SetColumn(navigation, 0);
        Grid.SetColumn(main, 1);

        // The list is read when the window opens, so the section never shows an empty
        // list while the database holds published packages.
        Opened += async (_, _) => await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>View model of the section.</summary>
    public PackagesViewModel ViewModel { get; }

    /// <summary>
    /// Reads the published packages and fills the list of the section with them.
    /// </summary>
    /// <returns>A task that completes when the list has been filled.</returns>
    public async Task RefreshAsync()
    {
        try
        {
            var packages = await _listPackages.ExecuteAsync(CancellationToken.None).ConfigureAwait(true);

            ViewModel.Replace(packages);

            RebuildRows();

            _summary.Text = $"{ViewModel.Count} paquetes publicados.";
        }
        catch (Exception exception)
        {
            // The exception never reaches the interface as a stack trace: the organizer
            // sees a line and the operator finds the detail in the log (AD-15).
            _status.Text = $"No se pudieron leer los paquetes: {exception.Message}";
        }
    }

    /// <summary>
    /// Opens the form of the publication. The form hands the package to the use case, and
    /// the list is refreshed when the package has been published.
    /// </summary>
    /// <returns>A task that completes when the form has been closed.</returns>
    private async Task OpenFormAsync()
    {
        var form = new PackageFormView(new PackageFormViewModel(), _publishPackage);

        await form.ShowDialog(this).ConfigureAwait(true);

        if (form.PublishedPackage is not null)
        {
            await RefreshAsync().ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Builds the rows of the list again, with the packages that the view model holds.
    /// </summary>
    private void RebuildRows()
    {
        _rows.Children.Clear();

        foreach (var row in ViewModel.Rows)
        {
            _rows.Children.Add(BuildRow(row));
        }
    }

    /// <summary>
    /// Builds the row of a published package of the list. The version and the identifier
    /// are the data that the organizer compares, so they go in a monospaced font and the
    /// size carries the numeric value and the unit apart (AC4).
    /// </summary>
    /// <param name="row">Row of the package.</param>
    /// <returns>The control of the row.</returns>
    private static Border BuildRow(PackageRow row)
    {
        var size = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children =
            {
                new TextBlock
                {
                    Text = row.SizeValue,
                    FontFamily = MonospaceFontFamily,
                    FontSize = PackageFormView.MonospaceFontSize,
                },
                new TextBlock
                {
                    Text = row.SizeUnit,
                    FontFamily = MetaFontFamily,
                    FontSize = PackageFormView.MetaFontSize,
                },
            },
        };

        var content = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = row.Version },
                new TextBlock
                {
                    Text = row.PackageId,
                    FontFamily = MonospaceFontFamily,
                    FontSize = PackageFormView.MonospaceFontSize,
                },
                size,
                new TextBlock
                {
                    Text = row.Sha256,
                    FontFamily = MonospaceFontFamily,
                    FontSize = PackageFormView.MetaFontSize,
                },
            },
        };

        return new Border
        {
            Padding = new Thickness(12),
            BorderThickness = new Thickness(0, 0, 0, 1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x28, 0x2C, 0x37, 0xFF)),
            Child = content,
        };
    }
}
