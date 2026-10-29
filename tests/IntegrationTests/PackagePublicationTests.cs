using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using LANPartyHub.Server.Application.Packages;
using LANPartyHub.Server.Host;
using LANPartyHub.Server.Infrastructure.Persistence;
using LANPartyHub.Server.Infrastructure.Signing;
using LANPartyHub.Shared.Contracts;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
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
    public async Task The_composition_root_resolves_the_use_cases_that_the_desktop_application_resolves()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        // La aplicación de escritorio resuelve estos casos de uso antes de abrir ninguna
        // ventana, y una registración que falta lanza InvalidOperationException antes
        // de que se asigne la ventana principal: la interfaz entera no se abre. La
        // prueba pasa por el contenedor real, no por uno construido a mano, para que
        // falle si la registración falta (AD-1).
        await using var scope = host.Application.Services.CreateAsyncScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<PublishPackage>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ListPackages>());
    }

    [Fact]
    public async Task The_publication_persists_the_descriptor_and_the_signed_manifest()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        var packageId = "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e40";
        var bytes = WritePackageBytes(dataDirectory, packageId, "contenido del paquete");
        var gameId = await RegisterGameAsync(client, dataDirectory);

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
        Assert.Equal(gameId, descriptorRow["gameId"]);
        Assert.Equal("1.0.0", descriptorRow["version"]);
        Assert.Equal(bytes.LongLength, descriptorRow["sizeBytes"]);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            descriptorRow["sha256"]);

        // The chunk size is informative and optional in the HTTP path of the MVP
        // (AD-24), and the reference points at the manifest that was signed (AD-7).
        Assert.Equal(0L, Convert.ToInt64(descriptorRow["chunkSizeBytes"], CultureInfo.InvariantCulture));

        // The descriptor does not transport a signature: the signature lives only in the
        // envelope of the manifest (AD-2, AD-7).
        Assert.DoesNotContain("signature", ExpectedDescriptorColumns, StringComparison.OrdinalIgnoreCase);

        var manifestRow = ReadRow(dataDirectory, "INSTALLATION_MANIFEST", "packageId", packageId);

        Assert.False(string.IsNullOrWhiteSpace((string)manifestRow["signature"]));
        Assert.False(string.IsNullOrWhiteSpace((string)manifestRow["keyId"]));
        Assert.False(string.IsNullOrWhiteSpace((string)manifestRow["canonicalManifest"]));

        // The reference of the descriptor is the identifier of the manifest that carries
        // the signature, so the descriptor points at the signed object (AD-7, AD-22).
        Assert.Equal(descriptorRow["manifestRef"], manifestRow["manifestId"]);

        // The reference travels back to the organizer, y el identificador del manifiesto
        // es el mismo que el de la fila (AD-7).
        Assert.Equal(descriptorRow["manifestRef"], descriptor.ManifestRef);
        Assert.Equal(packageId, descriptor.PackageId);
        Assert.Equal(gameId, descriptor.GameId);
        Assert.Equal(bytes.LongLength, descriptor.SizeBytes);
    }

    [Fact]
    public async Task The_persisted_signature_verifies_over_the_persisted_canonical_manifest()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        var packageId = "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e41";
        var bytes = WritePackageBytes(dataDirectory, packageId, "otro contenido del paquete");
        var gameId = await RegisterGameAsync(client, dataDirectory);

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
        // package identifier, the version, the exact size and the hash of the bytes, so
        // the correspondence between the manifest and the package is signed and not
        // assumed (AD-2, AD-7).
        Assert.Contains(packageId, canonical, StringComparison.Ordinal);
        Assert.Contains(
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            canonical,
            StringComparison.Ordinal);
        Assert.Contains("\"packageVersion\":\"1.0.0\"", canonical, StringComparison.Ordinal);
        Assert.Contains(
            $"\"sizeBytes\":{bytes.LongLength}",
            canonical,
            StringComparison.Ordinal);

        // The canonical text is the JSON canónico: sin espacios superfluos y con las
        // claves ordenadas por nombre (AD-2).
        Assert.DoesNotContain("\n", canonical, StringComparison.Ordinal);
        Assert.DoesNotContain(": ", canonical, StringComparison.Ordinal);
        Assert.DoesNotContain(", ", canonical, StringComparison.Ordinal);

        var launch = canonical.IndexOf("\"launch\"", StringComparison.Ordinal);
        var operations = canonical.IndexOf("\"operations\"", StringComparison.Ordinal);
        var packageIdKey = canonical.IndexOf("\"packageId\"", StringComparison.Ordinal);

        Assert.True(launch >= 0);
        Assert.True(launch < operations);
        Assert.True(operations < packageIdKey);
    }

    [Fact]
    public async Task The_publication_is_blocked_without_the_license()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        var gameId = await RegisterGameAsync(client, dataDirectory, license: string.Empty);
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

        // The block happens before any write: no row of the descriptor, no row of the
        // manifest and no file of the package (AC1).
        Assert.Equal(0, ReadRowCount(dataDirectory, "PACKAGE_DESCRIPTOR"));
        Assert.Equal(0, ReadRowCount(dataDirectory, "INSTALLATION_MANIFEST"));
        Assert.Equal(1, ReadPackageFileCount(dataDirectory));
    }

    [Fact]
    public async Task The_publication_is_blocked_without_the_restrictions_of_distribution()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        var gameId = await RegisterGameAsync(client, dataDirectory, distributionRestrictions: string.Empty);
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

        // El bloqueo ocurre antes de cualquier escritura: ni fila ni archivo (AC1).
        Assert.Equal(0, ReadRowCount(dataDirectory, "PACKAGE_DESCRIPTOR"));
        Assert.Equal(0, ReadRowCount(dataDirectory, "INSTALLATION_MANIFEST"));
        Assert.Equal(1, ReadPackageFileCount(dataDirectory));
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
        var gameId = await RegisterGameAsync(client, dataDirectory);

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
        var gameId = await RegisterGameAsync(client, dataDirectory);

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
    public async Task An_operation_that_is_not_allowed_is_rejected_even_when_the_body_spells_the_properties_differently()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        var packageId = "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e48";
        var bytes = WritePackageBytes(dataDirectory, packageId, "contenido");
        var gameId = await RegisterGameAsync(client, dataDirectory);

        var request = BuildValidRequest(packageId, bytes.LongLength, gameId);

        var payload = JsonSerializer.Serialize(request, SerializerOptions);

        // The body spells the properties of the envelope in a different case. The
        // deserializer of the server is case-insensitive, so the body is still readable,
        // and the inspection of the discriminators has to be case-insensitive too: an
        // operation outside the closed set is rejected as such, and not as a malformed
        // body (AD-7, AD-8, AD-15).
        payload = payload
            .Replace("\"manifest\"", "\"Manifest\"", StringComparison.Ordinal)
            .Replace("\"operations\"", "\"Operations\"", StringComparison.Ordinal)
            .Replace("\"createDirectory\"", "\"runArbitraryCommand\"", StringComparison.Ordinal);

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
        var gameId = await RegisterGameAsync(client, dataDirectory);

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

        // No se escribe ningún archivo: el paquete que no tiene bytes no deja un archivo
        // vacío en el repositorio (AC6).
        Assert.Equal(0, ReadPackageFileCount(dataDirectory));
    }

    [Fact]
    public async Task A_package_whose_bytes_do_not_match_the_declared_size_is_rejected_without_writing_a_row()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        var packageId = "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e48";
        var bytes = WritePackageBytes(dataDirectory, packageId, "contenido");
        var gameId = await RegisterGameAsync(client, dataDirectory);

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
        var gameId = await RegisterGameAsync(client, dataDirectory);

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
        var gameId = await RegisterGameAsync(client, dataDirectory);

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
        var gameId = await RegisterGameAsync(client, dataDirectory);

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

    [Fact]
    public async Task A_body_that_is_not_a_well_formed_json_is_rejected_with_its_own_code()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        // Un cuerpo que no se puede leer no es un manifiesto con una operación fuera del
        // conjunto cerrado: son dos rechazos distintos, con dos códigos estables
        // distintos, y la métrica de errores por código no los mezcla (AD-15, AD-25).
        using var response = await client.PostAsync(
            PackagesEndpoint.Route,
            new StringContent("{ this is not json", System.Text.Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        AssertRejected(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            ErrorCatalog.PackageBodyMalformedCode);

        Assert.Equal(0, ReadRowCount(dataDirectory, "PACKAGE_DESCRIPTOR"));
        Assert.Equal(0, ReadRowCount(dataDirectory, "INSTALLATION_MANIFEST"));
        Assert.Equal(0, ReadPackageFileCount(dataDirectory));
    }

    [Fact]
    public async Task A_field_of_the_body_that_does_not_have_the_declared_shape_is_rejected_with_its_own_code()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        // El identificador del videogame es un texto en el contrato, y un número en este
        // cuerpo: el cuerpo no se puede leer y su rechazo tiene su propio código (AD-5).
        using var response = await client.PostAsync(
            PackagesEndpoint.Route,
            new StringContent(
                "{\"schemaVersion\":1,\"gameId\":42}",
                System.Text.Encoding.UTF8,
                "application/json"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        AssertRejected(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            ErrorCatalog.PackageBodyMalformedCode);

        Assert.Equal(0, ReadRowCount(dataDirectory, "PACKAGE_DESCRIPTOR"));
    }

    [Theory]
    [InlineData("{\"schemaVersion\":1,\"gameId\":\"0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e55\",\"package\":{\"packageId\":\"p\",\"sizeBytes\":10},\"manifest\":{\"schemaVersion\":1}}")]
    [InlineData("{\"schemaVersion\":1,\"gameId\":\"0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e55\",\"package\":{\"packageId\":\"p\",\"sizeBytes\":10},\"manifest\":{\"schemaVersion\":1,\"launch\":null,\"operations\":[]}}")]
    [InlineData("{\"schemaVersion\":1,\"gameId\":\"0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e55\",\"package\":{\"packageId\":\"\",\"sizeBytes\":10},\"manifest\":{\"schemaVersion\":1,\"launch\":{\"executablePath\":\"g.exe\",\"arguments\":[],\"workingDirectory\":\"g\"},\"operations\":[]}}")]
    [InlineData("{\"schemaVersion\":1,\"package\":{\"packageId\":\"p\",\"sizeBytes\":10},\"manifest\":{\"schemaVersion\":1,\"launch\":{\"executablePath\":\"g.exe\",\"arguments\":[],\"workingDirectory\":\"g\"},\"operations\":[]}}")]
    public async Task A_body_whose_fields_do_not_have_the_declared_shape_is_rejected_as_malformed(string payload)
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        // Un cuerpo con los bloques pero sin la forma que el contrato declara es un cuerpo
        // que no se puede leer: se rechaza con un 400 y su propio código, y no con el
        // 500 de la persistencia, que es el de un fallo de EF Core (AD-5, AD-15).
        using var response = await client.PostAsync(
            PackagesEndpoint.Route,
            new StringContent(payload, System.Text.Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        AssertRejected(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            ErrorCatalog.PackageBodyMalformedCode);

        Assert.Equal(0, ReadRowCount(dataDirectory, "PACKAGE_DESCRIPTOR"));
        Assert.Equal(0, ReadRowCount(dataDirectory, "INSTALLATION_MANIFEST"));
    }

    [Fact]
    public async Task The_rejection_of_the_publication_counts_the_error_by_its_stable_code()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        var gameId = await RegisterGameAsync(client, dataDirectory, license: string.Empty);
        var packageId = "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e53";
        var bytes = WritePackageBytes(dataDirectory, packageId, "contenido");

        using var response = await client.PostAsync(
            PackagesEndpoint.Route,
            BuildBody(BuildValidRequest(packageId, bytes.LongLength, gameId)),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        // El error se cuenta por su código estable, y no por su texto (AC8, AD-15).
        using var document = JsonDocument.Parse(
            await client.GetStringAsync(MetricsEndpoint.Route, TestContext.Current.CancellationToken));

        var errors = document.RootElement.GetProperty("errorsByCode");

        Assert.Equal(1, errors.GetProperty(ErrorCatalog.PackageLicenseMissingCode).GetInt64());
    }

    [Theory]
    [InlineData("packageId")]
    [InlineData("packageVersion")]
    [InlineData("sizeBytes")]
    [InlineData("sha256")]
    public async Task A_manifest_that_does_not_match_the_descriptor_is_rejected_without_writing_a_row(
        string fieldName)
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        var packageId = "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e56";
        var bytes = WritePackageBytes(dataDirectory, packageId, "contenido");
        var gameId = await RegisterGameAsync(client, dataDirectory);

        var request = BuildValidRequest(packageId, bytes.LongLength, gameId);

        // El manifiesto y el descriptor son dos formas de la misma publicación, así que
        // su identidad tiene que coincidir: si no coincide, la correspondencia entre el
        // manifiesto y el paquete no está firmada (AD-2, AC7).
        request.Manifest = fieldName switch
        {
            "packageId" => request.Manifest! with { PackageId = "otro-paquete" },
            "packageVersion" => request.Manifest! with { PackageVersion = "9.9.9" },
            "sizeBytes" => request.Manifest! with { SizeBytes = request.Manifest.SizeBytes + 1 },
            _ => request.Manifest! with { Sha256 = new string('b', 64) },
        };

        using var response = await client.PostAsync(
            PackagesEndpoint.Route,
            BuildBody(request),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        AssertRejected(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            ErrorCatalog.PackageManifestMismatchCode);

        // El rechazo ocurre antes de cualquier escritura: ni fila ni archivo (AC7).
        Assert.Equal(0, ReadRowCount(dataDirectory, "PACKAGE_DESCRIPTOR"));
        Assert.Equal(0, ReadRowCount(dataDirectory, "INSTALLATION_MANIFEST"));
        Assert.Equal(1, ReadPackageFileCount(dataDirectory));
    }

    [Fact]
    public async Task A_manifest_with_an_unsupported_schema_version_is_rejected_without_writing_a_row()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        var packageId = "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e57";
        var bytes = WritePackageBytes(dataDirectory, packageId, "contenido");
        var gameId = await RegisterGameAsync(client, dataDirectory);

        var request = BuildValidRequest(packageId, bytes.LongLength, gameId);

        // El manifiesto declara su propia versión de esquema, y una versión que este
        // servidor no entiende se rechaza antes de escribir nada (AD-8). La versión del
        // esquema del CUERPO es otra cosa, y la de aquí sigue siendo la soportada.
        request.Manifest = request.Manifest! with
        {
            SchemaVersion = InstallationManifestSchema.Version + 1,
        };

        using var response = await client.PostAsync(
            PackagesEndpoint.Route,
            BuildBody(request),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        AssertRejected(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            ErrorCatalog.SchemaVersionNotSupportedCode);

        Assert.Equal(0, ReadRowCount(dataDirectory, "PACKAGE_DESCRIPTOR"));
        Assert.Equal(0, ReadRowCount(dataDirectory, "INSTALLATION_MANIFEST"));
        Assert.Equal(1, ReadPackageFileCount(dataDirectory));
    }

    [Fact]
    public async Task A_manifest_that_spells_its_properties_differently_is_still_rejected_as_an_unknown_operation()
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        var packageId = "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e58";
        var bytes = WritePackageBytes(dataDirectory, packageId, "contenido");
        var gameId = await RegisterGameAsync(client, dataDirectory);

        var request = BuildValidRequest(packageId, bytes.LongLength, gameId);

        var payload = JsonSerializer.Serialize(request, SerializerOptions);

        // El deserializador no distingue mayúsculas de minúsculas en el nombre de las
        // propiedades, así que la inspección que precede a la deserialización tiene que
        // hacer lo mismo: si no, un cuerpo que escribe "Manifest" caería en el rechazo
        // del cuerpo mal formado en lugar del rechazo de la operación que no está
        // permitida, y la métrica de errores por código se partiría en dos (AD-15).
        //
        // Las tres propiedades que la inspección busca --"manifest", "operations" y el
        // discriminador "operation"-- se escriben con otra grafía, de modo que la prueba
        // sólo pasa si TODAS las búsquedas ignoran las mayúsculas: con una búsqueda
        // sensible, el discriminador no se encuentra, la inspección no ve la operación
        // fuera del conjunto cerrado y el cuerpo se rechaza como mal formado con un 400
        // en lugar del 422 de la operación que no está permitida.
        payload = payload.Replace("\"manifest\"", "\"Manifest\"", StringComparison.Ordinal);
        payload = payload.Replace("\"operations\"", "\"Operations\"", StringComparison.Ordinal);
        payload = payload.Replace("\"operation\":", "\"Operation\":", StringComparison.Ordinal);
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

    [Theory]
    [InlineData("Manifest", "Operations")]
    [InlineData("MANIFEST", "OPERATIONS")]
    [InlineData("mAnIfEsT", "oPeRaTiOnS")]
    public async Task A_body_that_spells_the_manifest_and_the_operations_differently_is_rejected_as_an_unknown_operation(
        string manifestProperty,
        string operationsProperty)
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        var packageId = "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e59";
        var bytes = WritePackageBytes(dataDirectory, packageId, "contenido");
        var gameId = await RegisterGameAsync(client, dataDirectory);

        var request = BuildValidRequest(packageId, bytes.LongLength, gameId);

        var payload = JsonSerializer.Serialize(request, SerializerOptions);

        // El cuerpo escribe "manifest" y "operations" con otra grafía, y lleva una
        // operación fuera del conjunto cerrado. El deserializador que sigue no
        // distingue mayúsculas de minúsculas en el nombre de las propiedades
        // (JsonSerializerDefaults.Web), así que el cuerpo SÍ se puede leer: si la
        // inspección que precede a la deserialización no hiciese lo mismo, la operación
        // fuera del conjunto cerrado no se vería y el cuerpo caería en el rechazo del
        // cuerpo mal formado (400 PACKAGE_BODY_MALFORMED) en lugar del rechazo de la
        // operación que no está permitida (422 MANIFEST_OPERATION_NOT_ALLOWED), que son
        // dos códigos estables distintos y no se pueden medir en la misma métrica
        // (AD-15, AD-25).
        payload = payload.Replace("\"manifest\"", $"\"{manifestProperty}\"", StringComparison.Ordinal);
        payload = payload.Replace("\"operations\"", $"\"{operationsProperty}\"", StringComparison.Ordinal);

        // El nombre de la operación es el que la hace estar fuera del conjunto cerrado,
        // y es el que la inspección tiene que leer: "runArbitraryCommand" no es uno de
        // los seis nombres del conjunto cerrado (AD-7, AD-8).
        payload = payload.Replace("\"createDirectory\"", "\"runArbitraryCommand\"", StringComparison.Ordinal);

        using var response = await client.PostAsync(
            PackagesEndpoint.Route,
            new StringContent(payload, System.Text.Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        AssertRejected(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            ErrorCatalog.ManifestOperationNotAllowedCode);

        // El rechazo ocurre antes de cualquier escritura: ni fila ni archivo (AC1).
        Assert.Equal(0, ReadRowCount(dataDirectory, "PACKAGE_DESCRIPTOR"));
        Assert.Equal(0, ReadRowCount(dataDirectory, "INSTALLATION_MANIFEST"));
        Assert.Equal(1, ReadPackageFileCount(dataDirectory));
    }

    [Theory]
    [InlineData("copyFile")]
    [InlineData("writeConfig")]
    [InlineData("setLaunchConfig")]
    [InlineData("verify")]
    public async Task A_publication_that_carries_each_operation_of_the_closed_set_is_accepted(string operationName)
    {
        var dataDirectory = CreateTemporaryDataDirectory();

        await using var host = await TestServer.StartAsync(dataDirectory, TestContext.Current.CancellationToken);

        using var client = host.CreateClient();

        var packageId = $"0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e6{operationName.Length}";
        var bytes = WritePackageBytes(dataDirectory, packageId, "contenido");
        var gameId = await RegisterGameAsync(client, dataDirectory);

        var request = BuildValidRequest(packageId, bytes.LongLength, gameId);

        // Cada una de las seis operaciones del conjunto cerrado cruza la frontera HTTP y
        // es aceptada, para que el endpoint acepte de verdad el conjunto entero (AD-7).
        request.Manifest = request.Manifest! with
        {
            Operations = [BuildOperation(operationName)],
        };

        using var response = await client.PostAsync(
            PackagesEndpoint.Route,
            BuildBody(request),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        Assert.Equal(1, ReadRowCount(dataDirectory, "PACKAGE_DESCRIPTOR"));
        Assert.Equal(1, ReadRowCount(dataDirectory, "INSTALLATION_MANIFEST"));

        // La operación aceptada queda en el manifiesto canónico que se firma (AD-2).
        var manifestRow = ReadRow(dataDirectory, "INSTALLATION_MANIFEST", "packageId", packageId);
        var canonical = (string)manifestRow["canonicalManifest"];

        Assert.Contains($"\"operation\":\"{operationName}\"", canonical, StringComparison.Ordinal);
    }

    /// <summary>
    /// Builds the operation of the closed set that the test declares (AD-7).
    /// </summary>
    /// <param name="operationName">Name of the operation.</param>
    /// <returns>The operation of the manifest.</returns>
    private static InstallationManifest.ManifestOperation BuildOperation(string operationName) => operationName switch
    {
        "copyFile" => new InstallationManifest.CopyFile("game\\data.bin", "game\\data.bin"),
        "writeConfig" => new InstallationManifest.WriteConfig("game.ini", "[game]"),
        "setLaunchConfig" => new InstallationManifest.SetLaunchConfig(
            new InstallationManifest.LaunchSettings("game\\game.exe", ["-novid"], "game")),
        "verify" => new InstallationManifest.Verify("game\\game.exe"),
        _ => new InstallationManifest.CreateDirectory("game"),
    };

    /// <summary>
    /// Registers a videogame in the catalog and returns its identifier.
    ///
    /// <para>
    /// The registration demands the license and the restrictions of distribution, because
    /// FR-1 makes them mandatory metadata of the videogame (Story 2.1). To reach the
    /// block of the publication, the test blanks the field in the row that is already
    /// persisted: the catalog can hold a videogame whose license is empty, and the
    /// publication is the moment in which the catalog demands it and names the field that
    /// is missing (AC1 of this story).
    /// </para>
    /// </summary>
    /// <param name="client">Client of the host.</param>
    /// <param name="dataDirectory">Folder of the data of the host.</param>
    /// <param name="license">License of the videogame.</param>
    /// <param name="distributionRestrictions">Restrictions of distribution of the videogame.</param>
    /// <returns>The identifier of the videogame.</returns>
    private static async Task<string> RegisterGameAsync(
        HttpClient client,
        string dataDirectory,
        string license = "Propietaria",
        string distributionRestrictions = "Solo para el evento")
    {
        // El registro exige la licencia y las restricciones de distribución, porque FR-1
        // las hace metadata obligatorios del videogame (Story 2.1). El alta se hace con
        // valores válidos y el test vacía el campo en la fila ya persistida, que es el
        // estado que la publicación bloquea (AC1 de esta historia).
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

        var descriptor = await ReadDescriptorAsync(response, TestContext.Current.CancellationToken);

        BlankPersistedField(dataDirectory, "GAME", descriptor.GameId, "license", license);
        BlankPersistedField(dataDirectory, "GAME", descriptor.GameId, "distributionRestrictions", distributionRestrictions);

        return descriptor.GameId;
    }

    /// <summary>
    /// Empties a field of a persisted row of the server, so the state that the block of
    /// the publication rejects is reachable (AC1).
    /// </summary>
    /// <param name="dataDirectory">Folder of the data of the host.</param>
    /// <param name="table">Name of the table.</param>
    /// <param name="key">Value of the key of the row.</param>
    /// <param name="column">Name of the column to empty.</param>
    /// <param name="value">Value that the column holds when the test does not empty it.</param>
    private static void BlankPersistedField(
        string dataDirectory,
        string table,
        string key,
        string column,
        string value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            return;
        }

        using var connection = OpenDatabase(dataDirectory);

        using var command = connection.CreateCommand();
        command.CommandText = $"UPDATE {table} SET {column} = '' WHERE gameId = $key";
        command.Parameters.AddWithValue("$key", key);

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
            InstallationManifestSchema.Version,
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
    /// Counts the files of the repository of packages of the server. The publication that
    /// is rejected does not write any file, only the rows of the two tables (AC1, AC6).
    /// </summary>
    /// <param name="dataDirectory">Folder of the data of the host.</param>
    /// <returns>The number of files of the repository of packages.</returns>
    private static int ReadPackageFileCount(string dataDirectory)
    {
        var packages = Path.Combine(dataDirectory, DataDirectory.PackagesFolderName);

        if (!Directory.Exists(packages))
        {
            return 0;
        }

        return Directory.GetFiles(packages).Length;
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
