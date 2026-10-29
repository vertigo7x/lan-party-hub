using System.Net;
using System.Text.Json;
using LANPartyHub.Server.Application.Packages;
using LANPartyHub.Server.Application.Ports;
using LANPartyHub.Server.Domain.Packages;
using LANPartyHub.Server.Host;
using LANPartyHub.Server.Infrastructure.Persistence;
using LANPartyHub.Shared.Contracts;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LANPartyHub.IntegrationTests;

/// <summary>
/// The withdrawal of a videogame from the catalog against the real host and the real
/// SQLite file (AD-16, AC2, AC3).
///
/// <para>
/// The tests start the real host, with the real Kestrel, the real sockets and a real
/// <c>catalog.sqlite</c> in a file, because that is the only way to observe what the
/// server persists and what it answers. The withdrawal MARKS and never deletes: the rows
/// of the packages, of the videogames and of the projection of the installations of the
/// clients survive the withdrawal (AC2, AD-26), and the version of the catalog grows
/// monotonically in the same transaction as the write that causes it (AC3).
/// </para>
/// </summary>
[Collection(RealHostCollection.Name)]
public sealed class CatalogWithdrawalTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The withdrawal marks the published packages of the videogame, it deletes no row
    /// and it grows the version of the catalog (AC2, AC3).
    /// </summary>
    [Fact]
    public async Task The_withdrawal_marks_the_packages_and_grows_the_version_of_the_catalog()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        var gameId = await RegisterGameAsync(client);
        var packageId = await PublishPackageAsync(client, dataDirectory, gameId);

        // La publicación ya hizo crecer la versión del catálogo (AC3).
        var versionBefore = await ReadCatalogVersionAsync(host);

        Assert.Equal(1, versionBefore);

        var outcome = await WithdrawAsync(host, gameId);

        Assert.Equal(gameId, outcome.GameId);
        Assert.Equal(1, outcome.WithdrawnPackages);
        Assert.Equal(versionBefore + 1, outcome.CatalogVersion);

        // El paquete queda MARCADO, no borrado (AC2, AD-26).
        var row = ReadRow(dataDirectory, "PACKAGE_DESCRIPTOR", "packageId", packageId);

        Assert.False(string.IsNullOrWhiteSpace((string)row["withdrawnAt"]));

        // Y la fila sigue ahí: la retirada marca, no borra (AC2).
        Assert.Equal(1, ReadRowCount(dataDirectory, "PACKAGE_DESCRIPTOR"));
        Assert.Equal(1, ReadRowCount(dataDirectory, "GAME"));
    }

    /// <summary>
    /// The mark of the withdrawal does not touch the projection of the installations of
    /// the clients: the projection reflects what the client really has installed, not
    /// what the catalog publishes (AC2, AD-26).
    /// </summary>
    [Fact]
    public async Task The_withdrawal_does_not_delete_the_projection_of_the_installations()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        var gameId = await RegisterGameAsync(client);
        await PublishPackageAsync(client, dataDirectory, gameId);

        // Se persiste una proyección de la instalación de un cliente, como la que
        // alimentaría la vista de organización (AD-22).
        InsertClientInstallationView(dataDirectory, gameId);

        Assert.Equal(1, ReadRowCount(dataDirectory, "CLIENT_INSTALLATION_VIEW"));

        await WithdrawAsync(host, gameId);

        // La proyección sobrevive a la retirada: la instalación del cliente no se borra
        // porque el juego dejó de estar en el catálogo (AC2, AD-26).
        Assert.Equal(1, ReadRowCount(dataDirectory, "CLIENT_INSTALLATION_VIEW"));
    }

    /// <summary>
    /// The withdrawal does not delete the installation of any client, because the
    /// withdrawal only marks the packages of the catalog (AC2, AD-26).
    /// </summary>
    [Fact]
    public async Task The_withdrawal_does_not_delete_the_installation_of_any_client()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        var gameId = await RegisterGameAsync(client);
        await PublishPackageAsync(client, dataDirectory, gameId);

        InsertClientInstallationView(dataDirectory, gameId);

        var before = ReadRows(dataDirectory, "CLIENT_INSTALLATION_VIEW");

        await WithdrawAsync(host, gameId);

        var after = ReadRows(dataDirectory, "CLIENT_INSTALLATION_VIEW");

        Assert.Equal(before.Count, after.Count);

        foreach (var row in before)
        {
            Assert.Contains(
                after,
                candidate => string.Equals(
                    (string)candidate["clientId"],
                    (string)row["clientId"],
                    StringComparison.Ordinal)
                    && string.Equals(
                        (string)candidate["gameId"],
                        (string)row["gameId"],
                        StringComparison.Ordinal)
                    && string.Equals(
                        (string)candidate["installedVersion"],
                        (string)row["installedVersion"],
                        StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// A withdrawal of a videogame that does not exist writes nothing and does not grow
    /// the version of the catalog (AD-1, AD-25).
    /// </summary>
    [Fact]
    public async Task The_withdrawal_of_a_videogame_that_does_not_exist_writes_nothing()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        var versionBefore = await ReadCatalogVersionAsync(host);

        Assert.Equal(0, versionBefore);

        await using var scope = host.Application.Services.CreateAsyncScope();

        var useCase = scope.ServiceProvider.GetRequiredService<WithdrawGame>();

        var exception = await Assert.ThrowsAsync<GameWithdrawalNotFoundException>(
            () => useCase.ExecuteAsync(
                "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e99",
                TestContext.Current.CancellationToken));

        Assert.Equal("0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e99", exception.GameId);

        // El rechazo ocurre antes de cualquier escritura: la versión no crece (AC3).
        Assert.Equal(versionBefore, await ReadCatalogVersionAsync(host));
        Assert.Equal(0, ReadRowCount(dataDirectory, "PACKAGE_DESCRIPTOR"));
    }

    /// <summary>
    /// The version of the catalog grows with every publication and with every withdrawal,
    /// and it never goes back (AC3, AD-26).
    /// </summary>
    [Fact]
    public async Task The_version_of_the_catalog_grows_monotonically()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        var gameId = await RegisterGameAsync(client);

        Assert.Equal(0, await ReadCatalogVersionAsync(host));

        await PublishPackageAsync(client, dataDirectory, gameId);

        Assert.Equal(1, await ReadCatalogVersionAsync(host));

        await PublishPackageAsync(client, dataDirectory, gameId, packageSuffix: "b");

        Assert.Equal(2, await ReadCatalogVersionAsync(host));

        await WithdrawAsync(host, gameId);

        Assert.Equal(3, await ReadCatalogVersionAsync(host));

        await WithdrawAsync(host, gameId);

        Assert.Equal(4, await ReadCatalogVersionAsync(host));
    }

    /// <summary>
    /// The withdrawn package is still in the listing of the section of the packages, with
    /// the mark of the withdrawal: the listing shows what the catalog holds, and the
    /// withdrawal marks (AC1, AC2, AD-26).
    /// </summary>
    [Fact]
    public async Task The_listing_shows_the_withdrawn_package_with_its_mark()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        var gameId = await RegisterGameAsync(client);
        await PublishPackageAsync(client, dataDirectory, gameId);

        await using var scope = host.Application.Services.CreateAsyncScope();

        var listPackages = scope.ServiceProvider.GetRequiredService<ListPackages>();
        var withdrawGame = scope.ServiceProvider.GetRequiredService<WithdrawGame>();

        var before = await listPackages.ExecuteAsync(TestContext.Current.CancellationToken);

        var published = Assert.Single(before);

        Assert.Equal(gameId, published.GameId);
        Assert.Null(published.WithdrawnAt);

        await withdrawGame.ExecuteAsync(gameId, TestContext.Current.CancellationToken);

        var after = await listPackages.ExecuteAsync(TestContext.Current.CancellationToken);

        var withdrawn = Assert.Single(after);

        Assert.Equal(gameId, withdrawn.GameId);
        Assert.False(string.IsNullOrWhiteSpace(withdrawn.WithdrawnAt));

        // La fila enriquecida lleva todo lo que la organizadora necesita ver (AC1).
        Assert.False(string.IsNullOrWhiteSpace(withdrawn.GameName));
        Assert.False(string.IsNullOrWhiteSpace(withdrawn.License));
        Assert.False(string.IsNullOrWhiteSpace(withdrawn.DistributionRestrictions));
        Assert.True(withdrawn.SizeBytes > 0);
    }

    /// <summary>
    /// The videogame can be published again after the withdrawal: the withdrawal marks
    /// the packages, it does not close the videogame for ever (AC2, AD-26).
    /// </summary>
    [Fact]
    public async Task The_videogame_can_be_published_again_after_the_withdrawal()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        var gameId = await RegisterGameAsync(client);
        await PublishPackageAsync(client, dataDirectory, gameId);

        await WithdrawAsync(host, gameId);

        // La reincorporación es publicar una versión nueva del paquete (AC2, AD-26).
        await PublishPackageAsync(client, dataDirectory, gameId, packageSuffix: "b");

        Assert.Equal(2, ReadRowCount(dataDirectory, "PACKAGE_DESCRIPTOR"));
        Assert.Equal(1, ReadRowCount(dataDirectory, "GAME"));

        var version = await ReadCatalogVersionAsync(host);

        Assert.Equal(3, version);
    }

    /// <summary>
    /// Withdraws a videogame through the REAL use case, resolved from the container of
    /// the host (AD-1).
    /// </summary>
    /// <param name="host">The real host.</param>
    /// <param name="gameId">Identifier of the videogame to withdraw.</param>
    /// <returns>The outcome of the withdrawal.</returns>
    private static async Task<WithdrawalOutcome> WithdrawAsync(TestServer host, string gameId)
    {
        await using var scope = host.Application.Services.CreateAsyncScope();

        var useCase = scope.ServiceProvider.GetRequiredService<WithdrawGame>();

        return await useCase.ExecuteAsync(gameId, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Reads the version of the catalog through the REAL port, resolved from the
    /// container of the host (AC3).
    /// </summary>
    /// <param name="host">The real host.</param>
    /// <returns>The version of the catalog.</returns>
    private static async Task<long> ReadCatalogVersionAsync(TestServer host)
    {
        await using var scope = host.Application.Services.CreateAsyncScope();

        var repository = scope.ServiceProvider.GetRequiredService<IPackageRepository>();

        return await repository.GetCatalogVersionAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Registers a videogame in the catalog and returns its identifier.
    /// </summary>
    /// <param name="client">Client of the host.</param>
    /// <returns>The identifier of the videogame.</returns>
    private static async Task<string> RegisterGameAsync(HttpClient client)
    {
        var request = new GameRequest
        {
            SchemaVersion = GamesEndpoint.SupportedSchemaVersion,
            Game = new GameDescriptor(
                string.Empty,
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
                new InstallationManifest.LaunchSettings("bin\\cs2.exe", ["-novid"], "game\\bin"),
                ["DirectX 12"],
                "Propietaria",
                "Solo para el evento"),
        };

        using var response = await client.PostAsync(
            GamesEndpoint.Route,
            BuildBody(request),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var descriptor = JsonSerializer.Deserialize<GameDescriptor>(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            SerializerOptions);

        Assert.NotNull(descriptor);

        return descriptor!.GameId;
    }

    /// <summary>
    /// Publishes a package of the videogame and returns its identifier.
    /// </summary>
    /// <param name="client">Client of the host.</param>
    /// <param name="dataDirectory">Folder of the data of the host.</param>
    /// <param name="gameId">Identifier of the videogame the package belongs to.</param>
    /// <param name="packageSuffix">Suffix that tells the packages of the test apart.</param>
    /// <returns>The identifier of the published package.</returns>
    private static async Task<string> PublishPackageAsync(
        HttpClient client,
        string dataDirectory,
        string gameId,
        string packageSuffix = "a")
    {
        var packageId = $"0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5f{packageSuffix}0";

        var bytes = WritePackageBytes(dataDirectory, packageId, "contenido del paquete");

        var request = new PackageRequest
        {
            SchemaVersion = PackagesEndpoint.SupportedSchemaVersion,
            GameId = gameId,
            Package = new PackageDescriptor(
                packageId,
                gameId,
                "1.0.0",
                bytes.LongLength,
                0,
                string.Empty,
                string.Empty),
            Manifest = new InstallationManifest(
                InstallationManifestSchema.Version,
                packageId,
                "1.0.0",
                bytes.LongLength,
                string.Empty,
                new InstallationManifest.LaunchSettings("game\\game.exe", ["-novid"], "game"),
                [new InstallationManifest.CreateDirectory("game")]),
        };

        using var response = await client.PostAsync(
            PackagesEndpoint.Route,
            BuildBody(request),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return packageId;
    }

    /// <summary>
    /// Persists a projection of the installation of a client, como la que alimentaría la
    /// vista de organización (AD-22).
    /// </summary>
    /// <param name="dataDirectory">Folder of the data of the host.</param>
    /// <param name="gameId">Identifier of the videogame that the client has installed.</param>
    private static void InsertClientInstallationView(string dataDirectory, string gameId)
    {
        using var connection = OpenDatabase(dataDirectory);

        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO CLIENT_REGISTRATION (clientId, machineName, clientVersion, protocolVersion, connectionState, lastSeenAt) "
            + "VALUES ($clientId, $machineName, '1.0.0', 1, 'AnchorVerified', '2026-10-29T19:50:23.0000000Z'); "
            + "INSERT INTO CLIENT_INSTALLATION_VIEW (clientId, gameId, installedVersion, validationStateText) "
            + "VALUES ($clientId, $gameId, '1.0.0', 'Instalado');";
        command.Parameters.AddWithValue("$clientId", "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e77");
        command.Parameters.AddWithValue("$machineName", "EQ-PC");
        command.Parameters.AddWithValue("$gameId", gameId);

        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Writes the bytes of a package in the repository of packages of the server, so the
    /// publication has a real content whose hash is signed (AD-2).
    /// </summary>
    /// <param name="dataDirectory">Folder of the data of the host.</param>
    /// <param name="packageId">Identifier of the package.</param>
    /// <param name="content">Content of the package.</param>
    /// <returns>The bytes of the package.</returns>
    private static byte[] WritePackageBytes(string dataDirectory, string packageId, string content)
    {
        var packages = Path.Combine(dataDirectory, DataDirectory.PackagesFolderName);

        Directory.CreateDirectory(packages);

        var bytes = System.Text.Encoding.UTF8.GetBytes(content);

        File.WriteAllBytes(Path.Combine(packages, packageId), bytes);

        return bytes;
    }

    /// <summary>
    /// Builds the body of the request with the JSON que el servidor espera.
    /// </summary>
    /// <param name="request">Request of the publication.</param>
    /// <returns>The content of the request.</returns>
    private static StringContent BuildBody<T>(T request) =>
        new(JsonSerializer.Serialize(request, SerializerOptions), System.Text.Encoding.UTF8, "application/json");

    /// <summary>
    /// Reads the row of a table of the database of the server.
    /// </summary>
    /// <param name="dataDirectory">Folder of the data of the host.</param>
    /// <param name="table">Name of the table.</param>
    /// <param name="keyColumn">Name of the column of the lookup.</param>
    /// <param name="key">Value of the lookup.</param>
    /// <returns>The row, keyed by the name of the column.</returns>
    private static IReadOnlyDictionary<string, object> ReadRow(
        string dataDirectory,
        string table,
        string keyColumn,
        string key)
    {
        var rows = ReadRows(dataDirectory, table, keyColumn, key);

        return Assert.Single(rows);
    }

    /// <summary>
    /// Reads the rows of a table of the database of the server.
    /// </summary>
    /// <param name="dataDirectory">Folder of the data of the host.</param>
    /// <param name="table">Name of the table.</param>
    /// <param name="keyColumn">Name of the column of the lookup, or <c>null</c> for every row.</param>
    /// <param name="key">Value of the lookup.</param>
    /// <returns>The rows, keyed by the name of the column.</returns>
    private static IReadOnlyList<IReadOnlyDictionary<string, object>> ReadRows(
        string dataDirectory,
        string table,
        string? keyColumn = null,
        string? key = null)
    {
        using var connection = OpenDatabase(dataDirectory);

        using var reader = connection.CreateCommand();
        reader.CommandText = keyColumn is null
            ? $"SELECT * FROM {table}"
            : $"SELECT * FROM {table} WHERE {keyColumn} = $key";

        if (keyColumn is not null)
        {
            reader.Parameters.AddWithValue("$key", key);
        }

        var rows = new List<IReadOnlyDictionary<string, object>>();

        using var results = reader.ExecuteReader();

        while (results.Read())
        {
            var row = new Dictionary<string, object>(StringComparer.Ordinal);

            for (var index = 0; index < results.FieldCount; index++)
            {
                row[results.GetName(index)] = results.GetValue(index);
            }

            rows.Add(row);
        }

        return rows;
    }

    /// <summary>
    /// Counts the rows of a table of the database of the server.
    /// </summary>
    /// <param name="dataDirectory">Folder of the data of the host.</param>
    /// <param name="table">Name of the table.</param>
    /// <returns>The number of rows of the table.</returns>
    private static int ReadRowCount(string dataDirectory, string table)
    {
        using var connection = OpenDatabase(dataDirectory);

        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}";

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
