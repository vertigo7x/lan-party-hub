using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using LANPartyHub.Server.Presentation.Catalog;

namespace LANPartyHub.Server.Presentation.Trust;

/// <summary>
/// Panel of the ancla de confianza del evento (AC1, AD-27).
///
/// <para>
/// AD-27: el panel muestra el fingerprint del ancla EN PANTALLA, agrupado en bloques de 4
/// caracteres, y lo exporta como cÃ³digo QR que lleva el fingerprint y la direcciÃ³n del
/// servidor. El organizador puede imprimir la hoja o exportarla para repartirla por un
/// canal que no pase por la LAN (AC1, AD-27).
/// </para>
/// <para>
/// La exportaciÃ³n escribe un fichero que el organizador elige, asÃ­ que el fingerprint sale
/// de la mÃ¡quina del servidor por una decisiÃ³n explÃ­cita y no por un efecto secundario
/// (AD-27).
/// </para>
/// <para>
/// El panel se construye en cÃ³digo, como el resto de la interfaz del producto: el
/// repositorio no tiene ficheros XAML (AD-1).
/// </para>
/// </summary>
public sealed class TrustAnchorPanel : UserControl
{
    /// <summary>
    /// Name of the file that the exporter writes by default, without the extension
    /// (AD-27).
    /// </summary>
    public const string DefaultFileName = "ancla-de-confianza";

    private readonly TrustAnchorViewModel _viewModel;
    private readonly TextBlock _fingerprint;
    private readonly TextBlock _address;
    private readonly TextBlock _status;
    private readonly Image _qrCode;

    /// <summary>
    /// Creates the panel of the ancla de confianza.
    /// </summary>
    /// <param name="viewModel">View model of the panel.</param>
    public TrustAnchorPanel(TrustAnchorViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        _viewModel = viewModel;

        _fingerprint = new TextBlock
        {
            FontFamily = GameFormView.MonospaceFontFamily,
            FontSize = GameFormView.MonospaceFontSize,
            Text = viewModel.GroupedFingerprint,
            TextWrapping = TextWrapping.Wrap,
        };

        _address = new TextBlock
        {
            FontFamily = GameFormView.MonospaceFontFamily,
            FontSize = GameFormView.MonospaceFontSize,
            Text = viewModel.ServerAddress,
        };

        _status = new TextBlock { TextWrapping = TextWrapping.Wrap };

        _qrCode = new Image
        {
            Stretch = Stretch.Uniform,
            Width = 220,
            Height = 220,
        };

        LoadQrCode();

        var exportSvg = new Button { Content = "Exportar QR (SVG)" };
        var exportPng = new Button { Content = "Exportar QR (PNG)" };
        var exportPrintable = new Button { Content = "Imprimir hoja del ancla" };

        exportSvg.Click += async (_, _) => await ExportAsync("svg").ConfigureAwait(true);
        exportPng.Click += async (_, _) => await ExportAsync("png").ConfigureAwait(true);
        exportPrintable.Click += async (_, _) => await ExportPrintableSheetAsync().ConfigureAwait(true);

        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = "Ancla de confianza",
                    FontSize = 22,
                    FontWeight = FontWeight.SemiBold,
                },
                new TextBlock
                {
                    Text = viewModel.InstructionsText,
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock { Text = "Fingerprint" },
                _fingerprint,
                new TextBlock { Text = "DirecciÃ³n del servidor" },
                _address,
                _qrCode,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        exportSvg,
                        exportPng,
                        exportPrintable,
                    },
                },
                _status,
            },
        };
    }

    /// <summary>
    /// The fingerprint that the panel shows, grouped in blocks of 4 characters (AD-27).
    /// </summary>
    public string GroupedFingerprint => _fingerprint.Text ?? string.Empty;

    /// <summary>
    /// The image of the cÃ³digo QR that the panel shows (AC1, AD-27).
    /// </summary>
    public Image QrCode => _qrCode;

    /// <summary>
    /// The text of the status of the panel, after an export (AD-27).
    /// </summary>
    public string StatusText => _status.Text ?? string.Empty;

    /// <summary>
    /// Loads the image of the cÃ³digo QR from the bytes that the exporter produces. A
    /// failure NEVER takes the panel down: el fingerprint en texto sigue visible y el
    /// organizador puede repartirlo igual (AD-27).
    /// </summary>
    private void LoadQrCode()
    {
        try
        {
            using var stream = new MemoryStream(
                TrustAnchorQrExporter.ExportPng(_viewModel.Fingerprint, _viewModel.ServerAddress));

            _qrCode.Source = new Bitmap(stream);
        }
        catch (Exception)
        {
            // El QR es un complemento del texto: el fingerprint sigue en pantalla y el
            // organizador puede repartirlo por el canal que sea (AD-27).
            _qrCode.Source = null;
        }
    }

    /// <summary>
    /// Exports the cÃ³digo QR to a file that the organizer chooses (AD-27).
    /// </summary>
    /// <param name="extension">Extension of the file: <c>svg</c> or <c>png</c>.</param>
    /// <returns>A task that completes when the export has been attempted.</returns>
    private async Task ExportAsync(string extension)
    {
        try
        {
            var storage = TopLevel.GetTopLevel(this)?.StorageProvider;

            if (storage is null)
            {
                _status.Text = "Este equipo no puede elegir la carpeta de destino del fichero.";

                return;
            }

            var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Exportar el cÃ³digo QR del ancla de confianza",
                SuggestedFileName = $"{DefaultFileName}.{extension}",
                DefaultExtension = extension,
                FileTypeChoices = new[]
                {
                    extension == "svg"
                        ? new FilePickerFileType("Vector SVG") { Patterns = new[] { "*.svg" } }
                        : new FilePickerFileType("Imagen PNG") { Patterns = new[] { "*.png" } },
                },
            }).ConfigureAwait(true);

            if (file is null)
            {
                _status.Text = "No se exportÃ³ el cÃ³digo QR.";

                return;
            }

            await using var stream = await file.OpenWriteAsync().ConfigureAwait(true);

            if (extension == "svg")
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(
                    TrustAnchorQrExporter.ExportSvg(_viewModel.Fingerprint, _viewModel.ServerAddress));

                await stream.WriteAsync(bytes).ConfigureAwait(true);
            }
            else
            {
                var bytes = TrustAnchorQrExporter.ExportPng(_viewModel.Fingerprint, _viewModel.ServerAddress);

                await stream.WriteAsync(bytes).ConfigureAwait(true);
            }

            _status.Text = $"CÃ³digo QR exportado a {file.Name}.";
        }
        catch (Exception exception)
        {
            // La excepciÃ³n nunca llega a la interfaz como una traza: el organizador ve una
            // lÃ­nea y el detalle queda en el log (AD-15).
            _status.Text = $"No se pudo exportar el cÃ³digo QR: {exception.Message}";
        }
    }

    /// <summary>
    /// Exports the hoja imprimible del ancla de confianza: el fingerprint agrupado, la
    /// direcciÃ³n del servidor y el cÃ³digo QR, listos para el papel de la mesa de
    /// registro (AC1, AD-27).
    /// </summary>
    /// <returns>A task that completes when the export has been attempted.</returns>
    private async Task ExportPrintableSheetAsync()
    {
        try
        {
            var storage = TopLevel.GetTopLevel(this)?.StorageProvider;

            if (storage is null)
            {
                _status.Text = "Este equipo no puede elegir la carpeta de destino del fichero.";

                return;
            }

            var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Exportar la hoja imprimible del ancla de confianza",
                SuggestedFileName = $"{TrustAnchorQrExporter.PrintableFileName}.svg",
                DefaultExtension = "svg",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("Vector SVG") { Patterns = new[] { "*.svg" } },
                },
            }).ConfigureAwait(true);

            if (file is null)
            {
                _status.Text = "No se exportÃ³ la hoja imprimible.";

                return;
            }

            var sheet = TrustAnchorQrExporter.ExportPrintableSheet(
                _viewModel.Fingerprint,
                _viewModel.ServerAddress);

            await using var stream = await file.OpenWriteAsync().ConfigureAwait(true);

            await stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(sheet)).ConfigureAwait(true);

            _status.Text = $"Hoja del ancla exportada a {file.Name}. Ãbrela e imprÃ­mela para la mesa de registro.";
        }
        catch (Exception exception)
        {
            _status.Text = $"No se pudo exportar la hoja del ancla: {exception.Message}";
        }
    }
}
