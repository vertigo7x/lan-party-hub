using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using LANPartyHub.Server.Application.Packages;
using LANPartyHub.Server.Application.Ports;
using LANPartyHub.Server.Domain.Catalog;
using LANPartyHub.Server.Domain.Packages;
using LANPartyHub.Server.Host;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The invariants of the domain of the publication of the packages (AD-1, AD-7, AD-8, AC1).
///
/// <para>
/// The domain is the layer that holds the rules of the publication, so the rules are
/// exercised here and not in the adapter: the publication is blocked when the videogame
/// does not carry the license or the restrictions of distribution, and the rejection names
/// the concrete field, because the organizer has to know which box is empty (AD-25).
/// </para>
/// <para>
/// The block of the publication se ejerce sobre el CASO DE USO REAL, con dobles de los
/// puertos y no con una copia de su regla: una copia que repetiese la comprobación
/// pasaría la prueba aunque el caso de uso la perdiera, y el defecto volvería al
/// servidor sin que nada lo notara (AD-1, FR-1, NFR-8).
/// </para>
/// <para>
/// The operations of the manifest are typed and closed, and there is no operation that
/// runs arbitrary commands (AD-7, AD-8).
/// </para>
/// </summary>
public sealed class PackagePublicationDomainTests
{
    /// <summary>
    /// Identifier of the videogame whose package is published. It is the canonical form
    /// of an identifier of the catálogo (AD-23).
    /// </summary>
    private const string GameId = "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e40";

    /// <summary>Identifier of the package that is published (AD-7).</summary>
    private const string PackageId = "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e41";

    /// <summary>Version that the publisher declares (AD-7).</summary>
    private const string Version = "1.0.0";

    /// <summary>
    /// The publication is blocked when the videogame does not carry the license that the
    /// catalog demands, and the rejection names the concrete field (AC1, FR-1, NFR-8).
    /// </summary>
    [Fact]
    public async Task The_publication_is_blocked_without_the_license()
    {
        var exception = await Assert.ThrowsAsync<PackageLicenseMissingException>(
            () => PublishAsync(license: string.Empty));

        Assert.Equal(GameRegistration.FieldNames.License, exception.FieldName);
        Assert.Contains(GameRegistration.FieldNames.License, exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The publication is blocked when the videogame does not carry the restrictions of
    /// distribution, and the rejection names the concrete field (AC1, FR-1, NFR-8).
    /// </summary>
    [Fact]
    public async Task The_publication_is_blocked_without_the_restrictions_of_distribution()
    {
        var exception = await Assert.ThrowsAsync<PackageDistributionRestrictionsMissingException>(
            () => PublishAsync(distributionRestrictions: string.Empty));

        Assert.Equal(GameRegistration.FieldNames.DistributionRestrictions, exception.FieldName);
        Assert.Contains(
            GameRegistration.FieldNames.DistributionRestrictions,
            exception.Message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The block of the publication is explicit about the field that is missing: the two
    /// absences are two rejections distinct, and the organizer's form can name the field
    /// because the exception carries it (AC1, AD-25).
    /// </summary>
    [Fact]
    public async Task The_two_absences_are_two_rejections_that_name_their_own_field()
    {
        // Con las dos ausentes, la comprobación de la licencia es la que se adelanta, y
        // su rechazo nombra la licencia (AC1).
        var license = await Assert.ThrowsAsync<PackageLicenseMissingException>(
            () => PublishAsync(license: string.Empty, distributionRestrictions: string.Empty));

        Assert.Equal(GameRegistration.FieldNames.License, license.FieldName);

        // Con la licencia presente, la ausencia que queda es la de las restricciones, y su
        // rechazo nombra ese campo y no otro (AC1).
        var restrictions = await Assert.ThrowsAsync<PackageDistributionRestrictionsMissingException>(
            () => PublishAsync(license: "Propietaria", distributionRestrictions: string.Empty));

        Assert.Equal(GameRegistration.FieldNames.DistributionRestrictions, restrictions.FieldName);

        Assert.NotEqual(license.FieldName, restrictions.FieldName);
    }

    /// <summary>
    /// The block of the publication happens BEFORE any write: a package whose videogame
    /// does not carry the license leaves no row behind, and the descriptor and the
    /// manifest are not written either (AC1).
    /// </summary>
    [Fact]
    public async Task The_block_of_the_publication_writes_nothing()
    {
        var repository = new RecordingPackageRepository();

        await Assert.ThrowsAsync<PackageLicenseMissingException>(
            () => PublishAsync(repository, license: string.Empty));

        Assert.Empty(repository.AddedDescriptors);
        Assert.Empty(repository.AddedManifests);
    }

    /// <summary>
    /// The publication of a videogame that carries the license and the restrictions goes
    /// through, and the descriptor and the signed manifest are handed to the repository.
    ///
    /// <para>
    /// Esta prueba es la que demuestra que las dos anteriores no miden un caso raro: si
    /// el caso de uso rechazara SIEMPRE, las dos también pasarían (AC1).
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_publication_of_a_videogame_that_carries_the_license_goes_through()
    {
        var repository = new RecordingPackageRepository();

        var descriptor = await PublishAsync(repository);

        Assert.Equal(PackageId, descriptor.PackageId);
        Assert.Equal(GameId, descriptor.GameId);
        Assert.Equal(Version, descriptor.Version);
        Assert.Single(repository.AddedDescriptors);
        Assert.Single(repository.AddedManifests);

        // El descriptor NO transporta firma: la firma vive solo en el sobre del
        // manifiesto (AD-2, AD-7).
        Assert.False(string.IsNullOrWhiteSpace(repository.AddedManifests[0].Signature));
        Assert.False(string.IsNullOrWhiteSpace(repository.AddedManifests[0].KeyId));
    }

    /// <summary>
    /// The six operations of the closed set are the ones that the manifest accepts, and
    /// their names are the ones of the contract (AD-7).
    /// </summary>
    [Fact]
    public void The_closed_set_of_operations_is_the_one_of_the_contract()
    {
        Assert.Equal(
            [
                "extract",
                "createDirectory",
                "copyFile",
                "writeConfig",
                "setLaunchConfig",
                "verify",
            ],
            ManifestOperations.AllowedNames);

        foreach (var name in ManifestOperations.AllowedNames)
        {
            Assert.True(ManifestOperations.IsAllowed(name));
        }
    }

    /// <summary>
    /// The publication grows the version of the catalog, and it grows it exactly once
    /// (AC3, AD-26).
    ///
    /// <para>
    /// The version of the catalog is what tells the clients that the catalog changed, so
    /// it has to grow with EVERY publication that goes through and never with a
    /// publication that is rejected (AC3). The double of the port models the contract of
    /// the write: the write of the publication and the growth of the version are the SAME
    /// unit of work, so the version only grows when the write is committed.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_publication_grows_the_version_of_the_catalog()
    {
        var repository = new RecordingPackageRepository();

        Assert.Equal(0, await repository.GetCatalogVersionAsync(TestContext.Current.CancellationToken));

        await PublishAsync(repository);

        Assert.Equal(1, await repository.GetCatalogVersionAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A publication that is rejected does not grow the version of the catalog (AC3).
    ///
    /// <para>
    /// The version announces a change of the catalog, and a publication that never
    /// happened is not a change: a version that grew anyway would make every client of the
    /// LAN resynchronize for nothing (AC3, AD-26).
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_blocked_publication_does_not_grow_the_version_of_the_catalog()
    {
        var repository = new RecordingPackageRepository();

        await Assert.ThrowsAsync<PackageLicenseMissingException>(
            () => PublishAsync(repository, license: string.Empty));

        Assert.Equal(0, await repository.GetCatalogVersionAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Two publications grow the version twice, and the version never goes back (AC3).
    /// </summary>
    [Fact]
    public async Task The_version_of_the_catalog_grows_monotonically()
    {
        var repository = new RecordingPackageRepository();

        await PublishAsync(repository);
        await PublishAsync(repository);

        Assert.Equal(2, await repository.GetCatalogVersionAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The closed set of the domain is EXACTLY the one that the contract declares in its
    /// <see cref="JsonDerivedTypeAttribute"/> discriminators (AD-7).
    /// </summary>
    [Fact]
    public void The_closed_set_of_the_domain_is_exactly_the_one_that_the_contract_declares()
    {
        var declared = typeof(InstallationManifest.ManifestOperation)
            .GetCustomAttributes<JsonDerivedTypeAttribute>()
            .Select(attribute => ReadDiscriminator(attribute))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(declared);

        Assert.Equal(
            declared,
            ManifestOperations.AllowedNames.OrderBy(name => name, StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// Every operation of the closed set of the domain is one of the derived types of the
    /// contract, so the canonicalizer can write it (AD-2, AD-7).
    /// </summary>
    [Fact]
    public void Every_operation_of_the_closed_set_is_a_derived_type_of_the_contract()
    {
        var derived = typeof(InstallationManifest.ManifestOperation)
            .GetCustomAttributes<JsonDerivedTypeAttribute>()
            .Select(attribute => attribute.DerivedType)
            .ToArray();

        foreach (var name in ManifestOperations.AllowedNames)
        {
            var operation = BuildOperation(name);

            Assert.NotNull(operation);
            Assert.Contains(operation.GetType(), derived);
        }
    }

    /// <summary>
    /// The version of the schema of the manifest that the use case publishes is the same
    /// that the endpoint declares (AD-5, AD-8).
    ///
    /// <para>
    /// La versión del esquema del CUERPO y la versión del esquema del MANIFIESTO son dos
    /// cosas distintas, y cada una tiene su sitio. Esta prueba ata la del manifiesto con
    /// la que el endpoint usa para el manifiesto, de modo que las dos no puedan separarse
    /// sin que se note.
    /// </para>
    /// </summary>
    [Fact]
    public void The_version_of_the_schema_of_the_manifest_is_the_one_that_the_use_case_publishes()
    {
        Assert.Equal(
            InstallationManifestSchema.Version,
            PackagesEndpoint.SupportedManifestSchemaVersion);
    }

    /// <summary>
    /// An operation that is not one of the six is rejected before anything is written
    /// (AD-7, AD-8).
    /// </summary>
    [Fact]
    public void An_operation_outside_the_closed_set_is_rejected()
    {
        var exception = Assert.Throws<ManifestOperationNotAllowedException>(
            () => ManifestOperations.EnsureAllowed([new UnknownOperation()]));

        Assert.False(string.IsNullOrWhiteSpace(exception.OperationName));
    }

    /// <summary>
    /// The six operations of the closed set are accepted, and their name is the one that
    /// the discriminator of the contract writes (AD-7).
    /// </summary>
    [Fact]
    public void The_six_operations_of_the_closed_set_are_accepted()
    {
        InstallationManifest.ManifestOperation[] operations =
        [
            new InstallationManifest.Extract("game.zip", "game"),
            new InstallationManifest.CreateDirectory("game"),
            new InstallationManifest.CopyFile("a.txt", "game\\a.txt"),
            new InstallationManifest.WriteConfig("game.ini", "[game]"),
            new InstallationManifest.SetLaunchConfig(
                new InstallationManifest.LaunchSettings("game\\game.exe", [], "game")),
            new InstallationManifest.Verify("game\\game.exe"),
        ];

        ManifestOperations.EnsureAllowed(operations);

        Assert.Equal("extract", ManifestOperations.NameOf(operations[0]));
        Assert.Equal("createDirectory", ManifestOperations.NameOf(operations[1]));
        Assert.Equal("copyFile", ManifestOperations.NameOf(operations[2]));
        Assert.Equal("writeConfig", ManifestOperations.NameOf(operations[3]));
        Assert.Equal("setLaunchConfig", ManifestOperations.NameOf(operations[4]));
        Assert.Equal("verify", ManifestOperations.NameOf(operations[5]));
    }

    /// <summary>
    /// There is no operation of the closed set that runs arbitrary commands (AD-7, AD-8).
    /// </summary>
    [Fact]
    public void There_is_no_operation_that_runs_arbitrary_commands()
    {
        foreach (var name in ManifestOperations.AllowedNames)
        {
            Assert.DoesNotContain("command", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("exec", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("run", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("script", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("process", name, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// The name of an operation is EXACT, because the discriminator of the contract is
    /// resolved exactly: a body that spells the discriminator <c>"EXTRACT"</c> carries a
    /// name that is NOT one of the six, y el conjunto cerrado no se abre por la grafía
    /// (AD-7, AD-8).
    /// </summary>
    [Theory]
    [InlineData("EXTRACT")]
    [InlineData("Extract")]
    [InlineData("eXtRaCt")]
    [InlineData("CREATEDIRECTORY")]
    [InlineData("CopyFile")]
    [InlineData("writeconfig")]
    [InlineData("SETLAUNCHCONFIG")]
    [InlineData("VERIFY")]
    public void The_name_of_an_operation_is_exact(string name)
    {
        Assert.False(ManifestOperations.IsAllowed(name));
    }

    /// <summary>
    /// A name that is NOT one of the six is rejected: el conjunto cerrado sigue siendo
    /// cerrado, y ningún otro nombre se cuela por su forma (AD-7, AD-8).
    /// </summary>
    [Theory]
    [InlineData("runArbitraryCommand")]
    [InlineData("RUNARBITRARYCOMMAND")]
    [InlineData("Exec")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("extract ")]
    [InlineData(" extract")]
    public void A_name_outside_the_closed_set_is_rejected(string name)
    {
        Assert.False(ManifestOperations.IsAllowed(name));
    }

    /// <summary>
    /// A name that is NOT one of the six is rejected whatever its case is: el conjunto
    /// cerrado sigue siendo cerrado, y la insensibilidad a la grafía no abre la puerta
    /// a ningún otro nombre (AD-7, AD-8).
    /// </summary>
    [Theory]
    [InlineData("runArbitraryCommand")]
    [InlineData("RUNARBITRARYCOMMAND")]
    [InlineData("Exec")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("extract ")]
    public void A_name_outside_the_closed_set_is_rejected_whatever_its_case_is(string name)
    {
        Assert.False(ManifestOperations.IsAllowed(name));
    }

    /// <summary>
    /// The deserializer of the contract resolves the discriminator of the operations
    /// EXACTLY, and the closed set of the domain agrees with it.
    ///
    /// <para>
    /// El discriminador polimórfico de <c>System.Text.Json</c> se resuelve por el
    /// discriminador EXACTO que declara el contrato: un cuerpo que escribe
    /// <c>"EXTRACT"</c> no es la operación <c>extract</c>, sino un discriminador que el
    /// contrato no reconoce. La comprobación del dominio tiene que ser tan exacta como
    /// el deserializador, porque una comprobación más permisiva dejaría pasar un
    /// discriminador que después no se puede leer (AD-7, AD-15).
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("extract", "extract")]
    [InlineData("createDirectory", "createDirectory")]
    [InlineData("copyFile", "copyFile")]
    public void The_deserializer_resolves_the_discriminator_exactly(
        string canonicalName,
        string discriminator)
    {
        var payload = $$"""
            {
              "schemaVersion": 1,
              "packageId": "p",
              "packageVersion": "1.0.0",
              "sizeBytes": 10,
              "sha256": "",
              "launch": { "executablePath": "g.exe", "arguments": [], "workingDirectory": "g" },
              "operations": [{ "operation": "{{discriminator}}", "path": "g", "archivePath": "a.zip", "destinationPath": "g", "sourcePath": "a", "content": "c" }]
            }
            """;

        var manifest = JsonSerializer.Deserialize<InstallationManifest>(
            payload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(manifest);
        Assert.Single(manifest!.Operations);

        Assert.Equal(canonicalName, ManifestOperations.NameOf(manifest.Operations[0]));
        Assert.True(ManifestOperations.IsAllowed(discriminator));

        ManifestOperations.EnsureAllowed(manifest.Operations);
    }

    /// <summary>
    /// A discriminator with a DIFFERENT case is not one of the six of the closed set, and
    /// neither the deserializer nor the domain accept it (AD-7, AD-8).
    ///
    /// <para>
    /// El discriminador polimórfico de <c>System.Text.Json</c> se resuelve por el
    /// discriminador EXACTO, y la comprobación del dominio tiene que decir lo mismo: si
    /// el dominio aceptara <c>"EXTRACT"</c> y el deserializador no, la inspección previa
    /// dejaría pasar un cuerpo que a continuación se rechazaría como cuerpo mal formado,
    /// y el mismo defecto se contaría bajo dos códigos estables distintos (AD-15, AD-25).
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("EXTRACT")]
    [InlineData("CREATEDIRECTORY")]
    [InlineData("CopyFile")]
    [InlineData("WriteConfig")]
    [InlineData("SETLAUNCHCONFIG")]
    [InlineData("VERIFY")]
    public void A_discriminator_with_a_different_case_is_outside_the_closed_set(string discriminator)
    {
        var payload = $$"""
            {
              "schemaVersion": 1,
              "packageId": "p",
              "packageVersion": "1.0.0",
              "sizeBytes": 10,
              "sha256": "",
              "launch": { "executablePath": "g.exe", "arguments": [], "workingDirectory": "g" },
              "operations": [{ "operation": "{{discriminator}}", "path": "g", "archivePath": "a.zip", "destinationPath": "g", "sourcePath": "a", "content": "c" }]
            }
            """;

        // El deserializador NO puede leer el discriminador: la operación no es una de
            // las seis del contrato.
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<InstallationManifest>(
            payload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        // Y el dominio dice lo mismo, porque la comprobación es tan exacta como el
        // deserializador (AD-7).
        Assert.False(ManifestOperations.IsAllowed(discriminator));
    }

    /// <summary>
    /// Builds the operation of the closed set that carries the given name (AD-7).
    /// </summary>
    /// <param name="name">Name of the operation.</param>
    /// <returns>The operation of the manifest.</returns>
    private static InstallationManifest.ManifestOperation? BuildOperation(string name) => name switch
    {
        "extract" => new InstallationManifest.Extract("game.zip", "game"),
        "createDirectory" => new InstallationManifest.CreateDirectory("game"),
        "copyFile" => new InstallationManifest.CopyFile("game\\data.bin", "game\\data.bin"),
        "writeConfig" => new InstallationManifest.WriteConfig("game.ini", "[game]"),
        "setLaunchConfig" => new InstallationManifest.SetLaunchConfig(
            new InstallationManifest.LaunchSettings("game\\game.exe", [], "game")),
        "verify" => new InstallationManifest.Verify("game\\game.exe"),
        _ => null,
    };

    /// <summary>
    /// Reads the discriminator that the contract declares for a derived type.
    ///
    /// <para>
    /// The discriminator is the second argument of the constructor of
    /// <see cref="JsonDerivedTypeAttribute"/> and the attribute does not expose it as a
    /// property, so it is read from the raw metadata of the attribute.
    /// </para>
    /// </summary>
    /// <param name="attribute">Attribute that declares a derived type of the contract.</param>
    /// <returns>The discriminator that the contract declares.</returns>
    private static string ReadDiscriminator(JsonDerivedTypeAttribute attribute)
    {
        var declared = CustomAttributeData
            .GetCustomAttributes(typeof(InstallationManifest.ManifestOperation))
            .Where(candidate => candidate.AttributeType == typeof(JsonDerivedTypeAttribute))
            .Select(candidate => candidate.ConstructorArguments)
            .Where(arguments => arguments.Count >= 2)
            .Where(arguments => arguments[0].Value is Type derivedType
                && derivedType == attribute.DerivedType)
            .Select(arguments => (string)arguments[1].Value!)
            .ToArray();

        return declared.Length switch
        {
            1 => declared[0],
            0 => throw new InvalidOperationException(
                $"The contract does not declare a discriminator for the derived type '{attribute.DerivedType}'."),
            _ => throw new InvalidOperationException(
                $"The contract declares more than one discriminator for the derived type '{attribute.DerivedType}'."),
        };
    }

    /// <summary>
    /// Publishes a package through the REAL use case, with doubles of the ports (AD-1).
    /// </summary>
    /// <param name="repository">Repository that records what the use case persists.</param>
    /// <param name="license">License of the videogame.</param>
    /// <param name="distributionRestrictions">Restrictions of distribution of the videogame.</param>
    /// <returns>The descriptor of the published package.</returns>
    private static Task<PackageDescriptor> PublishAsync(
        RecordingPackageRepository? repository = null,
        string license = "Propietaria",
        string distributionRestrictions = "Solo para el evento")
    {
        repository ??= new RecordingPackageRepository();

        var contentStore = new FixedContentStore();
        var signer = new FixedSigner();

        var useCase = new PublishPackage(
            new StubGameRepository(license, distributionRestrictions),
            repository,
            contentStore,
            signer,
            new RecordingCatalogChangeNotifier(),
            NullLogger<PublishPackage>.Instance);

        var bytes = contentStore.Bytes;

        return useCase.ExecuteAsync(
            GameId,
            PackageId,
            Version,
            bytes.LongLength,
            null,
            new InstallationManifest.LaunchSettings("game\\game.exe", ["-novid"], "game"),
            [new InstallationManifest.CreateDirectory("game")],
            string.Empty,
            new ManifestIdentity(
                PackageId,
                Version,
                bytes.LongLength,
                InstallationManifestSchema.Version,
                string.Empty),
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// An operation that does not belong to the closed set of AD-7. It exists so that the
    /// guard of the domain can be exercised.
    /// </summary>
    private sealed record UnknownOperation : InstallationManifest.ManifestOperation;

    /// <summary>
    /// Repository of videogames that holds one, con la licencia y las restricciones que
    /// la prueba declara (AD-1).
    /// </summary>
    /// <param name="license">License of the videogame.</param>
    /// <param name="distributionRestrictions">Restrictions of distribution of the videogame.</param>
    private sealed class StubGameRepository : IGameRepository
    {
        private readonly GameRegistration _game;

        public StubGameRepository(string license, string distributionRestrictions)
        {
            _game = GameRegistration.FromPersisted(
                GameId,
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
                "bin\\cs2.exe",
                ["-novid"],
                "game\\bin",
                ["DirectX 12"],
                license,
                distributionRestrictions);
        }

        public Task AddAsync(GameRegistration game, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<GameRegistration?> FindAsync(string gameId, CancellationToken cancellationToken) =>
            Task.FromResult<GameRegistration?>(
                string.Equals(gameId, GameId, StringComparison.Ordinal) ? _game : null);

        public Task<IReadOnlyList<GameRegistration>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<GameRegistration>>([_game]);

        public Task<bool> UpdateAsync(GameRegistration game, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    /// <summary>
    /// Repository of packages that records what the use case persists, para que la
    /// prueba pueda observar que el bloqueo no escribe nada (AC1).
    ///
    /// <para>
    /// El doble modela el contrato del puerto tal como lo cumple el adaptador: la
    /// escritura de la publicación y el crecimiento de la versión del catálogo son la
    /// MISMA unidad de trabajo, así que la versión solo crece cuando la escritura se
    /// confirma (AC3, AD-26).
    /// </para>
    /// </summary>
    private sealed class RecordingPackageRepository : IPackageRepository
    {
        public List<PackageDescriptorRow> AddedDescriptors { get; } = [];

        public List<SignedManifestRow> AddedManifests { get; } = [];

        public long CatalogVersion { get; private set; }

        public Task AddAsync(
            PackageDescriptorRow descriptor,
            SignedManifestRow manifest,
            CancellationToken cancellationToken)
        {
            AddedDescriptors.Add(descriptor);
            AddedManifests.Add(manifest);

            CatalogVersion += 1;

            return Task.CompletedTask;
        }

        public Task<PackageDescriptorRow?> FindAsync(string packageId, CancellationToken cancellationToken) =>
            Task.FromResult<PackageDescriptorRow?>(null);

        public Task<IReadOnlyList<PackageDescriptorRow>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PackageDescriptorRow>>(AddedDescriptors);

        public Task<IReadOnlyList<PublishedPackageRow>> ListPublishedAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PublishedPackageRow>>([]);

        public Task<WithdrawalResult> WithdrawGameAsync(
            string gameId,
            string withdrawnAt,
            CancellationToken cancellationToken) =>
            Task.FromResult(new WithdrawalResult(0, CatalogVersion));

        public Task<long> GetCatalogVersionAsync(CancellationToken cancellationToken) =>
            Task.FromResult(CatalogVersion);
    }

    /// <summary>
    /// Content store that holds a fixed set of bytes, para que el hash que el caso de uso
    /// calcula sea el de unos bytes conocidos (AD-2).
    /// </summary>
    private sealed class FixedContentStore : IPackageContentStore
    {
        public byte[] Bytes { get; } = System.Text.Encoding.UTF8.GetBytes("contenido del paquete");

        public Task<byte[]?> ReadBytesAsync(string packageId, CancellationToken cancellationToken) =>
            Task.FromResult<byte[]?>(Bytes);
    }

    /// <summary>
    /// Signer that returns a signature fija, porque lo que esta prueba mide es el
    /// rechazo del dominio y no la criptografía (AD-2).
    /// </summary>
    private sealed class FixedSigner : IManifestSigner
    {
        public string Sign(string canonicalManifest) => new string('a', 128);

        public string GetKeyId() => "clave-de-prueba";
    }
}
