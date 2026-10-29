using LANPartyHub.Server.Presentation.Formatting;
using LANPartyHub.Server.Presentation.Packages;
using LANPartyHub.Shared.Contracts;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The surface of presentation of the section of the packages of the organizer (AC1, AC4).
///
/// <para>
/// El formulario de la publicación es la superficie donde la publicación se bloquea y
/// donde se explica el bloqueo, así que tiene que nombrar el campo concreto que falta y
/// no un error genérico (AC1, UX-DR-44). La lista muestra el tamaño ya derivado (AD-7).
/// </para>
/// </summary>
public sealed class PackagePresentationTests
{
    private const string GameId = "0193f2a1b2c34d4e5f60718293a4b5c6";

    private const string PackageId = "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e40";

    [Fact]
    public void The_form_starts_without_a_block()
    {
        var viewModel = new PackageFormViewModel();

        Assert.False(viewModel.IsBlocked);
        Assert.Null(viewModel.BlockedFieldName);
    }

    [Fact]
    public void The_block_names_the_concrete_field_that_is_missing()
    {
        // La organizadora tiene que ver cuál de los dos campos falta, y no un error
        // genérico (AC1, UX-DR-44).
        var viewModel = new PackageFormViewModel();

        viewModel.BlockFor("license");

        Assert.True(viewModel.IsBlocked);
        Assert.Equal("license", viewModel.BlockedFieldName);
    }

    [Fact]
    public void The_two_blocks_name_two_different_fields()
    {
        var license = new PackageFormViewModel();
        var restrictions = new PackageFormViewModel();

        license.BlockFor("license");
        restrictions.BlockFor("distributionRestrictions");

        Assert.NotEqual(license.BlockedFieldName, restrictions.BlockedFieldName);
    }

    [Fact]
    public void A_publication_that_succeeds_leaves_no_block()
    {
        var viewModel = new PackageFormViewModel();

        viewModel.BlockFor("license");
        viewModel.ClearBlock();

        Assert.False(viewModel.IsBlocked);
        Assert.Null(viewModel.BlockedFieldName);
    }

    [Fact]
    public void The_form_shows_the_size_as_the_derived_decimal_value_and_the_unit_apart()
    {
        var viewModel = new PackageFormViewModel
        {
            SizeBytes = 7_600_000_000,
        };

        Assert.Equal("7,6", viewModel.SizeValue);
        Assert.Equal("GB", viewModel.SizeUnit);
        Assert.Equal(SizeFormatter.Unit, viewModel.SizeUnit);
    }

    [Fact]
    public void The_form_marks_exactly_the_fields_that_the_publication_demands()
    {
        Assert.Equal(4, PackageFormViewModel.RequiredFieldNames.Count);
        Assert.Contains("gameId", PackageFormViewModel.RequiredFieldNames);
        Assert.Contains("packageId", PackageFormViewModel.RequiredFieldNames);
        Assert.Contains("version", PackageFormViewModel.RequiredFieldNames);
        Assert.Contains("sizeBytes", PackageFormViewModel.RequiredFieldNames);
    }

    [Fact]
    public void The_label_of_an_obligatory_field_carries_the_mark()
    {
        Assert.Equal("Videojuego *", PackageFormViewModel.LabelFor("Videojuego", "gameId"));
        Assert.Equal("Argumentos", PackageFormViewModel.LabelFor("Argumentos", "arguments"));
    }

    [Fact]
    public void The_form_builds_operations_that_all_belong_to_the_closed_set()
    {
        // Las operaciones que el formulario declara son del conjunto cerrado de AD-7, y
        // ninguna ejecuta comandos arbitrarios (AD-7, AD-8).
        var viewModel = new PackageFormViewModel
        {
            LaunchExecutablePath = "game\\game.exe",
            LaunchWorkingDirectory = "game",
        };

        var operations = viewModel.ToOperations();

        Assert.NotEmpty(operations);

        foreach (var operation in operations)
        {
            var name = LANPartyHub.Server.Domain.Packages.ManifestOperations.NameOf(operation);

            Assert.NotNull(name);
            Assert.Contains(name, LANPartyHub.Server.Domain.Packages.ManifestOperations.AllowedNames);
        }
    }

    [Fact]
    public void The_list_shows_the_size_that_the_formatter_derives()
    {
        var viewModel = new PackagesViewModel([CreateDescriptor()]);

        var row = Assert.Single(viewModel.Rows);

        Assert.Equal(PackageId, row.PackageId);
        Assert.Equal(GameId, row.GameId);
        Assert.Equal("1.0.0", row.Version);
        Assert.Equal("7,6", row.SizeValue);
        Assert.Equal("GB", row.SizeUnit);
        Assert.Equal(SizeFormatter.FormatValue(7_600_000_000), row.SizeValue);
    }

    [Fact]
    public void The_list_counts_the_packages_that_are_published()
    {
        var viewModel = new PackagesViewModel([CreateDescriptor(), CreateDescriptor("2.0.0")]);

        Assert.Equal(2, viewModel.Count);
        Assert.Equal(2, viewModel.Rows.Count);
    }

    [Fact]
    public void The_list_is_replaced_by_the_packages_that_the_repository_returns()
    {
        var viewModel = new PackagesViewModel([CreateDescriptor()]);

        viewModel.Replace([CreateDescriptor("2.0.0")]);

        Assert.Equal(1, viewModel.Count);
        Assert.Equal("2.0.0", viewModel.Rows[0].Version);
    }

    /// <summary>
    /// Builds the contract of a published package.
    /// </summary>
    /// <param name="version">Version published in the catalog.</param>
    /// <returns>The contract of the package.</returns>
    private static PackageDescriptor CreateDescriptor(string version = "1.0.0") => new(
        PackageId,
        GameId,
        version,
        7_600_000_000,
        0,
        new string('a', 64),
        "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e41");
}
