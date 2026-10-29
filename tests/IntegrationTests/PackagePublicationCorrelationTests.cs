using System.Net;
using System.Text.Json;
using LANPartyHub.Server.Host;
using LANPartyHub.Server.Infrastructure.Http;
using LANPartyHub.Server.Infrastructure.Persistence;
using LANPartyHub.Shared.Contracts;
using Xunit;

namespace LANPartyHub.IntegrationTests;

/// <summary>
/// The correlation of the publication of the packages against the real host and the real
/// log (AD-15, AD-30, AC8).
///
/// <para>
/// The publication is a unit of work like any other, so its log entry carries the
/// identifier of the operation, and its rejection is counted under its stable code and
/// not under its text (AD-15, AD-25). The observation is only possible with the server
/// in march, so the tests start the real host and read the real log (AD-16).
/// </para>
/// <para>
/// The tests live in the collection of the capture of the log because reading the log
/// means replacing the standard output of the process (AD-16).
/// </para>
/// </summary>
[Collection(LogCaptureCollection.Name)]
public sealed class PackagePublicationCorrelationTests
{
    /// <summary>
    /// Identifier of the unit of work that the test asks for explicitly.
    /// </summary>
    private const string OperationId = "9f2c1d3e4f5a6b7c8d9e0f1a2b3c4d5e";

    /// <summary>
    /// Category of the log of the use case that publishes a package. The entry of the
    /// publication is observed by this category and not by the one of the middleware,
    /// because both live in the same scope of correlation (AD-15).
    /// </summary>
    private const string PublishPackageCategory = "LANPartyHub.Server.Application.Packages.PublishPackage";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task The_publication_is_correlated_by_the_identifier_of_the_unit_of_work()
    {
        await using var capture = await ConsoleLogCapture.StartAsync();

        var dataDirectory = CreateTemporaryDataDirectory();

        await using (var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken))
        {
            using var client = host.CreateClient();

            var packageId = "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e52";
            var bytes = WritePackageBytes(dataDirectory, packageId, "contenido");
            var gameId = await RegisterGameAsync(client);

            using var request = new HttpRequestMessage(HttpMethod.Post, PackagesEndpoint.Route)
            {
                Content = BuildBody(BuildValidRequest(packageId, bytes.LongLength, gameId)),
            };

            request.Headers.Add(AdminCorrelationMiddleware.OperationIdHeaderName, OperationId);

            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            // El identificador de la unidad de trabajo viaja de vuelta a la organizadora,
            // para que el log del servidor y el de ella se puedan unir (AD-15).
            Assert.Equal(
                OperationId,
                response.Headers.GetValues(AdminCorrelationMiddleware.OperationIdHeaderName).Single());
        }

        // La entrada del log de la publicación lleva el identificador de la unidad de
        // trabajo (AC8, AD-15).
        var entry = Assert.Single(
            capture.Entries,
            candidate => LogEntries.HasCategory(candidate, PublishPackageCategory)
                && LogEntries.Scopes(candidate).Any(scope =>
                    scope.TryGetProperty("OperationId", out var scopeOperationId)
                    && scopeOperationId.GetString() == OperationId));

        Assert.Contains(
            LogEntries.Scopes(entry),
            scope => scope.TryGetProperty("OperationId", out var scopeOperationId)
                && scopeOperationId.GetString() == OperationId);
    }

    [Fact]
    public async Task The_publication_that_gets_its_identifier_from_the_server_is_also_correlated()
    {
        await using var capture = await ConsoleLogCapture.StartAsync();

        var dataDirectory = CreateTemporaryDataDirectory();

        await using (var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken))
        {
            using var client = host.CreateClient();

            var packageId = "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e54";
            var bytes = WritePackageBytes(dataDirectory, packageId, "contenido");
            var gameId = await RegisterGameAsync(client);

            using var response = await client.PostAsync(
                PackagesEndpoint.Route,
                BuildBody(BuildValidRequest(packageId, bytes.LongLength, gameId)),
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        // La publicación sigue siendo una unidad de trabajo aunque el identificador lo
        // genere el servidor, y su entrada lo lleva en el scope (AD-15, AC8).
        Assert.Contains(
            capture.Entries,
            candidate => LogEntries.HasCategory(candidate, PublishPackageCategory)
                && LogEntries.Scopes(candidate).Any(scope =>
                    scope.TryGetProperty("OperationId", out var scopeOperationId)
                    && !string.IsNullOrWhiteSpace(scopeOperationId.GetString())));
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

    private static string CreateTemporaryDataDirectory() =>
        Directory.CreateTempSubdirectory("lan-party-hub-server-").FullName;
}
