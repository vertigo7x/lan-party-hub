using LANPartyHub.Client.Presentation.Catalog;
using LANPartyHub.Client.Presentation.Design;
using LANPartyHub.Client.Presentation.Formatting;
using LANPartyHub.Shared.Contracts;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The ficha del videojuego (AC1, AC2, AC3, AC4, FR-2, UX-DR-2, UX-DR-5, AR-8).
///
/// <para>
/// The tests fix the behaviour of the ficha: it shows the EXACT values of the videogame,
/// the size is expressed in decimal gigabytes with the Spanish decimal separator, and the
/// text of the badge of the view state comes EXCLUSIVELY from the single table of
/// microcopy.
/// </para>
/// </summary>
public sealed class GameDetailViewModelTests
{
    private const string GameId = "0193f2a1b2c34d4e5f60718293a4b5c6";

    [Fact]
    public void The_ficha_shows_the_exact_values_of_the_videogame()
    {
        var viewModel = new GameDetailViewModel(CreateGame(), CatalogViewState.Available);

        Assert.Equal("Counter-Strike 2", viewModel.Name);
        Assert.Equal("1.41.3", viewModel.Version);
        Assert.Equal("Windows", viewModel.OperatingSystem);
        Assert.Equal("Propietaria", viewModel.License);
        Assert.Equal(["DirectX 12"], viewModel.Requirements);
        Assert.Equal("DirectX 12", viewModel.RequirementsText);
        Assert.Equal(1, viewModel.MinPlayers);
        Assert.Equal(10, viewModel.MaxPlayers);
        Assert.Equal("1 - 10", viewModel.PlayersText);
    }

    [Fact]
    public void The_size_of_the_ficha_is_in_decimal_gigabytes_with_the_spanish_comma()
    {
        var viewModel = new GameDetailViewModel(CreateGame(), CatalogViewState.Available);

        // El valor va en el token de tamaño y la unidad en el token de meta, de modo que
        // los dos tienen que poder separarse (AC2, UX-DR-5).
        Assert.Equal("7,6", viewModel.SizeValue);
        Assert.Equal("GB", viewModel.SizeUnit);
    }

    [Fact]
    public void The_size_of_the_ficha_never_carries_two_decimals()
    {
        var viewModel = new GameDetailViewModel(CreateGame(), CatalogViewState.Available);

        Assert.Equal(1, viewModel.SizeValue.Count(character => character == ','));
        Assert.DoesNotContain(".", viewModel.SizeValue, StringComparison.Ordinal);
    }

    [Fact]
    public void The_size_of_the_client_is_the_same_that_the_server_shows()
    {
        // AD-18 limita Shared a los contratos, así que la regla del tamaño vive en las dos
        // capas de presentación: las dos tienen que decir lo mismo (AC2).
        Assert.Equal(
            LANPartyHub.Server.Presentation.Formatting.SizeFormatter.FormatValue(7_600_000_000),
            SizeFormatter.FormatValue(7_600_000_000));

        Assert.Equal(
            LANPartyHub.Server.Presentation.Formatting.SizeFormatter.Format(7_600_000_000),
            SizeFormatter.Format(7_600_000_000));
    }

    [Fact]
    public void The_text_of_the_badge_of_the_synchronizing_state_comes_from_the_table()
    {
        var viewModel = new GameDetailViewModel(CreateGame(), CatalogViewState.Synchronizing);

        Assert.Equal("sincronizando", viewModel.ViewStateText);
        Assert.Equal(StateDisplayMap.GetText(CatalogViewState.Synchronizing), viewModel.ViewStateText);
    }

    [Fact]
    public void The_text_of_the_badge_of_the_available_state_comes_from_the_table()
    {
        var viewModel = new GameDetailViewModel(CreateGame(), CatalogViewState.Available);

        Assert.Equal("disponible", viewModel.ViewStateText);
        Assert.Equal(StateDisplayMap.GetText(CatalogViewState.Available), viewModel.ViewStateText);
    }

    [Fact]
    public void The_text_of_the_badge_of_the_outdated_state_comes_from_the_table()
    {
        var viewModel = new GameDetailViewModel(CreateGame(), CatalogViewState.InstalledOutdatedVersion);

        Assert.Equal("instalado con versión desactualizada", viewModel.ViewStateText);
        Assert.Equal(
            StateDisplayMap.GetText(CatalogViewState.InstalledOutdatedVersion),
            viewModel.ViewStateText);
    }

    [Fact]
    public void The_ficha_shows_the_installed_version_when_the_client_has_one()
    {
        var viewModel = new GameDetailViewModel(
            CreateGame(),
            CatalogViewState.InstalledOutdatedVersion,
            "1.9.0");

        Assert.Equal("1.9.0", viewModel.InstalledVersion);
        Assert.True(viewModel.HasOutdatedInstallation);
    }

    [Fact]
    public void The_ficha_has_no_installed_version_when_the_client_has_none()
    {
        var viewModel = new GameDetailViewModel(CreateGame(), CatalogViewState.Available);

        Assert.Null(viewModel.InstalledVersion);
        Assert.False(viewModel.HasOutdatedInstallation);
    }

    [Fact]
    public void The_view_of_the_ficha_defines_no_text_of_state()
    {
        // AC4, AR-8, R-MVP-7: el texto de cada insignia sale EXCLUSIVAMENTE de la tabla
        // de microcopy y la vista no define ninguno.
        var source = File.ReadAllText(FindSourceFile("GameDetailView.cs"));

        foreach (var state in Enum.GetValues<CatalogViewState>())
        {
            var literal = StateDisplayMap.GetText(state);

            Assert.DoesNotContain(literal, source, StringComparison.Ordinal);
        }

        foreach (var state in Enum.GetValues<InstallationState>())
        {
            var literal = StateDisplayMap.GetText(state);

            Assert.DoesNotContain(literal, source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_line_of_the_installed_version_uses_the_text_of_the_microcopy_table()
    {
        // La línea de la versión instalada dice «Instalado», que es el texto de
        // InstallationState.Installed en la tabla de microcopy (AC4, AR-8).
        var viewModel = new GameDetailViewModel(
            CreateGame(),
            CatalogViewState.InstalledOutdatedVersion,
            "1.9.0");

        Assert.Equal("Instalado: 1.9.0", viewModel.InstalledVersionText);
        Assert.StartsWith(
            StateDisplayMap.GetText(InstallationState.Installed),
            viewModel.InstalledVersionText,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_line_of_the_installed_version_does_not_exist_without_an_installation()
    {
        var viewModel = new GameDetailViewModel(CreateGame(), CatalogViewState.Available);

        Assert.Null(viewModel.InstalledVersionText);
    }

    [Fact]
    public void The_size_goes_in_the_token_of_sizes_and_the_unit_in_the_token_of_metadata()
    {
        // AC2, UX-DR-5: el valor numérico del tamaño va en {typography.size} y la unidad
        // «GB» en {typography.meta}.
        Assert.Equal(12, DesignTokens.SizeFontSize);
        Assert.Equal(11, DesignTokens.MetaFontSize);
        Assert.Equal(DesignTokens.SizeFontSize, DesignTokens.SizeFontSize);
        Assert.Equal("Cascadia Mono", DesignTokens.MonoFontFamily);
    }

    [Fact]
    public void Each_badge_color_carries_its_canonical_meaning()
    {
        // El azul de progreso es «sincronizando», el verde de la LAN es «disponible» y el
        // ámbar de la advertencia es «instalado con versión desactualizada» (AC3, DESIGN).
        Assert.Equal(
            DesignTokens.StateProgress,
            DesignTokens.BadgeColorFor(CatalogViewState.Synchronizing));

        Assert.Equal(
            DesignTokens.StateSuccess,
            DesignTokens.BadgeColorFor(CatalogViewState.Available));

        Assert.Equal(
            DesignTokens.StateWarning,
            DesignTokens.BadgeColorFor(CatalogViewState.InstalledOutdatedVersion));
    }

    /// <summary>
    /// Finds the source file of the presentation layer of the client.
    /// </summary>
    /// <param name="fileName">Name of the file.</param>
    /// <returns>The path of the file.</returns>
    private static string FindSourceFile(string fileName)
    {
        var root = RepositoryLayout.Root;

        var path = Path.Combine(root, "src", "Client", "Presentation", "Catalog", fileName);

        Assert.True(File.Exists(path), $"{fileName} does not exist at {path}.");

        return path;
    }

    /// <summary>
    /// Builds the contract of a videogame of the catalog.
    /// </summary>
    /// <returns>The contract of the videogame.</returns>
    private static GameDescriptor CreateGame() => new(
        GameId,
        "Counter-Strike 2",
        "Disparadores en primera persona.",
        ["Shooter", "Multijugador"],
        ["fps"],
        "cover.png",
        ["captura-1.png"],
        "1.41.3",
        7_600_000_000,
        1,
        10,
        "Windows",
        "Servidor",
        "Portable",
        new InstallationManifest.LaunchSettings("bin\\cs2.exe", ["-novid"], "game\\bin"),
        ["DirectX 12"],
        "Propietaria",
        "Solo para el evento");
}
