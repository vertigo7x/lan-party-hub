using System.Net;
using System.Text.Json;
using LANPartyHub.Server.Host;
using LANPartyHub.Server.Infrastructure.Persistence;
using LANPartyHub.Shared.Contracts;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LANPartyHub.IntegrationTests;

/// <summary>
/// The catalog of the videogames of the event against the real host and the real SQLite
/// file (AD-16, AC1, AC2, AC3, AC5).
///
/// <para>
/// The tests start the real host, with the real Kestrel, the real sockets and a real
/// <c>catalog.sqlite</c> in a file, because that is the only way to observe what the
/// server persists and what it answers. Every row of the matrix of the story is covered
/// here: the alta, the modificación, the rejection of a mandatory metadata that is
/// absent, the rejection of a body without <c>schemaVersion</c> and with an unsupported
/// one, the rejection of a modification of a videogame that does not exist, the two
/// videogames with the same name and the failure of the persistence.
/// </para>
/// </summary>
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

        // The identifier is a UUID v7 opaco in su forma canónica (AD-23).
        var gameId = root.GetProperty("gameId").GetString();

        Assert.NotNull(gameId);
        Assert.Equal(32, gameId!.Length);
        Assert.Equal(gameId.ToLowerInvariant(), gameId);
        Assert.All(gameId, character => Assert.True(
            char.IsAsciiDigit(character) || (character is >= 'a' and <= 'f'),
            $"The identifier '{gameId}' is not canonical."));
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
        request.Version = "1.42.0";
        request.SizeBytes = 8_100_000_000;
        request.LaunchConfiguration = new InstallationManifest.LaunchSettings(
            "bin\\cs2.exe",
            ["-novid"],
            "game\\bin");

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
        request.Version = "1.42.0";

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
        second.Version = "1.42.0";

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

    [Fact]
    public async Task A_mandatory_metadata_that_is_absent_is_rejected_without_writing_a_row()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        var request = BuildValidRequest();
        request.License = string.Empty;

        using var response = await client.PostAsync(
            GamesEndpoint.Route,
            BuildBody(request),
            TestContext.Current.CancellationToken);

        // El cuerpo es sintácticamente válido y viola una regla de negocio (AD-32, AD-15).
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var error = await ReadErrorAsync(response, TestContext.Current.CancellationToken);

        Assert.Equal(ErrorCatalog.GameMetadataMissingCode, error.Code);

        // El código y el mensaje nombran el campo concreto que falta (AD-25).
        Assert.Contains("license", error.Message, StringComparison.OrdinalIgnoreCase);

        // No se escribe ninguna fila (AD-15).
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

        var error = await ReadErrorAsync(response, TestContext.Current.CancellationToken);

        Assert.Equal(ErrorCatalog.SchemaVersionMissingCode, error.Code);

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

        var error = await ReadErrorAsync(response, TestContext.Current.CancellationToken);

        Assert.Equal(ErrorCatalog.SchemaVersionNotSupportedCode, error.Code);

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

        var error = await ReadErrorAsync(response, TestContext.Current.CancellationToken);

        Assert.Equal(ErrorCatalog.GameNotFoundCode, error.Code);

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

        var error = await ReadErrorAsync(response, TestContext.Current.CancellationToken);

        Assert.False(string.IsNullOrWhiteSpace(error.Code));
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
        Assert.False(string.IsNullOrWhiteSpace(error.Cause));
        Assert.False(string.IsNullOrWhiteSpace(error.NextStep));
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

    /// <summary>
    /// Builds a body of the administration with the eighteen metadata de FR-1.
    /// </summary>
    /// <returns>The request of the administration.</returns>
    private static GameRequest BuildValidRequest() => new()
    {
        SchemaVersion = GamesEndpoint.SupportedSchemaVersion,
        Name = "Counter-Strike 2",
        Description = "Disparadores en primera persona.",
        Categories = ["Shooter", "Multijugador"],
        Tags = ["fps", "valve"],
        CoverImage = "cover.png",
        Images = ["captura-1.png", "captura-2.png"],
        Version = "1.41.3",
        SizeBytes = 7_600_000_000,
        MinPlayers = 1,
        MaxPlayers = 10,
        OperatingSystem = "Windows",
        DistributionType = "Servidor",
        InstallationType = "Portable",
        LaunchConfiguration = new InstallationManifest.LaunchSettings(
            "bin\\cs2.exe",
            ["-novid", "-fullscreen"],
            "game\\bin"),
        Dependencies = ["DirectX 12"],
        License = "Propietaria",
        DistributionRestrictions = "Solo para el evento",
    };

    /// <summary>
    /// Builds the body of the request with the JSON that el servidor espera.
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
    /// <returns>The error of la API.</returns>
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
