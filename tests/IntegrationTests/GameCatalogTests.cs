using System.Net;
using System.Text.Json;
using LANPartyHub.Server.Application.Ports;
using LANPartyHub.Server.Host;
using LANPartyHub.Server.Infrastructure.Persistence;
using LANPartyHub.Shared.Contracts;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LANPartyHub.IntegrationTests;

/// <summary>
/// The catalog of the videogames of the event against the real host and the real SQLite
/// file (AD-16, AC1, AC2, AC3, AC5).
///
/// <para>
/// The tests start the real host, with the real Kestrel, the real sockets and a real
/// <c>catalog.sqlite</c> in a file, because that is the only way to observe what the
/// server persists and what it answers. Every row of the matrix of edges of the story is
/// covered here: the alta, the modificación, the rejection of a mandatory metadata that
/// is absent, the rejection of a body without <c>schemaVersion</c> and with an unsupported
/// one, the rejection of a modification of a videogame that does not exist, the rejection
/// of an incoherent range of players, the two videogames with the same name and the
/// failure of the persistence.
/// </para>
/// </summary>
[Collection(RealHostCollection.Name)]
public sealed class GameCatalogTests
{
    /// <summary>
    /// The eighteen columns of the table <c>GAME</c>, with the name and the order of
    /// AD-34. The table and the contract cannot diverge (AD-34).
    /// </summary>
    private const string ExpectedGameColumns =
        "gameId,name,description,categories,tags,coverImage,images,version,sizeBytes,minPlayers,maxPlayers,"
        + "operatingSystem,distributionType,installationType,launchConfiguration,dependencies,license,distributionRestrictions";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task The_alta_persists_the_eighteen_fields_with_the_name_and_the_order_of_ad_34()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        using var response = await client.PostAsync(
            GamesEndpoint.Route,
            BuildBody(BuildValidRequest()),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var descriptor = await ReadDescriptorAsync(response, TestContext.Current.CancellationToken);

        // The eighteen columns of the table are the eighteen metadata of FR-1, with the
        // same name and in the same order (AD-34).
        Assert.Equal(
            ExpectedGameColumns.Split(','),
            ReadColumnNames(dataDirectory, "SELECT name FROM pragma_table_info('GAME')"));

        // The row holds the metadata that the request carried.
        var row = ReadRow(dataDirectory, descriptor.GameId);

        Assert.Equal("Counter-Strike 2", row["name"]);
        Assert.Equal("Disparadores en primera persona.", row["description"]);

        // The collections are stored as their canonical JSON text, so the row holds the
        // whole collection and not only its first item.
        Assert.Equal("[\"Shooter\",\"Multijugador\"]", row["categories"]);
        Assert.Equal("[\"fps\",\"valve\"]", row["tags"]);
        Assert.Equal("cover.png", row["coverImage"]);
        Assert.Equal("[\"captura-1.png\",\"captura-2.png\"]", row["images"]);
        Assert.Equal("1.41.3", row["version"]);
        Assert.Equal(7_600_000_000L, row["sizeBytes"]);
        Assert.Equal(1L, row["minPlayers"]);
        Assert.Equal(10L, row["maxPlayers"]);
        Assert.Equal("Windows", row["operatingSystem"]);
        Assert.Equal("Servidor", row["distributionType"]);
        Assert.Equal("Portable", row["installationType"]);
        Assert.Equal("Propietaria", row["license"]);
        Assert.Equal("Solo para el evento", row["distributionRestrictions"]);
    }

    [Fact]
    public async Task The_alta_answers_the_contract_with_the_eighteen_fields()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        using var response = await client.PostAsync(
            GamesEndpoint.Route,
            BuildBody(BuildValidRequest()),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var root = document.RootElement;

        // The answer is the contract of Shared, with the eighteen fields in the fixed
        // order of AD-34 (AD-34).
        Assert.Equal(
            [
                "gameId", "name", "description", "categories", "tags", "coverImage", "images", "version",
                "sizeBytes", "minPlayers", "maxPlayers", "operatingSystem", "distributionType",
                "installationType", "launchConfiguration", "dependencies", "license", "distributionRestrictions",
            ],
            root.EnumerateObject().Select(property => property.Name).ToArray());

        // The identifier is a UUID v7 opaco en su forma canónica (AD-23).
        var gameId = root.GetProperty("gameId").GetString();

        Assert.NotNull(gameId);
        Assert.Equal(32, gameId!.Length);
        Assert.Equal(gameId.ToLowerInvariant(), gameId);
        Assert.All(gameId, character => Assert.True(
            char.IsAsciiDigit(character) || (character is >= 'a' and <= 'f'),
            $"The identifier '{gameId}' is not canonical."));
    }

    [Fact]
    public async Task The_alta_keeps_the_size_in_exact_bytes()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        var request = BuildValidRequest();
        request.Game = request.Game! with { SizeBytes = 7_600_000_001 };

        using var response = await client.PostAsync(
            GamesEndpoint.Route,
            BuildBody(request),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var descriptor = await ReadDescriptorAsync(response, TestContext.Current.CancellationToken);

        var row = ReadRow(dataDirectory, descriptor.GameId);

        Assert.Equal(7_600_000_001L, row["sizeBytes"]);
    }

    [Fact]
    public async Task The_modificacion_keeps_the_identifier_and_reflects_the_change()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        using var created = await client.PostAsync(
            GamesEndpoint.Route,
            BuildBody(BuildValidRequest()),
            TestContext.Current.CancellationToken);

        var original = await ReadDescriptorAsync(created, TestContext.Current.CancellationToken);

        var request = BuildValidRequest();
        request.GameId = original.GameId;
        request.Game = request.Game! with
        {
            Version = "1.42.0",
            SizeBytes = 8_100_000_000,
            LaunchConfiguration = new InstallationManifest.LaunchSettings(
                "bin\\cs2.exe",
                ["-novid"],
                "game\\bin"),
        };

        using var modified = await client.PostAsync(
            GamesEndpoint.Route,
            BuildBody(request),
            TestContext.Current.CancellationToken);

        // The modificación answers 200, not 201: the videogame already existed.
        Assert.Equal(HttpStatusCode.OK, modified.StatusCode);

        var updated = await ReadDescriptorAsync(modified, TestContext.Current.CancellationToken);

        // The identifier never changes in a modificación (AD-34).
        Assert.Equal(original.GameId, updated.GameId);
        Assert.Equal("1.42.0", updated.Version);
        Assert.Equal(8_100_000_000, updated.SizeBytes);

        var row = ReadRow(dataDirectory, original.GameId);

        Assert.Equal("1.42.0", row["version"]);
        Assert.Equal(8_100_000_000L, row["sizeBytes"]);

        // The row is the one of the videogame modificado, and it is the only one.
        Assert.Equal(1, ReadRowCount(dataDirectory));
    }

    [Fact]
    public async Task The_modificacion_keeps_the_metadata_that_did_not_change()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        using var created = await client.PostAsync(
            GamesEndpoint.Route,
            BuildBody(BuildValidRequest()),
            TestContext.Current.CancellationToken);

        var original = await ReadDescriptorAsync(created, TestContext.Current.CancellationToken);

        var request = BuildValidRequest();
        request.GameId = original.GameId;
        request.Game = request.Game! with { Version = "1.42.0" };

        using var modified = await client.PostAsync(
            GamesEndpoint.Route,
            BuildBody(request),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, modified.StatusCode);

        var updated = await ReadDescriptorAsync(modified, TestContext.Current.CancellationToken);

        Assert.Equal(original.Name, updated.Name);
        Assert.Equal(original.License, updated.License);
        Assert.Equal(original.DistributionRestrictions, updated.DistributionRestrictions);
        Assert.Equal(original.LaunchConfiguration.ExecutablePath, updated.LaunchConfiguration.ExecutablePath);
        Assert.Equal(original.LaunchConfiguration.WorkingDirectory, updated.LaunchConfiguration.WorkingDirectory);
    }

    [Fact]
    public async Task Two_videogames_with_the_same_name_are_told_apart_by_the_identifier_and_the_version()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        using var first = await client.PostAsync(
            GamesEndpoint.Route,
            BuildBody(BuildValidRequest()),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = BuildValidRequest();
        second.Game = second.Game! with { Version = "1.42.0" };

        using var secondResponse = await client.PostAsync(
            GamesEndpoint.Route,
            BuildBody(second),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);

        var firstDescriptor = await ReadDescriptorAsync(first, TestContext.Current.CancellationToken);
        var secondDescriptor = await ReadDescriptorAsync(secondResponse, TestContext.Current.CancellationToken);

        Assert.Equal(firstDescriptor.Name, secondDescriptor.Name);
        Assert.NotEqual(firstDescriptor.GameId, secondDescriptor.GameId);
        Assert.NotEqual(firstDescriptor.Version, secondDescriptor.Version);

        // Las dos filas conviven en el catálogo (FR-1).
        Assert.Equal(2, ReadRowCount(dataDirectory));
    }

    [Theory]
    [InlineData("name", "GAME_NAME_MISSING")]
    [InlineData("description", "GAME_DESCRIPTION_MISSING")]
    [InlineData("coverImage", "GAME_COVER_IMAGE_MISSING")]
    [InlineData("version", "GAME_VERSION_MISSING")]
    [InlineData("operatingSystem", "GAME_OPERATING_SYSTEM_MISSING")]
    [InlineData("distributionType", "GAME_DISTRIBUTION_TYPE_MISSING")]
    [InlineData("installationType", "GAME_INSTALLATION_TYPE_MISSING")]
    public async Task A_mandatory_metadata_that_is_absent_is_rejected_with_the_code_that_names_the_field(
        string fieldName,
        string expectedCode)
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        var request = BuildValidRequest();
        request.Game = ApplyEmptyField(request.Game!, fieldName);

        using var response = await client.PostAsync(
            GamesEndpoint.Route,
            BuildBody(request),
            TestContext.Current.CancellationToken);

        // El cuerpo es sintácticamente válido y viola una regla de negocio (AD-15).
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        AssertRejected(body, expectedCode);

        // El código nombra el campo concreto que falta (AD-25). El nombre del campo va
        // en el código con la convención del catálogo, que son palabras en mayúsculas
        // separadas por guion bajo, de modo que la comparación se hace con las palabras
        // del campo y no con su nombre en camelCase.
        using var document = JsonDocument.Parse(body);

        var code = document.RootElement.GetProperty("code").GetString();

        Assert.NotNull(code);
        Assert.NotEqual(ErrorCatalog.GameMetadataMissingCode, code);
        Assert.Equal(expectedCode, code);

        foreach (var word in SplitWords(fieldName))
        {
            Assert.Contains(word, code!, StringComparison.OrdinalIgnoreCase);
        }

        // No se escribe ninguna fila (AD-15).
        Assert.Equal(0, ReadRowCount(dataDirectory));
    }

    [Fact]
    public async Task A_mandatory_collection_that_is_empty_is_rejected_with_the_code_that_names_the_field()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        var request = BuildValidRequest();
        request.Game = request.Game! with { Categories = [] };

        using var response = await client.PostAsync(
            GamesEndpoint.Route,
            BuildBody(request),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        AssertRejected(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            ErrorCatalog.GameCategoriesMissingCode);

        Assert.Equal(0, ReadRowCount(dataDirectory));
    }

    [Fact]
    public async Task The_range_of_players_that_is_not_coherent_is_rejected_with_its_own_code()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        var request = BuildValidRequest();
        request.Game = request.Game! with { MinPlayers = 4, MaxPlayers = 2 };

        using var response = await client.PostAsync(
            GamesEndpoint.Route,
            BuildBody(request),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        // El rango inválido tiene su propio código, distinto del de un metadato ausente,
        // de modo que la métrica de errores por código los distingue (AD-15, AD-25).
        AssertRejected(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            ErrorCatalog.GamePlayerRangeInvalidCode);

        Assert.Equal(0, ReadRowCount(dataDirectory));
    }

    [Fact]
    public async Task A_body_without_the_schema_version_is_rejected_without_writing_a_row()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        var request = BuildValidRequest();
        request.SchemaVersion = null;

        using var response = await client.PostAsync(
            GamesEndpoint.Route,
            BuildBody(request),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        AssertRejected(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            ErrorCatalog.SchemaVersionMissingCode);

        // No se escribe ninguna fila (AD-15).
        Assert.Equal(0, ReadRowCount(dataDirectory));
    }

    [Fact]
    public async Task A_body_with_an_unsupported_schema_version_is_rejected_without_writing_a_row()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        var request = BuildValidRequest();
        request.SchemaVersion = 99;

        using var response = await client.PostAsync(
            GamesEndpoint.Route,
            BuildBody(request),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        AssertRejected(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            ErrorCatalog.SchemaVersionNotSupportedCode);

        // No se escribe ninguna fila (AD-15).
        Assert.Equal(0, ReadRowCount(dataDirectory));
    }

    [Fact]
    public async Task The_modificacion_of_a_videogame_that_does_not_exist_is_rejected_without_writing_a_row()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        var request = BuildValidRequest();
        request.GameId = "0000000000000000000000000000ffff";

        using var response = await client.PostAsync(
            GamesEndpoint.Route,
            BuildBody(request),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        AssertRejected(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            ErrorCatalog.GameNotFoundCode);

        // No se escribe ninguna fila (AD-15).
        Assert.Equal(0, ReadRowCount(dataDirectory));
    }

    [Fact]
    public async Task A_failure_of_the_persistence_is_answered_with_the_error_of_the_catalog()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        // La tabla de los videojuegos se borra de la base de datos real, de modo que la
        // escritura de EF Core falla al guardar. La operación es real y su fallo es real.
        using (var connection = OpenDatabase(dataDirectory))
        {
            using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE GAME";

            command.ExecuteNonQuery();
        }

        using var response = await client.PostAsync(
            GamesEndpoint.Route,
            BuildBody(BuildValidRequest()),
            TestContext.Current.CancellationToken);

        // La excepción nunca alcanza el mensaje de la respuesta (AD-15).
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        using var document = JsonDocument.Parse(body);

        var code = document.RootElement.GetProperty("code").GetString();

        Assert.False(string.IsNullOrWhiteSpace(code));
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("message").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("cause").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("nextStep").GetString()));

        // El mensaje de la respuesta no lleva la excepción de EF Core (AD-15).
        Assert.DoesNotContain("SqliteException", body, StringComparison.Ordinal);
        Assert.DoesNotContain("GAME", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_error_of_a_rejection_has_the_three_parts_of_the_catalog()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        var request = BuildValidRequest();
        request.SchemaVersion = null;

        using var response = await client.PostAsync(
            GamesEndpoint.Route,
            BuildBody(request),
            TestContext.Current.CancellationToken);

        var error = await ReadErrorAsync(response, TestContext.Current.CancellationToken);

        // La forma del error de la API es única y es la del catálogo (AD-15, AD-25).
        Assert.True(ErrorCatalog.TryGet(error.Code, out var entry));
        Assert.NotNull(entry);
        Assert.Equal(entry!.Message, error.Message);
        Assert.Equal(entry.Cause, error.Cause);
        Assert.Equal(entry.NextStep, error.NextStep);
    }

    [Fact]
    public async Task The_catalog_of_the_server_is_its_own_database_and_it_holds_the_table_of_the_videogames()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        // La tabla de los videojuegos vive en el archivo del servidor y en ningún otro
        // sitio (AD-22).
        Assert.True(File.Exists(Path.Combine(dataDirectory, DataDirectory.CatalogDatabaseFileName)));

        var tables = ReadColumnNames(dataDirectory, "SELECT name FROM sqlite_master WHERE type = 'table'");

        Assert.Contains("GAME", tables);
        Assert.DoesNotContain("CLIENT_INSTALLATION", tables);
    }

    [Fact]
    public async Task The_rejection_counts_the_error_by_its_stable_code()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        var request = BuildValidRequest();
        request.Game = request.Game! with { Name = string.Empty };

        using var response = await client.PostAsync(
            GamesEndpoint.Route,
            BuildBody(request),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        using var document = JsonDocument.Parse(
            await client.GetStringAsync(MetricsEndpoint.Route, TestContext.Current.CancellationToken));

        var errors = document.RootElement.GetProperty("errorsByCode");

        // La métrica cuenta el error por su código estable, y el código nombra el campo
        // que falta (AD-15, AD-25).
        Assert.Equal(1, errors.GetProperty(ErrorCatalog.GameNameMissingCode).GetInt64());
    }

    [Fact]
    public async Task A_videogame_without_launch_arguments_is_registered()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        // Un videojuego que se lanza sin argumentos es un caso ordinario: la lista de
        // argumentos es la única colección de la ficha que puede estar vacía.
        var request = BuildValidRequest();
        request.Game = request.Game! with
        {
            LaunchConfiguration = new InstallationManifest.LaunchSettings(
                "bin\\cs2.exe",
                [],
                "game\\bin"),
        };

        using var response = await client.PostAsync(
            GamesEndpoint.Route,
            BuildBody(request),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var descriptor = await ReadDescriptorAsync(response, TestContext.Current.CancellationToken);

        Assert.Empty(descriptor.LaunchConfiguration.Arguments);

        // La fila persistida guarda la lista vacía, de modo que el contrato y la tabla no
        // divergen (AD-34).
        Assert.Equal("[]", ReadLaunchArguments(dataDirectory, descriptor.GameId));
    }

    [Fact]
    public async Task A_body_without_the_metadata_of_the_videogame_is_rejected_with_its_own_code()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        // El cuerpo lleva la versión del esquema y nada más: la ausencia de los
        // metadatos tiene su propio código, distinto del de la versión que falta.
        var body = new StringContent(
            JsonSerializer.Serialize(new { schemaVersion = GamesEndpoint.SupportedSchemaVersion }),
            System.Text.Encoding.UTF8,
            "application/json");

        using var response = await client.PostAsync(
            GamesEndpoint.Route,
            body,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        AssertRejected(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            ErrorCatalog.GamePayloadMissingCode);

        Assert.Equal(0, ReadRowCount(dataDirectory));
    }

    [Fact]
    public async Task A_body_that_names_the_videogame_only_inside_the_descriptor_modifies_that_videogame()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        using var created = await client.PostAsync(
            GamesEndpoint.Route,
            BuildBody(BuildValidRequest()),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var original = await ReadDescriptorAsync(created, TestContext.Current.CancellationToken);

        // El identificador viaja solo dentro del descriptor: el cuerpo sigue siendo una
        // modificación y no un alta que descarta el identificador (AD-34).
        var request = BuildValidRequest();
        request.Game = request.Game! with { GameId = original.GameId, Version = "1.42.0" };

        using var modified = await client.PostAsync(
            GamesEndpoint.Route,
            BuildBody(request),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, modified.StatusCode);

        var updated = await ReadDescriptorAsync(modified, TestContext.Current.CancellationToken);

        Assert.Equal(original.GameId, updated.GameId);
        Assert.Equal("1.42.0", updated.Version);

        // La modificación no crea una fila nueva: el catálogo sigue teniendo una sola.
        Assert.Equal(1, ReadRowCount(dataDirectory));
    }

    [Fact]
    public async Task The_catalog_is_read_back_through_the_read_port_of_the_repository()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        // El alta se hace por la superficie de la API, que es el camino de producción.
        var first = BuildValidRequest();
        first.Game = first.Game! with { Name = "Zumba" };

        using var firstResponse = await client.PostAsync(
            GamesEndpoint.Route,
            BuildBody(first),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        var second = BuildValidRequest();
        second.Game = second.Game! with { Name = "Anclas" };

        using var secondResponse = await client.PostAsync(
            GamesEndpoint.Route,
            BuildBody(second),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);

        // La lectura se hace por el puerto del repositorio, contra el SQLite real del
        // servidor, que es el mismo archivo que la API acaba de escribir.
        await using var scope = host.Application.Services.CreateAsyncScope();

        var repository = scope.ServiceProvider.GetRequiredService<IGameRepository>();

        var games = await repository.ListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, games.Count);

        // El orden es por nombre y después por identificador (AD-1).
        Assert.Equal(["Anclas", "Zumba"], games.Select(game => game.Name).ToArray());

        Assert.All(games, game => Assert.Equal(1, game.MinPlayers));
        Assert.All(games, game => Assert.Equal(7_600_000_000, game.SizeBytes));
        Assert.All(games, game => Assert.Equal("Propietaria", game.License));
        Assert.All(games, game => Assert.Equal("Solo para el evento", game.DistributionRestrictions));
    }

    [Fact]
    public async Task The_traffic_of_the_administration_feeds_the_published_transfer_rate()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        using var response = await client.PostAsync(
            GamesEndpoint.Route,
            BuildBody(BuildValidRequest()),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var document = JsonDocument.Parse(
            await client.GetStringAsync(MetricsEndpoint.Route, TestContext.Current.CancellationToken));

        // Los bytes que la administración escribe cuentan en la tasa de transferencia
        // publicada, igual que los de la superficie del cliente (AD-15).
        Assert.True(
            document.RootElement.GetProperty("transferRateBytesPerSecond").GetInt64() > 0,
            "The transfer rate of the host is still zero after the administration has been served.");
    }

    [Theory]
    [InlineData("tags", "GAME_TAGS_MISSING")]
    [InlineData("images", "GAME_IMAGES_MISSING")]
    [InlineData("dependencies", "GAME_DEPENDENCIES_MISSING")]
    [InlineData("sizeBytes", "GAME_SIZE_BYTES_MISSING")]
    [InlineData("minPlayers", "GAME_MIN_PLAYERS_MISSING")]
    [InlineData("maxPlayers", "GAME_MAX_PLAYERS_MISSING")]
    [InlineData("launchConfiguration", "GAME_LAUNCH_CONFIGURATION_MISSING")]
    public async Task The_remaining_mandatory_metadata_have_their_own_stable_code(
        string fieldName,
        string expectedCode)
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        var request = BuildValidRequest();
        request.Game = ApplyEmptyField(request.Game!, fieldName);

        using var response = await client.PostAsync(
            GamesEndpoint.Route,
            BuildBody(request),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        AssertRejected(body, expectedCode);

        // El código nombra el campo concreto que falta (AD-25).
        using var document = JsonDocument.Parse(body);

        var code = document.RootElement.GetProperty("code").GetString();

        Assert.NotNull(code);
        Assert.NotEqual(ErrorCatalog.GameMetadataMissingCode, code);

        foreach (var word in SplitWords(fieldName))
        {
            Assert.Contains(word, code!, StringComparison.OrdinalIgnoreCase);
        }

        // No se escribe ninguna fila (AD-15).
        Assert.Equal(0, ReadRowCount(dataDirectory));
    }

    /// <summary>
    /// Builds a body of the administration with the eighteen metadata de FR-1.
    /// </summary>
    /// <returns>The request of the administration.</returns>
    private static GameRequest BuildValidRequest() => new()
    {
        SchemaVersion = GamesEndpoint.SupportedSchemaVersion,
        Game = new GameDescriptor(
            string.Empty,
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
            new InstallationManifest.LaunchSettings(
                "bin\\cs2.exe",
                ["-novid", "-fullscreen"],
                "game\\bin"),
            ["DirectX 12"],
            "Propietaria",
            "Solo para el evento"),
    };

    /// <summary>
    /// Leaves empty the metadata that the test wants to leave empty.
    /// </summary>
    /// <param name="descriptor">Contract of the videogame.</param>
    /// <param name="fieldName">Name of the field that the test wants to leave empty.</param>
    /// <returns>The contract with the field empty.</returns>
    private static GameDescriptor ApplyEmptyField(GameDescriptor descriptor, string fieldName) => fieldName switch
    {
        "name" => descriptor with { Name = string.Empty },
        "description" => descriptor with { Description = string.Empty },
        "coverImage" => descriptor with { CoverImage = string.Empty },
        "version" => descriptor with { Version = string.Empty },
        "operatingSystem" => descriptor with { OperatingSystem = string.Empty },
        "distributionType" => descriptor with { DistributionType = string.Empty },
        "installationType" => descriptor with { InstallationType = string.Empty },
        "tags" => descriptor with { Tags = [] },
        "images" => descriptor with { Images = [] },
        "dependencies" => descriptor with { Dependencies = [] },
        "sizeBytes" => descriptor with { SizeBytes = 0 },
        "minPlayers" => descriptor with { MinPlayers = 0 },
        "maxPlayers" => descriptor with { MaxPlayers = 0 },
        // La ruta de lanzamiento es obligatoria: solo los argumentos son opcionales.
        "launchConfiguration" => descriptor with
        {
            LaunchConfiguration = new InstallationManifest.LaunchSettings(
                string.Empty,
                descriptor.LaunchConfiguration.Arguments,
                descriptor.LaunchConfiguration.WorkingDirectory),
        },
        _ => descriptor,
    };

    /// <summary>
    /// Lee los argumentos de lanzamiento de la fila de un videojuego, que la columna
    /// guarda como texto JSON (AD-7, AD-34).
    /// </summary>
    /// <param name="dataDirectory">Folder of the data of the host.</param>
    /// <param name="gameId">Identifier of the videogame.</param>
    /// <returns>The arguments of the launch configuration, as they are stored.</returns>
    private static string ReadLaunchArguments(string dataDirectory, string gameId)
    {
        using var connection = OpenDatabase(dataDirectory);

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT launchConfiguration FROM GAME WHERE gameId = $gameId";
        command.Parameters.AddWithValue("$gameId", gameId);

        var canonical = (string)command.ExecuteScalar()!;

        using var document = JsonDocument.Parse(canonical);

        return document.RootElement.GetProperty("arguments").GetRawText();
    }

    /// <summary>
    /// Splits the name of a field in camelCase into its words, the way the catalog of
    /// errors writes them: en mayúsculas y separadas por guion bajo.
    /// </summary>
    /// <param name="fieldName">Name of the field.</param>
    /// <returns>The words of the name of the field.</returns>
    private static IReadOnlyList<string> SplitWords(string fieldName)
    {
        var words = new List<string>();
        var current = new System.Text.StringBuilder();

        foreach (var character in fieldName)
        {
            if (char.IsUpper(character) && current.Length > 0)
            {
                words.Add(current.ToString());
                current.Clear();
            }

            current.Append(character);
        }

        if (current.Length > 0)
        {
            words.Add(current.ToString());
        }

        return words;
    }

    /// <summary>
    /// Builds the body of the request with the JSON que el servidor espera.
    /// </summary>
    /// <param name="request">Request of the administration.</param>
    /// <returns>The content of the request.</returns>
    private static StringContent BuildBody(GameRequest request) =>
        new(JsonSerializer.Serialize(request, SerializerOptions), System.Text.Encoding.UTF8, "application/json");

    /// <summary>
    /// Reads the contract of the videogame of a response.
    /// </summary>
    /// <param name="response">Response of the host.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The contract of the videogame.</returns>
    private static async Task<GameDescriptor> ReadDescriptorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        var descriptor = JsonSerializer.Deserialize<GameDescriptor>(payload, SerializerOptions);

        Assert.NotNull(descriptor);

        return descriptor!;
    }

    /// <summary>
    /// Reads the error de la API de una respuesta.
    /// </summary>
    /// <param name="response">Response of the host.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The error de la API.</returns>
    private static async Task<ApiError> ReadErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        var error = JsonSerializer.Deserialize<ApiError>(payload, SerializerOptions);

        Assert.NotNull(error);

        return error!;
    }

    /// <summary>
    /// Asserts that the body of a rejection is the single shape of error of the system,
    /// taken from the catalog.
    /// </summary>
    /// <param name="body">Body of the rejection.</param>
    /// <param name="expectedCode">Stable code that the rejection carries.</param>
    private static void AssertRejected(string body, string expectedCode)
    {
        using var document = JsonDocument.Parse(body);

        Assert.Equal(expectedCode, document.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("message").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("cause").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("nextStep").GetString()));
    }

    /// <summary>
    /// Reads a value of the first column of a query over the database of the server.
    /// </summary>
    /// <param name="dataDirectory">Folder of the data of the host.</param>
    /// <param name="command">Query to run.</param>
    /// <returns>The values of the first column.</returns>
    private static IReadOnlyList<string> ReadColumnNames(string dataDirectory, string command)
    {
        using var connection = OpenDatabase(dataDirectory);

        using var reader = connection.CreateCommand();
        reader.CommandText = command;

        var values = new List<string>();

        using var results = reader.ExecuteReader();

        while (results.Read())
        {
            values.Add(results.GetString(0));
        }

        return values;
    }

    /// <summary>
    /// Reads the row de un videojuego de la tabla <c>GAME</c>.
    /// </summary>
    /// <param name="dataDirectory">Folder of the data of the host.</param>
    /// <param name="gameId">Identifier of the videogame.</param>
    /// <returns>The row of the videogame, keyed by the name of the column.</returns>
    private static IReadOnlyDictionary<string, object> ReadRow(string dataDirectory, string gameId)
    {
        using var connection = OpenDatabase(dataDirectory);

        using var reader = connection.CreateCommand();
        reader.CommandText = "SELECT * FROM GAME WHERE gameId = $gameId";
        reader.Parameters.AddWithValue("$gameId", gameId);

        var row = new Dictionary<string, object>(StringComparer.Ordinal);

        using var results = reader.ExecuteReader();

        Assert.True(results.Read(), "The row of the videogame was not written.");

        for (var index = 0; index < results.FieldCount; index++)
        {
            row[results.GetName(index)] = results.GetValue(index);
        }

        return row;
    }

    /// <summary>
    /// Counts the rows of la tabla <c>GAME</c>.
    /// </summary>
    /// <param name="dataDirectory">Folder of the data of the host.</param>
    /// <returns>The number of rows of la tabla.</returns>
    private static int ReadRowCount(string dataDirectory)
    {
        using var connection = OpenDatabase(dataDirectory);

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM GAME";

        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Opens the database of the server.
    /// </summary>
    /// <param name="dataDirectory">Folder of the data of the host.</param>
    /// <returns>The open connection.</returns>
    private static SqliteConnection OpenDatabase(string dataDirectory)
    {
        var connection = new SqliteConnection(
            $"Data Source={Path.Combine(dataDirectory, DataDirectory.CatalogDatabaseFileName)}");

        connection.Open();

        return connection;
    }

    private static string CreateTemporaryDataDirectory() =>
        Directory.CreateTempSubdirectory("lan-party-hub-server-").FullName;
}
