using LANPartyHub.Server.Domain.Catalog;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The invariants of the metadata of a videogame (AC1, AC2, FR-1, AD-1, AD-34).
///
/// <para>
/// The rules live in the domain, so these checks exercise the domain and not the adapter:
/// a mandatory metadata that is absent is named, the range of players has to be coherent,
/// the version is an opaque string of the editor and the size is kept in exact bytes.
/// </para>
/// </summary>
public sealed class GameMetadataTests
{
    /// <summary>
    /// Builds a registration with the eighteen metadata of FR-1, so that every check can
    /// break exactly the field that it examines.
    /// </summary>
    /// <returns>A registration that satisfies every invariant.</returns>
    private static GameRegistration CreateValidRegistration() => new(
        "0193f2a1b2c34d4e5f60718293a4b5c6",
        "Counter-Strike 2",
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
        "bin\\cs2.exe",
        ["-novid", "-fullscreen"],
        "game\\bin",
        ["DirectX 12"],
        "Propietaria",
        "Solo para el evento");

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
    public void A_mandatory_metadata_that_is_empty_is_rejected_and_named(string fieldName)
    {
        var exception = Assert.Throws<GameMetadataMissingException>(() => BuildWithText(fieldName, string.Empty));

        // The exception names the concrete field that is missing, so the organizer knows
        // which one to fill in without having to guess (AD-25).
        Assert.Equal(fieldName, exception.FieldName);
        Assert.Contains(fieldName, exception.Message, StringComparison.Ordinal);
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
    public void A_mandatory_metadata_that_is_only_white_space_is_rejected_and_named(string fieldName)
    {
        var exception = Assert.Throws<GameMetadataMissingException>(() => BuildWithText(fieldName, "   "));

        Assert.Equal(fieldName, exception.FieldName);
    }

    [Theory]
    [InlineData("categories")]
    [InlineData("tags")]
    [InlineData("images")]
    [InlineData("dependencies")]
    public void A_mandatory_collection_that_is_empty_is_rejected_and_named(string fieldName)
    {
        var exception = Assert.Throws<GameMetadataMissingException>(() => BuildWithCollection(fieldName, []));

        Assert.Equal(fieldName, exception.FieldName);
    }

    [Fact]
    public void The_launch_configuration_without_a_path_is_rejected_and_named()
    {
        var exception = Assert.Throws<GameMetadataMissingException>(
            () => new GameRegistration(
                "0193f2a1b2c34d4e5f60718293a4b5c6",
                "Counter-Strike 2",
                "Disparadores en primera persona.",
                ["Shooter"],
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
                string.Empty,
                [],
                "game\\bin",
                [],
                "Propietaria",
                "Solo para el evento"));

        Assert.Equal(GameRegistration.FieldNames.LaunchConfiguration, exception.FieldName);
    }

    [Fact]
    public void A_minimum_of_players_that_is_greater_than_the_maximum_is_rejected()
    {
        var exception = Assert.Throws<GamePlayerRangeException>(() => new GameRegistration(
            "0193f2a1b2c34d4e5f60718293a4b5c6",
            "Counter-Strike 2",
            "Disparadores en primera persona.",
            ["Shooter"],
            ["fps"],
            "cover.png",
            ["captura-1.png"],
            "1.41.3",
            7_600_000_000,
            8,
            4,
            "Windows",
            "Servidor",
            "Portable",
            "bin\\cs2.exe",
            [],
            "game\\bin",
            ["DirectX 12"],
            "Propietaria",
            "Solo para el evento"));

        Assert.Equal(8, exception.MinPlayers);
        Assert.Equal(4, exception.MaxPlayers);
        Assert.Contains("8", exception.Message, StringComparison.Ordinal);
        Assert.Contains("4", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_minimum_of_players_that_equals_the_maximum_is_accepted()
    {
        var registration = new GameRegistration(
            "0193f2a1b2c34d4e5f60718293a4b5c6",
            "Counter-Strike 2",
            "Disparadores en primera persona.",
            ["Shooter"],
            ["fps"],
            "cover.png",
            ["captura-1.png"],
            "1.41.3",
            7_600_000_000,
            4,
            4,
            "Windows",
            "Servidor",
            "Portable",
            "bin\\cs2.exe",
            [],
            "game\\bin",
            ["DirectX 12"],
            "Propietaria",
            "Solo para el evento");

        Assert.Equal(4, registration.MinPlayers);
        Assert.Equal(4, registration.MaxPlayers);
    }

    [Theory]
    [InlineData("1.41.3")]
    [InlineData("v1.0-beta")]
    [InlineData("2026.10")]
    [InlineData("nightly-20261029")]
    [InlineData("0")]
    public void The_version_is_an_opaque_string_of_the_editor_and_never_a_number(string version)
    {
        var registration = BuildWithText(GameRegistration.FieldNames.Version, version);

        // The version travels as it was written: it is not parsed and it is not compared
        // as a number (AD-34, convención de datos).
        Assert.Equal(version, registration.Version);
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(1_073_741_824L)]
    [InlineData(7_600_000_000L)]
    [InlineData(9_999_999_999_999L)]
    public void The_size_is_kept_in_exact_bytes(long sizeBytes)
    {
        var registration = BuildWithSize(sizeBytes);

        // The size travels in exact bytes and it is never rounded or converted (AD-7).
        Assert.Equal(sizeBytes, registration.SizeBytes);
    }

    [Fact]
    public void A_size_of_zero_bytes_is_rejected_as_a_missing_metadata()
    {
        var exception = Assert.Throws<GameMetadataMissingException>(() => BuildWithSize(0));

        Assert.Equal(GameRegistration.FieldNames.SizeBytes, exception.FieldName);
    }

    [Fact]
    public void A_negative_size_is_rejected_as_a_missing_metadata()
    {
        var exception = Assert.Throws<GameMetadataMissingException>(() => BuildWithSize(-1));

        Assert.Equal(GameRegistration.FieldNames.SizeBytes, exception.FieldName);
    }

    [Fact]
    public void A_valid_registration_keeps_every_metadata_of_fr_1()
    {
        var registration = CreateValidRegistration();

        Assert.Equal("Counter-Strike 2", registration.Name);
        Assert.Equal("Disparadores en primera persona.", registration.Description);
        Assert.Equal(["Shooter", "Multijugador"], registration.Categories);
        Assert.Equal(["fps", "valve"], registration.Tags);
        Assert.Equal("cover.png", registration.CoverImage);
        Assert.Equal(["captura-1.png", "captura-2.png"], registration.Images);
        Assert.Equal("1.41.3", registration.Version);
        Assert.Equal(7_600_000_000, registration.SizeBytes);
        Assert.Equal(1, registration.MinPlayers);
        Assert.Equal(10, registration.MaxPlayers);
        Assert.Equal("Windows", registration.OperatingSystem);
        Assert.Equal("Servidor", registration.DistributionType);
        Assert.Equal("Portable", registration.InstallationType);
        Assert.Equal("bin\\cs2.exe", registration.LaunchExecutablePath);
        Assert.Equal(["-novid", "-fullscreen"], registration.LaunchArguments);
        Assert.Equal("game\\bin", registration.LaunchWorkingDirectory);
        Assert.Equal(["DirectX 12"], registration.Dependencies);
        Assert.Equal("Propietaria", registration.License);
        Assert.Equal("Solo para el evento", registration.DistributionRestrictions);
    }

    [Fact]
    public void The_identifier_of_a_videogame_is_a_canonical_uuid_v7()
    {
        var gameId = GameRegistration.GenerateGameId();

        Assert.Equal(32, gameId.Length);
        Assert.True(GameRegistration.IsCanonicalIdentifier(gameId));
        Assert.Equal(gameId.ToLowerInvariant(), gameId);
    }

    [Fact]
    public void Two_identifiers_of_videogames_are_different()
    {
        var first = GameRegistration.GenerateGameId();
        var second = GameRegistration.GenerateGameId();

        Assert.NotEqual(first, second);
    }

    /// <summary>
    /// Builds a registration with one of its mandatory texts replaced.
    /// </summary>
    /// <param name="fieldName">Name of the field to replace.</param>
    /// <param name="value">Value that the field takes.</param>
    /// <returns>The registration.</returns>
    private static GameRegistration BuildWithText(string fieldName, string value) => new(
        "0193f2a1b2c34d4e5f60718293a4b5c6",
        fieldName == "name" ? value : "Counter-Strike 2",
        fieldName == "description" ? value : "Disparadores en primera persona.",
        ["Shooter"],
        ["fps"],
        fieldName == "coverImage" ? value : "cover.png",
        ["captura-1.png"],
        fieldName == "version" ? value : "1.41.3",
        7_600_000_000,
        1,
        10,
        fieldName == "operatingSystem" ? value : "Windows",
        fieldName == "distributionType" ? value : "Servidor",
        fieldName == "installationType" ? value : "Portable",
        "bin\\cs2.exe",
        [],
        "game\\bin",
        ["DirectX 12"],
        fieldName == "license" ? value : "Propietaria",
        fieldName == "distributionRestrictions" ? value : "Solo para el evento");

    /// <summary>
    /// Builds a registration with one of its mandatory collections replaced.
    /// </summary>
    /// <param name="fieldName">Name of the field to replace.</param>
    /// <param name="values">Values that the field takes.</param>
    /// <returns>The registration.</returns>
    private static GameRegistration BuildWithCollection(string fieldName, IReadOnlyList<string> values) => new(
        "0193f2a1b2c34d4e5f60718293a4b5c6",
        "Counter-Strike 2",
        "Disparadores en primera persona.",
        fieldName == "categories" ? values : ["Shooter"],
        fieldName == "tags" ? values : ["fps"],
        "cover.png",
        fieldName == "images" ? values : ["captura-1.png"],
        "1.41.3",
        7_600_000_000,
        1,
        10,
        "Windows",
        "Servidor",
        "Portable",
        "bin\\cs2.exe",
        [],
        "game\\bin",
        fieldName == "dependencies" ? values : ["DirectX 12"],
        "Propietaria",
        "Solo para el evento");

    /// <summary>
    /// Builds a registration with another size.
    /// </summary>
    /// <param name="sizeBytes">Size in bytes.</param>
    /// <returns>The registration.</returns>
    private static GameRegistration BuildWithSize(long sizeBytes) => new(
        "0193f2a1b2c34d4e5f60718293a4b5c6",
        "Counter-Strike 2",
        "Disparadores en primera persona.",
        ["Shooter"],
        ["fps"],
        "cover.png",
        ["captura-1.png"],
        "1.41.3",
        sizeBytes,
        1,
        10,
        "Windows",
        "Servidor",
        "Portable",
        "bin\\cs2.exe",
        [],
        "game\\bin",
        ["DirectX 12"],
        "Propietaria",
        "Solo para el evento");
}
