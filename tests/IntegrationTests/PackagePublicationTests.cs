using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using LANPartyHub.Server.Host;
using LANPartyHub.Server.Infrastructure.Persistence;
using LANPartyHub.Server.Infrastructure.Signing;
using LANPartyHub.Shared.Contracts;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LANPartyHub.IntegrationTests;

/// <summary>
/// The publication of the packages of the event against the real host and the real
/// SQLite file (AD-16, AC1, AC2, AC3, AC4, AC6).
///
/// <para>
/// The tests start the real host, with the real Kestrel, the real sockets and a real
/// <c>catalog.sqlite</c> in a file, because that is the only way to observe what the
/// server persists and what it answers. Every row of the matrix of edges of the story is
/// covered here: the publication that succeeds, the two blocks of the publication, the
/// videogame that does not exist, the package that is already published, the operation
/// that is not allowed, the bytes that are missing, the body without
/// <c>schemaVersion</c> and the failure of the persistence.
/// </para>
/// </summary>
[Collection(RealHostCollection.Name)]
public sealed class PackagePublicationTests
{
    /// <summary>
    /// The seven columns of the table <c>PACKAGE_DESCRIPTOR</c>, with the name and the
    /// order of AD-7. The table and the contract cannot diverge (AD-7, AD-22).
    /// </summary>
    private const string ExpectedDescriptorColumns =
        "packageId,gameId,version,sizeBytes,chunkSizeBytes,sha256,manifestRef";

    /// <summary>
    /// The six columns of the table <c>INSTALLATION_MANIFEST</c>, with the name and the
    /// order of AD-22.
    /// </summary>
    private const string ExpectedManifestColumns =
        "manifestId,packageId,schemaVersion,signature,keyId,canonicalManifest";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task The_publication_persists_the_descriptor_and_the_signed_manifest()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        var packageId = "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e40";
        var bytes = WritePackageBytes(dataDirectory, packageId, "contenido del paquete");
        var gameId = await RegisterGameAsync(client);

        using var response = await client.PostAsync(
            PackagesEndpoint.Route,
            BuildBody(BuildValidRequest(packageId, bytes.LongLength, gameId)),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var descriptor = await ReadDescriptorAsync(response, TestContext.Current.CancellationToken);

        // The seven columns of the table are the seven fields of the descriptor, with the
        // same name and in the same order (AD-7, AD-22).
        Assert.Equal(
            ExpectedDescriptorColumns.Split(','),
            ReadColumnNames(dataDirectory, "SELECT name FROM pragma_table_info('PACKAGE_DESCRIPTOR')"));

        // The six columns of the manifest carry the signature and the canonical text
        // (AD-2, AD-22).
        Assert.Equal(
            ExpectedManifestColumns.Split(','),
            ReadColumnNames(dataDirectory, "SELECT name FROM pragma_table_info('INSTALLATION_MANIFEST')"));

        var descriptorRow = ReadRow(dataDirectory, "PACKAGE_DESCRIPTOR", "packageId", packageId);

        Assert.Equal(packageId, descriptorRow["packageId"]);
        Assert.Equal("1.0.0", descriptorRow["version"]);
        Assert.Equal(bytes.LongLength, descriptorRow["sizeBytes"]);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            descriptorRow["sha256"]);

        // The descriptor does not transport a signature: the signature lives only in the
        // envelope of the manifest (AD-2, AD-7).
        Assert.DoesNotContain("signature", ExpectedDescriptorColumns, StringComparison.OrdinalIgnoreCase);

        var manifestRow = ReadRow(dataDirectory, "INSTALLATION_MANIFEST", "packageId", packageId);

        Assert.False(string.IsNullOrWhiteSpace((string)manifestRow["signature"]));
        Assert.False(string.IsNullOrWhiteSpace((string)manifestRow["keyId"]));
        Assert.False(string.IsNullOrWhiteSpace((string)manifestRow["canonicalManifest"]));
    }

    [Fact]
    public async Task The_persisted_signature_verifies_over_the_persisted_canonical_manifest()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        var packageId = "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e41";
        var bytes = WritePackageBytes(dataDirectory, packageId, "otro contenido del paquete");
        var gameId = await RegisterGameAsync(client);

        using var response = await client.PostAsync(
            PackagesEndpoint.Route,
            BuildBody(BuildValidRequest(packageId, bytes.LongLength, gameId)),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var manifestRow = ReadRow(dataDirectory, "INSTALLATION_MANIFEST", "packageId", packageId);

        var canonical = (string)manifestRow["canonicalManifest"];
        var signature = (string)manifestRow["signature"];

        // The signature covers exactly the canonical text that is persisted, so a
        // verifier recomputes the same bytes and the signature verifies (AD-2).
        Assert.True(Ed25519ManifestSigner.Verify(canonical, signature, Environment.MachineName));

        // The canonical text is the canonical form of the manifest: it carries the
        // package identifier and the hash of the bytes, so the correspondence between the
        // manifest and the package is signed and not assumed (AD-2, AD-7).
        Assert.Contains(packageId, canonical, StringComparison.Ordinal);
        Assert.Contains(
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            canonical,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_publication_is_blocked_without_the_license()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        var gameId = await RegisterGameAsync(client, license: string.Empty);
        var packageId = "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e42";
        var bytes = WritePackageBytes(dataDirectory, packageId, "contenido");

        using var response = await client.PostAsync(
            PackagesEndpoint.Route,
            BuildBody(BuildValidRequest(packageId, bytes.LongLength, gameId)),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        AssertRejected(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            ErrorCatalog.PackageLicenseMissingCode);

        // The block happens before any write: no row of the descriptor and no row of the
        // manifest (AC1).
        Assert.Equal(0, ReadRowCount(dataDirectory, "PACKAGE_DESCRIPTOR"));
        Assert.Equal(0, ReadRowCount(dataDirectory, "INSTALLATION_MANIFEST"));
    }

    [Fact]
    public async Task The_publication_is_blocked_without_the_restrictions_of_distribution()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        var gameId = await RegisterGameAsync(client, distributionRestrictions: string.Empty);
        var packageId = "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e43";
        var bytes = WritePackageBytes(dataDirectory, packageId, "contenido");

        using var response = await client.PostAsync(
            PackagesEndpoint.Route,
            BuildBody(BuildValidRequest(packageId, bytes.LongLength, gameId)),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        AssertRejected(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            ErrorCatalog.PackageDistributionRestrictionsMissingCode);

        Assert.Equal(0, ReadRowCount(dataDirectory, "PACKAGE_DESCRIPTOR"));
        Assert.Equal(0, ReadRowCount(dataDirectory, "INSTALLATION_MANIFEST"));
    }

    [Fact]
    public async Task The_publication_of_a_videogame_that_does_not_exist_is_rejected_without_writing_a_row()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        var packageId = "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e44";
        var bytes = WritePackageBytes(dataDirectory, packageId, "contenido");

        using var response = await client.PostAsync(
            PackagesEndpoint.Route,
            BuildBody(BuildValidRequest(
                packageId,
                bytes.LongLength,
                "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e99")),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        AssertRejected(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            ErrorCatalog.PackageGameNotFoundCode);

        Assert.Equal(0, ReadRowCount(dataDirectory, "PACKAGE_DESCRIPTOR"));
        Assert.Equal(0, ReadRowCount(dataDirectory, "INSTALLATION_MANIFEST"));
    }

    [Fact]
    public async Task A_published_package_is_immutable_and_republishing_it_does_not_modify_the_row()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        var packageId = "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e45";
        var bytes = WritePackageBytes(dataDirectory, packageId, "contenido");
        var gameId = await RegisterGameAsync(client);

        using var first = await client.PostAsync(
            PackagesEndpoint.Route,
            BuildBody(BuildValidRequest(packageId, bytes.LongLength, gameId)),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var before = ReadRow(dataDirectory, "PACKAGE_DESCRIPTOR", "packageId", packageId);

        // The republication names a package that is already published: it is rejected and
        // the row that exists is not modified (AD-7).
        using var second = await client.PostAsync(
            PackagesEndpoint.Route,
            BuildBody(BuildValidRequest(packageId, bytes.LongLength, gameId, version: "2.0.0")),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        AssertRejected(
            await second.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            ErrorCatalog.PackageAlreadyPublishedCode);

        var after = ReadRow(dataDirectory, "PACKAGE_DESCRIPTOR", "packageId", packageId);

        Assert.Equal(before["version"], after["version"]);
        Assert.Equal(before["sha256"], after["sha256"]);
        Assert.Equal(1, ReadRowCount(dataDirectory, "PACKAGE_DESCRIPTOR"));
        Assert.Equal(1, ReadRowCount(dataDirectory, "INSTALLATION_MANIFEST"));
    }

    [Fact]
    public async Task A_manifest_with_an_operation_that_is_not_allowed_is_rejected_without_writing_a_row()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        var packageId = "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e46";
        var bytes = WritePackageBytes(dataDirectory, packageId, "contenido");
        var gameId = await RegisterGameAsync(client);

        var request = BuildValidRequest(packageId, bytes.LongLength, gameId);

        // The operation is not one of the six of the closed set, so it cannot be read
        // into the contract of Shared, and the publication is rejected before any write
        // (AD-7, AD-8).
        var payload = JsonSerializer.Serialize(request, SerializerOptions);

        payload = payload.Replace("\"createDirectory\"", "\"runArbitraryCommand\"", StringComparison.Ordinal);

        using var response = await client.PostAsync(
            PackagesEndpoint.Route,
            new StringContent(payload, System.Text.Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        AssertRejected(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            ErrorCatalog.ManifestOperationNotAllowedCode);

        Assert.Equal(0, ReadRowCount(dataDirectory, "PACKAGE_DESCRIPTOR"));
        Assert.Equal(0, ReadRowCount(dataDirectory, "INSTALLATION_MANIFEST"));
    }

    [Fact]
    public async Task A_package_without_bytes_is_rejected_without_writing_a_row()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        // The bytes of the package are never written in the repository of the server, so
        // the publication is rejected (AC6).
        var packageId = "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e47";
        var gameId = await RegisterGameAsync(client);

        using var response = await client.PostAsync(
            PackagesEndpoint.Route,
            BuildBody(BuildValidRequest(packageId, 128, gameId)),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        AssertRejected(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            ErrorCatalog.PackageContentMismatchCode);

        Assert.Equal(0, ReadRowCount(dataDirectory, "PACKAGE_DESCRIPTOR"));
        Assert.Equal(0, ReadRowCount(dataDirectory, "INSTALLATION_MANIFEST"));
    }

    [Fact]
    public async Task A_package_whose_bytes_do_not_match_the_declared_size_is_rejected_without_writing_a_row()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        var packageId = "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e48";
        var bytes = WritePackageBytes(dataDirectory, packageId, "contenido");
        var gameId = await RegisterGameAsync(client);

        // The manifest declares a size that is not the size of the bytes that are in the
        // repository, so the correspondence between the manifest and the package is not
        // signed and the publication is rejected (AC6).
        using var response = await client.PostAsync(
            PackagesEndpoint.Route,
            BuildBody(BuildValidRequest(packageId, bytes.LongLength + 1, gameId)),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        AssertRejected(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            ErrorCatalog.PackageContentMismatchCode);

        Assert.Equal(0, ReadRowCount(dataDirectory, "PACKAGE_DESCRIPTOR"));
    }

    [Fact]
    public async Task A_body_without_the_schema_version_is_rejected_without_writing_a_row()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        var packageId = "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e49";
        var bytes = WritePackageBytes(dataDirectory, packageId, "contenido");
        var gameId = await RegisterGameAsync(client);

        var request = BuildValidRequest(packageId, bytes.LongLength, gameId);
        request.SchemaVersion = null;

        using var response = await client.PostAsync(
            PackagesEndpoint.Route,
            BuildBody(request),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        AssertRejected(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            ErrorCatalog.SchemaVersionMissingCode);

        Assert.Equal(0, ReadRowCount(dataDirectory, "PACKAGE_DESCRIPTOR"));
    }

    [Fact]
    public async Task A_body_with_an_unsupported_schema_version_is_rejected_without_writing_a_row()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        var packageId = "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e50";
        var bytes = WritePackageBytes(dataDirectory, packageId, "contenido");
        var gameId = await RegisterGameAsync(client);

        var request = BuildValidRequest(packageId, bytes.LongLength, gameId);
        request.SchemaVersion = PackagesEndpoint.SupportedSchemaVersion + 1;

        using var response = await client.PostAsync(
            PackagesEndpoint.Route,
            BuildBody(request),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        AssertRejected(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            ErrorCatalog.SchemaVersionNotSupportedCode);

        Assert.Equal(0, ReadRowCount(dataDirectory, "PACKAGE_DESCRIPTOR"));
    }

    [Fact]
    public async Task A_failure_of_the_persistence_is_answered_with_the_error_of_the_catalog()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        var packageId = "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e51";
        var bytes = WritePackageBytes(dataDirectory, packageId, "contenido");
        var gameId = await RegisterGameAsync(client);

        // The persistence of the descriptor fails: a trigger of the database raises the
        // failure of the write, which is the failure that the endpoint has to answer with
        // the error of the catalog and never with the exception (AD-15).
        using (var connection = OpenDatabase(dataDirectory))
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                "CREATE TRIGGER fail_descriptor_insert BEFORE INSERT ON PACKAGE_DESCRIPTOR "
                + "BEGIN SELECT RAISE(ABORT, 'the persistence of the descriptor fails'); END";
            command.ExecuteNonQuery();
        }

        using var response = await client.PostAsync(
            PackagesEndpoint.Route,
            BuildBody(BuildValidRequest(packageId, bytes.LongLength, gameId)),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        AssertRejected(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            ErrorCatalog.UnknownErrorCode);

        // The failure of the write leaves no row behind (AD-15).
        Assert.Equal(0, ReadRowCount(dataDirectory, "PACKAGE_DESCRIPTOR"));
        Assert.Equal(0, ReadRowCount(dataDirectory, "INSTALLATION_MANIFEST"));
    }

    /// <summary>
    /// Registers a videogame in the catalog and returns its identifier.
    /// </summary>
    /// <param name="client">Client of the host.</param>
    /// <param name="license">License of the videogame.</param>
    /// <param name="distributionRestrictions">Restrictions of distribution of the videogame.</param>
    /// <returns>The identifier of the videogame.</returns>
    private static async Task<string> RegisterGameAsync(
        HttpClient client,
        string license = "Propietaria",
        string distributionRestrictions = "Solo para el evento")
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
                license,
                distributionRestrictions),
        };

        using var response = await client.PostAsync(
            GamesEndpoint.Route,
            BuildBody(request),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var descriptor = await ReadDescriptorAsync(response, TestContext.Current.CancellationToken);

        return descriptor.GameId;
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
    /// Builds a body of the publication with the data of the package and of its manifest.
    /// </summary>
    /// <param name="packageId">Identifier of the package.</param>
    /// <param name="sizeBytes">Exact size of the package in bytes.</param>
    /// <param name="gameId">Identifier of the videogame the package belongs to.</param>
    /// <param name="version">Opaque version string of the publisher.</param>
    /// <returns>The request of the publication.</returns>
    private static PackageRequest BuildValidRequest(
        string packageId,
        long sizeBytes,
        string gameId,
        string version = "1.0.0") => new()
    {
        SchemaVersion = PackagesEndpoint.SupportedSchemaVersion,
        GameId = gameId,
        Package = new PackageDescriptor(
            packageId,
            gameId,
            version,
            sizeBytes,
            0,
            string.Empty,
            "manifest-ref"),
        Manifest = new InstallationManifest(
            1,
            packageId,
            version,
            sizeBytes,
            string.Empty,
            new InstallationManifest.LaunchSettings("game\\game.exe", ["-novid"], "game"),
            [
                new InstallationManifest.CreateDirectory("game"),
                new InstallationManifest.Extract("game.zip", "game"),
            ]),
    };

    /// <summary>
    /// Builds the body of the request with the JSON que el servidor espera.
    /// </summary>
    /// <param name="request">Request of the publication.</param>
    /// <returns>The content of the request.</returns>
    private static StringContent BuildBody<T>(T request) =>
        new(JsonSerializer.Serialize(request, SerializerOptions), System.Text.Encoding.UTF8, "application/json");

    /// <summary>
    /// Reads the contract of the package of a response.
    /// </summary>
    /// <param name="response">Response of the host.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The contract of the package.</returns>
    private static async Task<PackageDescriptor> ReadDescriptorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        var descriptor = JsonSerializer.Deserialize<PackageDescriptor>(payload, SerializerOptions);

        Assert.NotNull(descriptor);

        return descriptor!;
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
        using var connection = OpenDatabase(dataDirectory);

        using var reader = connection.CreateCommand();
        reader.CommandText = $"SELECT * FROM {table} WHERE {keyColumn} = $key";
        reader.Parameters.AddWithValue("$key", key);

        var row = new Dictionary<string, object>(StringComparer.Ordinal);

        using var results = reader.ExecuteReader();

        Assert.True(results.Read(), $"The row of the table '{table}' was not written.");

        for (var index = 0; index < results.FieldCount; index++)
        {
            row[results.GetName(index)] = results.GetValue(index);
        }

        return row;
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
