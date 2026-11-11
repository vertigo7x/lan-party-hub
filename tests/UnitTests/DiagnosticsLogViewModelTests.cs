using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using LANPartyHub.Client.Application;
using LANPartyHub.Client.Presentation.Design;
using LANPartyHub.Client.Presentation.Diagnostics;
using LANPartyHub.Shared.Contracts;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The VISOR de diagnóstico del cliente (AC1, AC2, AC3, AC4, NFR-7, UX-DR-41).
///
/// <para>
/// El visor muestra el registro de errores persistidos de su equipo, con el código
/// estable, el mensaje, la causa y el siguiente paso, y con la asociación a la descarga,
/// la instalación o el lanzamiento que los produjo (AC1, AC2).
/// </para>
/// <para>
/// El visor correlaciona con el log del servidor por <c>operationId</c> y <c>clientId</c>,
/// así que un error del servidor y su correspondiente en el cliente se pueden UNIR
/// (AC3, AD-23, AR-20).
/// </para>
/// <para>
/// AC4: el visor dispone de VARIANTE CLARA con los tokens <c>-light</c> de
/// <c>DESIGN.md</c>, y el modo OSCURO es el PREDETERMINADO (UX-DR-41, UX-DR-1).
/// </para>
/// <para>
/// La clase vive en la COLECCIÓN de las superficies de Avalonia, que NO corre en
/// paralelo: la plataforma headless tiene UN dispatcher con un hilo propio
/// (UX-DR-37).
/// </para>
/// </summary>
[Collection(AvaloniaSurfaceCollection.Name)]
public sealed class DiagnosticsLogViewModelTests
{
    private const string ClientId = "0193f2a1b2c34d4e5f60718293a4b5c6";
    private const string OtherClientId = "0193f2a1b2c34d4e5f60718293a4b5c7";
    private const string DownloadOperationId = "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e70";
    private const string InstallationOperationId = "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e71";
    private const string LaunchOperationId = "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e72";

    /// <summary>
    /// AC1: la entrada del registro lleva sus CUATRO PARTES, así que el equipo sabe qué
    /// falló, por qué y qué hacer ahora (AD-15, UX-DR-36).
    /// </summary>
    [AvaloniaFact]
    public void The_entry_carries_the_four_parts_of_the_error()
    {
        var error = ErrorCatalog.Resolve(ErrorCatalog.PackageIntegrityMismatchCode);

        var entry = new DiagnosticsLogEntry(
            error,
            ClientId,
            DownloadOperationId,
            OperationTypeNames.Download,
            new DateTimeOffset(2026, 11, 11, 8, 15, 0, TimeSpan.Zero));

        // AD-15, AD-25: las tres partes salen del CATÁLOGO y el código estable es el
        // identificador interno del error, nunca el texto principal.
        Assert.Equal(error.Code, entry.Code);
        Assert.Equal(error.Message, entry.Message);
        Assert.Equal(error.Cause, entry.Cause);
        Assert.Equal(error.NextStep, entry.NextStep);
    }

    /// <summary>
    /// AC2: el error queda ASOCIADO a la descarga, la instalación o el lanzamiento que lo
    /// produjo (NFR-7).
    /// </summary>
    [AvaloniaFact]
    public void The_entry_is_associated_to_the_kind_of_operation_that_produced_it()
    {
        var download = Entry(
            ErrorCatalog.PackageIntegrityMismatchCode,
            DownloadOperationId,
            OperationTypeNames.Download);

        var installation = Entry(
            ErrorCatalog.InstallOperationFailedCode,
            InstallationOperationId,
            OperationTypeNames.Installation);

        var launch = Entry(
            ErrorCatalog.GameLaunchExecutableMissingCode,
            LaunchOperationId,
            OperationTypeNames.Launch);

        Assert.Equal(OperationTypeNames.Download, download.OperationType);
        Assert.Equal(OperationTypeNames.Installation, installation.OperationType);
        Assert.Equal(OperationTypeNames.Launch, launch.OperationType);
    }

    /// <summary>
    /// AC3: el visor FILTRA por <c>operationId</c>, así que el error de una unidad de
    /// trabajo se aísla del resto (AD-23).
    /// </summary>
    [AvaloniaFact]
    public void The_view_model_filters_by_the_operation_identifier()
    {
        var viewModel = new DiagnosticsLogViewModel(
        [
            Entry(ErrorCatalog.PackageIntegrityMismatchCode, DownloadOperationId, OperationTypeNames.Download),
            Entry(ErrorCatalog.InstallOperationFailedCode, InstallationOperationId, OperationTypeNames.Installation),
        ]);

        viewModel.OperationIdFilter = DownloadOperationId;

        var entry = Assert.Single(viewModel.Entries);

        Assert.Equal(DownloadOperationId, entry.OperationId);
    }

    /// <summary>
    /// AC3: el visor FILTRA por <c>clientId</c>, así que el log del cliente se puede
    /// aislar por equipo (AD-23).
    /// </summary>
    [AvaloniaFact]
    public void The_view_model_filters_by_the_client_identifier()
    {
        var viewModel = new DiagnosticsLogViewModel(
        [
            new DiagnosticsLogEntry(
                ErrorCatalog.Resolve(ErrorCatalog.PackageIntegrityMismatchCode),
                ClientId,
                DownloadOperationId,
                OperationTypeNames.Download,
                new DateTimeOffset(2026, 11, 11, 8, 15, 0, TimeSpan.Zero)),
            new DiagnosticsLogEntry(
                ErrorCatalog.Resolve(ErrorCatalog.PackageIntegrityMismatchCode),
                OtherClientId,
                DownloadOperationId,
                OperationTypeNames.Download,
                new DateTimeOffset(2026, 11, 11, 8, 16, 0, TimeSpan.Zero)),
        ]);

        viewModel.ClientIdFilter = ClientId;

        var entry = Assert.Single(viewModel.Entries);

        Assert.Equal(ClientId, entry.ClientId);
    }

    /// <summary>
    /// AC2: el visor FILTRA por el TIPO de la operación, así que el equipo puede aislar
    /// los errores de las descargas de los de las instalaciones (NFR-7).
    /// </summary>
    [AvaloniaFact]
    public void The_view_model_filters_by_the_kind_of_operation()
    {
        var viewModel = new DiagnosticsLogViewModel(
        [
            Entry(ErrorCatalog.PackageIntegrityMismatchCode, DownloadOperationId, OperationTypeNames.Download),
            Entry(ErrorCatalog.InstallOperationFailedCode, InstallationOperationId, OperationTypeNames.Installation),
        ]);

        viewModel.OperationTypeFilter = OperationTypeNames.Installation;

        var entry = Assert.Single(viewModel.Entries);

        Assert.Equal(OperationTypeNames.Installation, entry.OperationType);
    }

    /// <summary>
    /// Un filtro VACÍO deja pasar todas las entradas, así que el visor muestra el
    /// registro completo cuando el jugador no ha filtrado nada (AC1).
    /// </summary>
    [AvaloniaFact]
    public void An_empty_filter_shows_every_entry()
    {
        var viewModel = new DiagnosticsLogViewModel(
        [
            Entry(ErrorCatalog.PackageIntegrityMismatchCode, DownloadOperationId, OperationTypeNames.Download),
            Entry(ErrorCatalog.InstallOperationFailedCode, InstallationOperationId, OperationTypeNames.Installation),
        ]);

        Assert.Equal(2, viewModel.Count);
        Assert.False(viewModel.IsEmpty);
    }

    /// <summary>
    /// AC4: el visor tiene VARIANTE CLARA con los tokens <c>-light</c> de
    /// <c>DESIGN.md</c> (UX-DR-41).
    /// </summary>
    [AvaloniaFact]
    public void The_view_model_offers_the_light_variant_of_the_design_tokens()
    {
        var viewModel = new DiagnosticsLogViewModel();

        viewModel.UseLightVariant();

        Assert.True(viewModel.IsLightVariant);

        // UX-DR-41: la variante clara usa los tokens `-light` de `DESIGN.md`.
        Assert.Equal(DesignTokens.SurfaceBaseLight, viewModel.Background);
        Assert.Equal(DesignTokens.SurfaceRaisedLight, DesignTokens.SurfaceRaisedLight);
        Assert.Equal(DesignTokens.InkPrimaryLight, viewModel.PrimaryInk);
        Assert.Equal(DesignTokens.InkSecondaryLight, viewModel.SecondaryInk);
        Assert.Equal(DesignTokens.BorderHairlineLight, viewModel.HairlineBorder);
        Assert.Equal(DesignTokens.StateDangerLight, viewModel.Danger);
    }

    /// <summary>
    /// AC4, UX-DR-1: el modo OSCURO es el PREDETERMINADO, así que el visor no abre en
    /// claro (UX-DR-41).
    /// </summary>
    [AvaloniaFact]
    public void The_dark_variant_is_the_default_one()
    {
        var viewModel = new DiagnosticsLogViewModel();

        Assert.False(viewModel.IsLightVariant);

        // El modo oscuro usa los tokens oscuros, no los `-light`.
        Assert.Equal(DesignTokens.SurfaceBase, viewModel.Background);
        Assert.Equal(DesignTokens.InkPrimary, viewModel.PrimaryInk);
        Assert.Equal(DesignTokens.InkSecondary, viewModel.SecondaryInk);
        Assert.Equal(DesignTokens.BorderHairline, viewModel.HairlineBorder);
        Assert.Equal(DesignTokens.StateDanger, viewModel.Danger);
    }

    /// <summary>
    /// El cliente NO declara una variante clara GLOBAL: el modo oscuro es el
    /// predeterminado del cliente y la variante clara existe para las pocas superficies
    /// que la necesitan (DESIGN, UX-DR-41).
    /// </summary>
    [AvaloniaFact]
    public void The_design_tokens_declare_the_dark_variant_as_the_default_one()
    {
        Assert.True(DesignTokens.IsDarkThemeTheDefault);
    }

    /// <summary>
    /// La VISTA del visor muestra una fila por entrada del registro, con sus cuatro
    /// partes y con su asociación a la operación que la produjo (AC1, AC2).
    /// </summary>
    [AvaloniaFact]
    public async Task The_view_shows_one_row_per_registered_error()
    {
        var viewModel = new DiagnosticsLogViewModel(
        [
            Entry(ErrorCatalog.PackageIntegrityMismatchCode, DownloadOperationId, OperationTypeNames.Download),
            Entry(ErrorCatalog.InstallOperationFailedCode, InstallationOperationId, OperationTypeNames.Installation),
        ]);

        var view = new DiagnosticsLogView(viewModel);

        await OpenAsync(view);

        Assert.Equal(2, view.Entries.Children.Count);

        var texts = view.Entries.Children
            .OfType<Border>()
            .SelectMany(border => border.GetVisualDescendants().OfType<TextBlock>())
            .Select(text => text.Text)
            .ToArray();

        var error = ErrorCatalog.Resolve(ErrorCatalog.PackageIntegrityMismatchCode);

        // AD-15, UX-DR-36: la fila lleva el código, el mensaje, la causa y el siguiente
        // paso, así que el equipo sabe qué hacer.
        Assert.Contains(error.Code, texts);
        Assert.Contains(error.Message, texts);
        Assert.Contains(error.Cause, texts);
        Assert.Contains(error.NextStep, texts);

        // AC2, AC3: la fila lleva el tipo de la operación y los dos identificadores que
        // la correlacionan con el log del servidor.
        Assert.Contains(texts, text =>
            text.Contains(OperationTypeNames.Download, StringComparison.Ordinal)
            && text.Contains(DownloadOperationId, StringComparison.Ordinal)
            && text.Contains(ClientId, StringComparison.Ordinal));
    }

    /// <summary>
    /// El filtro del visor SEGURO al registro, así que la vista muestra solo lo que pasa
    /// el filtro (AC2, AC3).
    /// </summary>
    [AvaloniaFact]
    public async Task The_view_applies_the_filter_to_the_registered_entries()
    {
        var viewModel = new DiagnosticsLogViewModel(
        [
            Entry(ErrorCatalog.PackageIntegrityMismatchCode, DownloadOperationId, OperationTypeNames.Download),
            Entry(ErrorCatalog.InstallOperationFailedCode, InstallationOperationId, OperationTypeNames.Installation),
        ]);

        var view = new DiagnosticsLogView(viewModel);

        await OpenAsync(view);

        Assert.Equal(2, view.Entries.Children.Count);

        // AC2, AC3: el filtro del visor va al view model y la vista se repinta con lo que
        // pasa el filtro.
        viewModel.OperationIdFilter = InstallationOperationId;

        Assert.Single(view.Entries.Children);

        var entry = Assert.Single(viewModel.Entries);

        Assert.Equal(InstallationOperationId, entry.OperationId);
        Assert.Equal(OperationTypeNames.Installation, entry.OperationType);
    }

    /// <summary>
    /// La vista ofrece los TRES filtros del visor: por <c>operationId</c>, por
    /// <c>clientId</c> y por tipo de operación (AC2, AC3).
    /// </summary>
    [AvaloniaFact]
    public void The_view_offers_the_three_filters_of_the_viewer()
    {
        var view = new DiagnosticsLogView(new DiagnosticsLogViewModel());

        Assert.NotNull(view.OperationIdFilter);
        Assert.NotNull(view.ClientIdFilter);
        Assert.NotNull(view.OperationTypeFilter);
    }

    /// <summary>
    /// El estado vacío del visor dice que no hay errores registrados, así que la vista
    /// nunca deja un hueco sin explicación (AC1, UX-DR-28).
    /// </summary>
    [AvaloniaFact]
    public async Task The_view_shows_the_empty_state_when_there_are_no_registered_errors()
    {
        var viewModel = new DiagnosticsLogViewModel();

        var view = new DiagnosticsLogView(viewModel);

        await OpenAsync(view);

        Assert.True(view.EmptyText.IsVisible);
        Assert.Empty(view.Entries.Children);
    }

    /// <summary>
    /// Opens the visor in a window, así que sus filas existen en el árbol visual
    /// (AC1, UX-DR-37).
    /// </summary>
    /// <param name="view">View of the visor de diagnóstico.</param>
    /// <returns>A task that completes when the window has been opened.</returns>
    private static async Task OpenAsync(DiagnosticsLogView view)
    {
        var window = new Window { Content = view };

        window.Show();

        await Task.Yield();
    }

    /// <summary>
    /// Builds an entry of the register with the given error and association (AC1, AC2).
    /// </summary>
    /// <param name="code">Stable code of the error of the catalog.</param>
    /// <param name="operationId">Identifier of the operation.</param>
    /// <param name="operationType">Kind of operation that produced the error.</param>
    /// <returns>The entry of the register.</returns>
    private static DiagnosticsLogEntry Entry(
        string code,
        string operationId,
        string operationType) =>
        new(
            ErrorCatalog.Resolve(code),
            ClientId,
            operationId,
            operationType,
            new DateTimeOffset(2026, 11, 11, 8, 15, 0, TimeSpan.Zero));
}
