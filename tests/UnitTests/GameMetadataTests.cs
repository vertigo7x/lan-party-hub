using LANPartyHub.Server.Domain.Catalog;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// Invariants of the domain of the catalog (AD-1, AD-7, AD-34, FR-1, AC1, AC2).
///
/// <para>
/// The domain is the layer that holds the rules of the videogame, so the rules are
/// exercised here and not in the adapter: a mandatory metadata that is absent is
/// rejected and the rejection names the concrete field, the range of players has to be
/// coherent, the size is kept in exact bytes and the version is an opaque string of the
/// editor (AD-1, AD-25).
/// </para>
/// </summary>
public sealed class GameMetadataTests
{
    private const string GameId = "0193f2a1b2c34d4e5f60718293a4b5c6";

    [Fact]
    public void A_mandatory_metadata_that_is_absent_is_rejected_and_the_exception_names_the_field()
    {
        var exception = Assert.Throws<GameMetadataMissingException>(
            () => Create(name: string.Empty));

        Assert.Equal(GameRegistration.FieldNames.Name, exception.FieldName);
        Assert.Contains(GameRegistration.FieldNames.Name, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("description")]
    [InlineData("coverImage")]
    [InlineData("version")]
    [InlineData("operatingSystem")]
    [InlineData("distributionType")]
    [InlineData("installationType")]
    [InlineData("license")]
    [InlineData("distributionRestrictions")]
    public void Every_mandatory_text_names_the_field_that_fails(string fieldName)
    {
        var exception = Assert.Throws<GameMetadataMissingException>(
            () => Create(fieldName: fieldName));

        Assert.Equal(fieldName, exception.FieldName);
    }

    [Fact]
    public void A_null_identifier_names_the_identifier_and_not_the_name()
    {
        // The identifier that is absent is the identifier itself, and not another field:
        // the organizer has to know which box is empty (AD-25).
        var exception = Assert.Throws<GameMetadataMissingException>(
            () => Create(gameId: null));

        Assert.Equal(GameRegistration.FieldNames.GameId, exception.FieldName);
        Assert.NotEqual(GameRegistration.FieldNames.Name, exception.FieldName);
    }

    [Fact]
    public void An_empty_identifier_is_rejected()
    {
        var exception = Assert.Throws<GameMetadataMissingException>(
            () => Create(gameId: string.Empty));

        Assert.Equal(GameRegistration.FieldNames.GameId, exception.FieldName);
    }

    [Fact]
    public void An_identifier_that_holds_only_white_space_is_rejected()
    {
        var exception = Assert.Throws<GameMetadataMissingException>(
            () => Create(gameId: "   "));

        Assert.Equal(GameRegistration.FieldNames.GameId, exception.FieldName);
    }

    [Fact]
    public void A_mandatory_collection_that_is_empty_is_rejected()
    {
        var exception = Assert.Throws<GameMetadataMissingException>(
            () => Create(categories: []));

        Assert.Equal(GameRegistration.FieldNames.Categories, exception.FieldName);
    }

    [Fact]
    public void An_absent_item_inside_a_mandatory_collection_is_rejected()
    {
        // A collection with an absent item is as absent as an empty one: the organizer
        // would publish a category that does not exist (AD-25).
        var exception = Assert.Throws<GameMetadataMissingException>(
            () => Create(categories: ["Shooter", null!]));

        Assert.Equal(GameRegistration.FieldNames.Categories, exception.FieldName);
    }

    [Fact]
    public void An_item_of_a_mandatory_collection_that_holds_only_white_space_is_rejected()
    {
        var exception = Assert.Throws<GameMetadataMissingException>(
            () => Create(tags: ["fps", "   "]));

        Assert.Equal(GameRegistration.FieldNames.Tags, exception.FieldName);
    }

    [Fact]
    public void The_items_of_a_mandatory_collection_are_trimmed_like_the_texts()
    {
        var game = Create(categories: ["  Shooter  ", " Multijugador "]);

        Assert.Equal(["Shooter", "Multijugador"], game.Categories);
    }

    [Fact]
    public void The_mandatory_texts_are_trimmed()
    {
        var game = Create(name: "  Counter-Strike 2  ");

        Assert.Equal("Counter-Strike 2", game.Name);
    }

    [Fact]
    public void The_minimum_of_players_that_is_greater_than_the_maximum_is_rejected()
    {
        var exception = Assert.Throws<GamePlayerRangeException>(
            () => Create(minPlayers: 4, maxPlayers: 2));

        Assert.Equal(4, exception.MinPlayers);
        Assert.Equal(2, exception.MaxPlayers);
    }

    [Fact]
    public void The_range_of_players_that_is_coherent_is_accepted()
    {
        var game = Create(minPlayers: 2, maxPlayers: 2);

        Assert.Equal(2, game.MinPlayers);
        Assert.Equal(2, game.MaxPlayers);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_size_that_is_not_positive_is_rejected(int sizeBytes)
    {
        var exception = Assert.Throws<GameMetadataMissingException>(
            () => Create(sizeBytes: sizeBytes));

        Assert.Equal(GameRegistration.FieldNames.SizeBytes, exception.FieldName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void A_minimum_of_players_that_is_not_positive_is_rejected(int minPlayers)
    {
        var exception = Assert.Throws<GameMetadataMissingException>(
            () => Create(minPlayers: minPlayers, maxPlayers: 10));

        Assert.Equal(GameRegistration.FieldNames.MinPlayers, exception.FieldName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void A_maximum_of_players_that_is_not_positive_is_rejected(int maxPlayers)
    {
        var exception = Assert.Throws<GameMetadataMissingException>(
            () => Create(minPlayers: 1, maxPlayers: maxPlayers));

        Assert.Equal(GameRegistration.FieldNames.MaxPlayers, exception.FieldName);
    }

    [Fact]
    public void The_size_is_kept_in_exact_bytes()
    {
        // The conversion to decimal gigabytes is a rule of presentation and not of the
        // model, so the domain keeps the exact value (AD-7).
        var game = Create(sizeBytes: 7_600_000_001);

        Assert.Equal(7_600_000_001, game.SizeBytes);
    }

    [Fact]
    public void The_version_is_an_opaque_string_of_the_editor()
    {
        // The version is never parsed as a number: the editor decides what it means
        // (AD-7, convención de datos).
        var game = Create(version: "1.41.3-beta2+build77");

        Assert.Equal("1.41.3-beta2+build77", game.Version);
    }

    [Fact]
    public void The_launch_arguments_are_validated_like_the_rest_of_the_mandatory_collections()
    {
        var exception = Assert.Throws<GameMetadataMissingException>(
            () => Create(launchArguments: ["-novid", null!]));

        Assert.Equal(GameRegistration.FieldNames.LaunchConfiguration, exception.FieldName);
    }

    [Fact]
    public void An_empty_list_of_launch_arguments_is_accepted()
    {
        // Un videojuego que se lanza sin argumentos es un caso ordinario: la lista de
        // argumentos es la única colección de la ficha que puede estar vacía (AD-34).
        var game = Create(launchArguments: []);

        Assert.Empty(game.LaunchArguments);
    }

    [Fact]
    public void The_launch_arguments_are_trimmed_like_the_rest_of_the_mandatory_collections()
    {
        var game = Create(launchArguments: [" -novid ", "-fullscreen"]);

        Assert.Equal(["-novid", "-fullscreen"], game.LaunchArguments);
    }

    [Fact]
    public void The_launch_configuration_that_is_empty_names_the_launch_configuration()
    {
        var exception = Assert.Throws<GameMetadataMissingException>(
            () => Create(launchExecutablePath: string.Empty));

        Assert.Equal(GameRegistration.FieldNames.LaunchConfiguration, exception.FieldName);
    }

    [Fact]
    public void The_generated_identifier_is_a_canonical_uuid_v7()
    {
        var gameId = GameRegistration.GenerateGameId();

        Assert.Equal(GameRegistration.CanonicalIdentifierLength, gameId.Length);
        Assert.Equal(gameId.ToLowerInvariant(), gameId);
        Assert.All(gameId, character => Assert.True(
            char.IsAsciiDigit(character) || (character is >= 'a' and <= 'f'),
            $"The identifier '{gameId}' is not canonical."));
        Assert.True(GameRegistration.IsCanonicalIdentifier(gameId));
    }

    [Fact]
    public void The_identifier_that_arrives_is_kept_as_it_is()
    {
        // The identifier never changes in a modificación, y el servidor no inventa otro
        // (AD-34).
        var game = Create(gameId: GameId);

        Assert.Equal(GameId, game.GameId);
    }

    /// <summary>
    /// Builds a registration of a videogame with the eighteen metadata de FR-1, de forma
    /// que cada prueba pueda alterar uno solo de ellos.
    /// </summary>
    /// <param name="gameId">Identifier of the videogame.</param>
    /// <param name="name">Human readable name.</param>
    /// <param name="description">Description of the videogame.</param>
    /// <param name="categories">Categories used to filter the catalog.</param>
    /// <param name="tags">Free form tags of the videogame.</param>
    /// <param name="coverImage">Reference of the cover image.</param>
    /// <param name="images">References of the additional images.</param>
    /// <param name="version">Version published in the catalog.</param>
    /// <param name="sizeBytes">Exact size of the package in bytes.</param>
    /// <param name="minPlayers">Minimum number of players.</param>
    /// <param name="maxPlayers">Maximum number of players.</param>
    /// <param name="operatingSystem">Operating system the videogame runs on.</param>
    /// <param name="distributionType">Distribution type of the videogame.</param>
    /// <param name="installationType">Installation type of the videogame.</param>
    /// <param name="launchExecutablePath">Path of the executable.</param>
    /// <param name="launchArguments">Arguments passed to the executable.</param>
    /// <param name="launchWorkingDirectory">Working directory of the process.</param>
    /// <param name="dependencies">Dependencies required by the videogame.</param>
    /// <param name="license">License of the videogame.</param>
    /// <param name="distributionRestrictions">Restrictions on the publication.</param>
    /// <param name="fieldName">
    /// Name of the field that the test wants to leave empty. It exists so the tests that
    /// comprueban el nombre del campo que falta no repiten el constructor entero.
    /// </param>
    /// <returns>The registration of the videogame.</returns>
    private static GameRegistration Create(
        string? gameId = GameId,
        string name = "Counter-Strike 2",
        string description = "Disparadores en primera persona.",
        IReadOnlyList<string>? categories = null,
        IReadOnlyList<string>? tags = null,
        string coverImage = "cover.png",
        IReadOnlyList<string>? images = null,
        string version = "1.41.3",
        long sizeBytes = 7_600_000_000,
        int minPlayers = 1,
        int maxPlayers = 10,
        string operatingSystem = "Windows",
        string distributionType = "Servidor",
        string installationType = "Portable",
        string launchExecutablePath = "bin\\cs2.exe",
        IReadOnlyList<string>? launchArguments = null,
        string launchWorkingDirectory = "game\\bin",
        IReadOnlyList<string>? dependencies = null,
        string license = "Propietaria",
        string distributionRestrictions = "Solo para el evento",
        string? fieldName = null)
    {
        name = Empty(fieldName, GameRegistration.FieldNames.Name, name);
        description = Empty(fieldName, GameRegistration.FieldNames.Description, description);
        coverImage = Empty(fieldName, GameRegistration.FieldNames.CoverImage, coverImage);
        version = Empty(fieldName, GameRegistration.FieldNames.Version, version);
        operatingSystem = Empty(fieldName, GameRegistration.FieldNames.OperatingSystem, operatingSystem);
        distributionType = Empty(fieldName, GameRegistration.FieldNames.DistributionType, distributionType);
        installationType = Empty(fieldName, GameRegistration.FieldNames.InstallationType, installationType);
        license = Empty(fieldName, GameRegistration.FieldNames.License, license);
        distributionRestrictions = Empty(
            fieldName,
            GameRegistration.FieldNames.DistributionRestrictions,
            distributionRestrictions);

        return new GameRegistration(
            gameId!,
            name,
            description,
            categories ?? ["Shooter", "Multijugador"],
            tags ?? ["fps", "valve"],
            coverImage,
            images ?? ["captura-1.png", "captura-2.png"],
            version,
            sizeBytes,
            minPlayers,
            maxPlayers,
            operatingSystem,
            distributionType,
            installationType,
            launchExecutablePath,
            launchArguments ?? ["-novid", "-fullscreen"],
            launchWorkingDirectory,
            dependencies ?? ["DirectX 12"],
            license,
            distributionRestrictions);
    }

    /// <summary>
    /// Leaves empty the field that the test wants to leave empty.
    /// </summary>
    /// <param name="fieldName">Name of the field that the test wants to leave empty.</param>
    /// <param name="candidate">Name of the field that this value belongs to.</param>
    /// <param name="value">Value of the field.</param>
    /// <returns>The value of the field, empty when it is the one that the test alters.</returns>
    private static string Empty(string? fieldName, string candidate, string value) =>
        fieldName == candidate ? string.Empty : value;
}
