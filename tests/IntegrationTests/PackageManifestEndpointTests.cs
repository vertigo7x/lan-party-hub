using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using LANPartyHub.Server.Application.Packages;
using LANPartyHub.Server.Host;
using LANPartyHub.Server.Infrastructure.Http;
using LANPartyHub.Server.Infrastructure.Persistence;
using LANPartyHub.Server.Infrastructure.Signing;
using LANPartyHub.Shared.Contracts;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LANPartyHub.IntegrationTests;

/// <summary>
/// The service of the signed manifest of a package against the real host (AD-2, AD-8,
/// AD-20, AD-16).
///
/// <para>
/// El test arranca el host real, con el Kestrel real y un <c>catalog.sqlite</c> real,
/// porque lo que se observa es lo que el servidor PERSISTE y lo que CONTESTA: el texto
/// canónico que firmó y la firma que lo cubre (AD-2).
/// </para>
/// </summary>
[Collection(RealHostCollection.Name)]
public sealed class PackageManifestEndpointTests
{
    /// <summary>
    /// Identifier of the team that asks for the manifests (AD-23).
    /// </summary>
    private const string ClientId = "0193f2a1b2c34d4e5f60718293a4b5c6";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task The_composition_root_resolves_the_use_case_of_the_signed_manifest()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        await using var scope = host.Application.Services.CreateAsyncScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<GetSignedManifest>());
    }

    [Fact]
    public async Task The_server_serves_the_canonical_manifest_with_its_signature_and_its_key()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        var packageId = "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5f01";
        var bytes = WritePackageBytes(dataDirectory, packageId, "contenido del paquete");
        var gameId = await RegisterGameAsync(client);

        await PublishAsync(client, packageId, bytes.LongLength, gameId);

        using var response = await GetManifestAsync(client, packageId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var envelope = await ReadEnvelopeAsync(response, TestContext.Current.CancellationToken);

        // Los tres elementos del SOBRE firmado: el texto canónico, la firma y la clave
        // (AD-2).
        Assert.False(string.IsNullOrWhiteSpace(envelope.Manifest));
        Assert.False(string.IsNullOrWhiteSpace(envelope.Signature));
        Assert.False(string.IsNullOrWhiteSpace(envelope.KeyId));

        // El texto que sirve el servidor es EXACTAMENTE el que firmó, y la firma
        // valida sobre él: los bytes que el cliente verifica son los bytes que la firma
        // cubre (AD-2).
        Assert.True(Ed25519ManifestSigner.Verify(envelope.Manifest, envelope.Signature, Environment.MachineName));

        // El texto canónico es el que el servidor PERSISTió, byte a byte (AD-2).
        var persisted = ReadRow(dataDirectory, "INSTALLATION_MANIFEST", "packageId", packageId);

        Assert.Equal(persisted["canonicalManifest"] as string, envelope.Manifest);
        Assert.Equal(persisted["signature"] as string, envelope.Signature);
        Assert.Equal(persisted["keyId"] as string, envelope.KeyId);
    }

    [Fact]
    public async Task The_served_manifest_carries_the_identity_and_the_hash_of_the_package()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        var packageId = "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5f02";
        var bytes = WritePackageBytes(dataDirectory, packageId, "otro contenido del paquete");
        var gameId = await RegisterGameAsync(client);

        await PublishAsync(client, packageId, bytes.LongLength, gameId);

        using var response = await GetManifestAsync(client, packageId);

        var envelope = await ReadEnvelopeAsync(response, TestContext.Current.CancellationToken);

        // El manifiesto canónico lleva la identidad del paquete y el hash de sus bytes,
        // de modo que la correspondencia entre el manifiesto y el paquete está FIRMADA y
        // no supuesta (AD-2, AD-7).
        Assert.Contains(packageId, envelope.Manifest, StringComparison.Ordinal);
        Assert.Contains(
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            envelope.Manifest,
            StringComparison.Ordinal);
        Assert.Contains("\"schemaVersion\":1", envelope.Manifest, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_manifest_is_readable_back_by_the_contract_of_shared()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        var packageId = "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5f03";
        var bytes = WritePackageBytes(dataDirectory, packageId, "contenido legible");
        var gameId = await RegisterGameAsync(client);

        await PublishAsync(client, packageId, bytes.LongLength, gameId);

        using var response = await GetManifestAsync(client, packageId);

        var envelope = await ReadEnvelopeAsync(response, TestContext.Current.CancellationToken);

        // El texto canónico que el servidor firmó se LEE con el deserializador del
        // contrato, así que el cliente puede leer la versión del esquema que declara
        // (AD-2, AD-8).
        var manifest = JsonSerializer.Deserialize<InstallationManifest>(envelope.Manifest, SerializerOptions);

        Assert.NotNull(manifest);
        Assert.Equal(packageId, manifest!.PackageId);
        Assert.Equal(InstallationManifestSchema.Version, manifest.SchemaVersion);
        Assert.Equal(bytes.LongLength, manifest.SizeBytes);
        Assert.NotEmpty(manifest.Operations);
    }

    [Fact]
    public async Task A_package_that_is_not_published_has_no_manifest()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        using var response = await GetManifestAsync(client, "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5f99");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        AssertRejected(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            ErrorCatalog.DownloadPackageNotFoundCode);
    }

    [Fact]
    public async Task The_manifest_travels_in_a_separate_route_from_the_bytes_of_the_package()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        var packageId = "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5f04";
        var bytes = WritePackageBytes(dataDirectory, packageId, "contenido aparte");
        var gameId = await RegisterGameAsync(client);

        await PublishAsync(client, packageId, bytes.LongLength, gameId);

        // El manifiesto y los bytes son DOS artefactos: el manifiesto va en su propia ruta
        // y los bytes en la del contenido (AD-20).
        using var manifest = await GetManifestAsync(client, packageId);

        using var content = await GetContentAsync(client, packageId);

        Assert.Equal(HttpStatusCode.OK, manifest.StatusCode);
        Assert.Equal(HttpStatusCode.OK, content.StatusCode);

        Assert.Equal(bytes, await content.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Builds the route of the signed manifest of a package (AD-20).
    /// </summary>
    /// <param name="packageId">Identifier of the package.</param>
    /// <returns>The route of the signed manifest.</returns>
    private static string BuildManifestRoute(string packageId) =>
        PackageManifestEndpoint.Route.Replace("{packageId}", packageId);

    /// <summary>
    /// Asks the host for the signed manifest of a package, with the headers that la
    /// superficie del cliente EXIGE: la versión del cliente, la versión del protocolo y el
    /// identificador del equipo (AD-6, AD-15, AD-23).
    /// </summary>
    /// <param name="client">Client of the host.</param>
    /// <param name="packageId">Identifier of the package.</param>
    /// <returns>The response of the host.</returns>
    private static async Task<HttpResponseMessage> GetManifestAsync(HttpClient client, string packageId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildManifestRoute(packageId));

        request.Headers.TryAddWithoutValidation(CorrelationMiddleware.ClientIdHeaderName, ClientId);
        request.Headers.TryAddWithoutValidation(ClientVersionMiddleware.ClientVersionHeaderName, "1.0.0");
        request.Headers.TryAddWithoutValidation(
            ClientVersionMiddleware.ProtocolVersionHeaderName,
            VersionInfo.CurrentProtocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Asks the host for the bytes of a package, with the headers that la superficie del
    /// cliente EXIGE (AD-6, AD-15, AD-23).
    /// </summary>
    /// <param name="client">Client of the host.</param>
    /// <param name="packageId">Identifier of the package.</param>
    /// <returns>The response of the host.</returns>
    private static async Task<HttpResponseMessage> GetContentAsync(HttpClient client, string packageId)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/packages/{packageId}/content");

        request.Headers.TryAddWithoutValidation(CorrelationMiddleware.ClientIdHeaderName, ClientId);
        request.Headers.TryAddWithoutValidation(ClientVersionMiddleware.ClientVersionHeaderName, "1.0.0");
        request.Headers.TryAddWithoutValidation(
            ClientVersionMiddleware.ProtocolVersionHeaderName,
            VersionInfo.CurrentProtocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Reads the signed envelope of a response (AD-2).
    /// </summary>
    /// <param name="response">Response of the host.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The signed envelope.</returns>
    private static async Task<InstallationManifest.SignedEnvelope> ReadEnvelopeAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        using var document = JsonDocument.Parse(payload);

        var root = document.RootElement;

        return new InstallationManifest.SignedEnvelope(
            root.GetProperty("manifest").GetString() ?? string.Empty,
            root.GetProperty("signature").GetString() ?? string.Empty,
            root.GetProperty("keyId").GetString() ?? string.Empty);
    }

    /// <summary>
    /// Registers a videogame in the catalog of the event.
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
            new StringContent(
                JsonSerializer.Serialize(request, SerializerOptions),
                System.Text.Encoding.UTF8,
                "application/json"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var payload = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        using var document = JsonDocument.Parse(payload);

        return document.RootElement.GetProperty("gameId").GetString() ?? string.Empty;
    }

    /// <summary>
    /// Publishes a package in the catalog of the event.
    /// </summary>
    /// <param name="client">Client of the host.</param>
    /// <param name="packageId">Identifier of the package.</param>
    /// <param name="sizeBytes">Exact size of the package.</param>
    /// <param name="gameId">Identifier of the videogame.</param>
    /// <returns>A task that completes when the package is published.</returns>
    private static async Task PublishAsync(HttpClient client, string packageId, long sizeBytes, string gameId)
    {
        var request = new PackageRequest
        {
            SchemaVersion = PackagesEndpoint.SupportedSchemaVersion,
            GameId = gameId,
            Package = new PackageDescriptor(
                packageId,
                gameId,
                "1.0.0",
                sizeBytes,
                0,
                string.Empty,
                string.Empty),
            Manifest = new InstallationManifest(
                InstallationManifestSchema.Version,
                packageId,
                "1.0.0",
                sizeBytes,
                string.Empty,
                new InstallationManifest.LaunchSettings("game\\game.exe", ["-novid"], "game"),
                [
                    new InstallationManifest.CreateDirectory("game"),
                    new InstallationManifest.Extract("game.zip", "game"),
                ]),
        };

        using var response = await client.PostAsync(
            PackagesEndpoint.Route,
            new StringContent(
                JsonSerializer.Serialize(request, SerializerOptions),
                System.Text.Encoding.UTF8,
                "application/json"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    /// <summary>
    /// Writes the bytes of a package in the repository of packages of the server (AD-2).
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
    /// Asserts that the body of a rejection is the single shape of error of the system,
    /// taken from the catalog (AD-15, AD-25).
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
    /// Reads a row of the database of the server.
    /// </summary>
    /// <param name="dataDirectory">Folder of the data of the host.</param>
    /// <param name="table">Name of the table.</param>
    /// <param name="keyColumn">Name of the column of the key.</param>
    /// <param name="key">Value of the key.</param>
    /// <returns>The row, as a dictionary of its columns.</returns>
    private static Dictionary<string, object?> ReadRow(
        string dataDirectory,
        string table,
        string keyColumn,
        string key)
    {
        using var connection = new SqliteConnection(
            $"Data Source={Path.Combine(dataDirectory, DataDirectory.CatalogDatabaseFileName)};Pooling=False");

        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM {table} WHERE {keyColumn} = $key";
        command.Parameters.AddWithValue("$key", key);

        using var reader = command.ExecuteReader();

        Assert.True(reader.Read());

        var row = new Dictionary<string, object?>(StringComparer.Ordinal);

        for (var index = 0; index < reader.FieldCount; index++)
        {
            row[reader.GetName(index)] = reader.IsDBNull(index) ? null : reader.GetValue(index);
        }

        return row;
    }

    /// <summary>
    /// Creates a temporary data folder for the host.
    /// </summary>
    /// <returns>The absolute path of the folder.</returns>
    private static string CreateTemporaryDataDirectory() =>
        Directory.CreateTempSubdirectory("lan-party-hub-manifest-endpoint-").FullName;
}
