using LANPartyHub.Server.Presentation.Catalog;
using LANPartyHub.Server.Presentation.Formatting;
using LANPartyHub.Shared.Contracts;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The surface of presentation of the catalog of the organizer (AC1, AC4).
///
/// <para>
/// The form and the list of the section are the surface that the organizer uses, and they
/// have to be verified: the form builds the contract that the use cases receive out of
/// what the organizer types, and the list shows the size already derived.
/// </para>
/// </summary>
public sealed class CatalogPresentationTests
{
    private const string GameId = "0193f2a1b2c34d4e5f60718293a4b5c6";

    [Fact]
    public void The_text_of_a_collection_is_split_into_its_values()
    {
        var values = GameFormViewModel.SplitList("Shooter, Multijugador");

        Assert.Equal(["Shooter", "Multijugador"], values);
    }

    [Fact]
    public void The_values_of_a_collection_that_have_padding_are_trimmed()
    {
        var values = GameFormViewModel.SplitList("  Shooter  ,  Multijugador  ");

        Assert.Equal(["Shooter", "Multijugador"], values);
    }

    [Fact]
    public void The_text_of_a_collection_that_is_empty_has_no_values()
    {
        Assert.Empty(GameFormViewModel.SplitList(string.Empty));
        Assert.Empty(GameFormViewModel.SplitList("   "));
        Assert.Empty(GameFormViewModel.SplitList(null));
    }

    [Fact]
    public void The_values_of_a_collection_survive_the_way_back_to_the_text()
    {
        var values = new[] { "Shooter", "Multijugador" };

        var text = string.Join(", ", values);

        Assert.Equal(values, GameFormViewModel.SplitList(text));
    }

    [Fact]
    public void The_form_of_an_alta_has_no_identifier()
    {
        var viewModel = new GameFormViewModel();

        Assert.Null(viewModel.GameId);
        Assert.False(viewModel.IsModification);
    }

    [Fact]
    public void The_form_of_a_modificacion_carries_the_identifier_of_the_videogame()
    {
        var viewModel = new GameFormViewModel(CreateDescriptor());

        Assert.Equal(GameId, viewModel.GameId);
        Assert.True(viewModel.IsModification);
    }

    [Fact]
    public void The_form_builds_the_contract_with_the_eighteen_metadata_of_fr_1()
    {
        var viewModel = new GameFormViewModel
        {
            Name = "Counter-Strike 2",
            Description = "Disparadores en primera persona.",
            Categories = "Shooter, Multijugador",
            Tags = "fps, valve",
            CoverImage = "cover.png",
            Images = "captura-1.png, captura-2.png",
            Version = "1.41.3",
            SizeBytes = 7_600_000_000,
            MinPlayers = 1,
            MaxPlayers = 10,
            OperatingSystem = "Windows",
            DistributionType = "Servidor",
            InstallationType = "Portable",
            LaunchExecutablePath = "bin\\cs2.exe",
            LaunchArguments = "-novid, -fullscreen",
            LaunchWorkingDirectory = "game\\bin",
            Dependencies = "DirectX 12",
            License = "Propietaria",
            DistributionRestrictions = "Solo para el evento",
        };

        var descriptor = viewModel.ToDescriptor();

        Assert.Equal(string.Empty, descriptor.GameId);
        Assert.Equal("Counter-Strike 2", descriptor.Name);
        Assert.Equal("Disparadores en primera persona.", descriptor.Description);
        Assert.Equal(["Shooter", "Multijugador"], descriptor.Categories);
        Assert.Equal(["fps", "valve"], descriptor.Tags);
        Assert.Equal("cover.png", descriptor.CoverImage);
        Assert.Equal(["captura-1.png", "captura-2.png"], descriptor.Images);
        Assert.Equal("1.41.3", descriptor.Version);
        Assert.Equal(7_600_000_000, descriptor.SizeBytes);
        Assert.Equal(1, descriptor.MinPlayers);
        Assert.Equal(10, descriptor.MaxPlayers);
        Assert.Equal("Windows", descriptor.OperatingSystem);
        Assert.Equal("Servidor", descriptor.DistributionType);
        Assert.Equal("Portable", descriptor.InstallationType);
        Assert.Equal("bin\\cs2.exe", descriptor.LaunchConfiguration.ExecutablePath);
        Assert.Equal(["-novid", "-fullscreen"], descriptor.LaunchConfiguration.Arguments);
        Assert.Equal("game\\bin", descriptor.LaunchConfiguration.WorkingDirectory);
        Assert.Equal(["DirectX 12"], descriptor.Dependencies);
        Assert.Equal("Propietaria", descriptor.License);
        Assert.Equal("Solo para el evento", descriptor.DistributionRestrictions);
    }

    [Fact]
    public void The_form_builds_the_contract_with_the_eighteen_fields_and_no_more()
    {
        // El contrato de Shared tiene una lista exacta de campos, y el formulario no puede
        // añadir ni uno (AD-34).
        var properties = typeof(GameDescriptor)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.Equal(18, properties.Length);
    }

    [Fact]
    public void The_form_of_a_modificacion_is_filled_with_the_metadata_of_the_videogame()
    {
        var viewModel = new GameFormViewModel(CreateDescriptor());

        Assert.Equal("Counter-Strike 2", viewModel.Name);
        Assert.Equal("Shooter, Multijugador", viewModel.Categories);
        Assert.Equal("fps, valve", viewModel.Tags);
        Assert.Equal("captura-1.png, captura-2.png", viewModel.Images);
        Assert.Equal("1.41.3", viewModel.Version);
        Assert.Equal(7_600_000_000, viewModel.SizeBytes);
        Assert.Equal("bin\\cs2.exe", viewModel.LaunchExecutablePath);
        Assert.Equal("-novid, -fullscreen", viewModel.LaunchArguments);
        Assert.Equal("DirectX 12", viewModel.Dependencies);
        Assert.Equal("Propietaria", viewModel.License);
        Assert.Equal("Solo para el evento", viewModel.DistributionRestrictions);
    }

    [Fact]
    public void The_form_shows_the_size_as_the_derived_decimal_value_and_the_unit_apart()
    {
        var viewModel = new GameFormViewModel(CreateDescriptor());

        Assert.Equal("7,6", viewModel.SizeValue);
        Assert.Equal("GB", viewModel.SizeUnit);
    }

    [Fact]
    public void The_form_marks_exactly_the_fields_that_fr_1_demands()
    {
        // Los campos obligatorios de FR-1 son los que el catálogo exige, y el formulario
        // marca exactamente esos (AC4).
        //
        // La licencia y las restricciones de distribución SON metadata obligatorios del
        // videogame: el registro las exige y el formulario las marca (FR-1, NFR-8). La
        // historia de la publicación las vuelve a exigir al publicar el paquete, que es
        // una segunda comprobación distinta (AC1 de esa historia).
        Assert.Equal(17, GameFormViewModel.RequiredFieldNames.Count);
        Assert.Contains("name", GameFormViewModel.RequiredFieldNames);
        Assert.Contains("dependencies", GameFormViewModel.RequiredFieldNames);
        Assert.DoesNotContain("gameId", GameFormViewModel.RequiredFieldNames);
        Assert.Contains("license", GameFormViewModel.RequiredFieldNames);
        Assert.Contains("distributionRestrictions", GameFormViewModel.RequiredFieldNames);
    }

    [Fact]
    public void The_label_of_an_obligatory_field_carries_the_mark()
    {
        Assert.Equal("Nombre *", GameFormViewModel.LabelFor("Nombre *", "name"));
    }

    [Fact]
    public void The_list_shows_the_size_that_the_formatter_derives()
    {
        var viewModel = new CatalogViewModel([CreateDescriptor()]);

        var row = Assert.Single(viewModel.Rows);

        Assert.Equal(GameId, row.GameId);
        Assert.Equal("Counter-Strike 2", row.Name);
        Assert.Equal("1.41.3", row.Version);
        Assert.Equal(SizeFormatter.FormatValue(7_600_000_000), row.SizeValue);
        Assert.Equal("7,6", row.SizeValue);
        Assert.Equal(SizeFormatter.Unit, row.SizeUnit);
        Assert.Equal("GB", row.SizeUnit);
        Assert.Equal(1, row.MinPlayers);
        Assert.Equal(10, row.MaxPlayers);
    }

    [Fact]
    public void The_list_counts_the_videogames_that_the_catalog_publishes()
    {
        var viewModel = new CatalogViewModel([CreateDescriptor(), CreateDescriptor("Otro juego")]);

        Assert.Equal(2, viewModel.Count);
        Assert.Equal(2, viewModel.Rows.Count);
    }

    [Fact]
    public void The_list_is_replaced_by_the_videogames_that_the_repository_returns()
    {
        var viewModel = new CatalogViewModel([CreateDescriptor()]);

        viewModel.Replace([CreateDescriptor("Otro juego")]);

        Assert.Equal(1, viewModel.Count);
        Assert.Equal("Otro juego", viewModel.Rows[0].Name);
    }

    [Fact]
    public void The_section_builds_the_form_of_the_alta_and_the_form_of_the_modificacion()
    {
        var viewModel = new CatalogViewModel([CreateDescriptor()]);

        var alta = viewModel.CreateFormViewModel();

        var modificacion = viewModel.CreateFormViewModel(CreateDescriptor());

        Assert.Null(alta.GameId);
        Assert.Equal(GameId, modificacion.GameId);
    }

    /// <summary>
    /// Builds the contract of a videogame with the eighteen metadata de FR-1.
    /// </summary>
    /// <param name="name">Human readable name.</param>
    /// <returns>The contract of the videogame.</returns>
    private static GameDescriptor CreateDescriptor(string name = "Counter-Strike 2") => new(
        GameId,
        name,
        "Disparadores en primera persona.",
        ["Shooter", "Multijugador"],
        ["fps", "valve"],
        "cover.png",
        ["captura-1.png", "captura-2.png"],
        "1.41.3",
        7_600_000_000,
        1,
        10,
        "Windows",
        "Servidor",
        "Portable",
        new InstallationManifest.LaunchSettings("bin\\cs2.exe", ["-novid", "-fullscreen"], "game\\bin"),
        ["DirectX 12"],
        "Propietaria",
        "Solo para el evento");
}
