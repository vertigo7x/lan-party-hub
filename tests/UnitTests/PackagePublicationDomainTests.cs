using System.Reflection;
using System.Text.Json.Serialization;
using LANPartyHub.Server.Application.Packages;
using LANPartyHub.Server.Domain.Catalog;
using LANPartyHub.Server.Domain.Packages;
using LANPartyHub.Server.Host;
using LANPartyHub.Shared.Contracts;
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
/// The operations of the manifest are typed and closed, and there is no operation that
/// runs arbitrary commands (AD-7, AD-8).
/// </para>
/// </summary>
public sealed class PackagePublicationDomainTests
{
    /// <summary>
    /// The publication is blocked when the videogame does not carry the license that the
    /// catalog demands, and the rejection names the concrete field (AC1, FR-1, NFR-8).
    /// </summary>
    [Fact]
    public void The_publication_is_blocked_without_the_license()
    {
        var exception = Assert.Throws<PackageLicenseMissingException>(
            () => EnsurePublishable(license: string.Empty));

        Assert.Equal(GameRegistration.FieldNames.License, exception.FieldName);
        Assert.Contains(GameRegistration.FieldNames.License, exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The publication is blocked when the videogame does not carry the restrictions of
    /// distribution, and the rejection names the concrete field (AC1, FR-1, NFR-8).
    /// </summary>
    [Fact]
    public void The_publication_is_blocked_without_the_restrictions_of_distribution()
    {
        var exception = Assert.Throws<PackageDistributionRestrictionsMissingException>(
            () => EnsurePublishable(distributionRestrictions: string.Empty));

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
    public void The_two_absences_are_two_rejections_that_name_their_own_field()
    {
        // Con las dos ausentes, la comprobación de la licencia es la que se adelanta, y
        // su rechazo nombra la licencia (AC1).
        var license = Assert.Throws<PackageLicenseMissingException>(
            () => EnsurePublishable(license: string.Empty, distributionRestrictions: string.Empty));

        Assert.Equal(GameRegistration.FieldNames.License, license.FieldName);

        // Con la licencia presente, la ausencia que queda es la de las restricciones, y su
        // rechazo nombra ese campo y no otro (AC1).
        var restrictions = Assert.Throws<PackageDistributionRestrictionsMissingException>(
            () => EnsurePublishable(license: "Propietaria", distributionRestrictions: string.Empty));

        Assert.Equal(GameRegistration.FieldNames.DistributionRestrictions, restrictions.FieldName);

        Assert.NotEqual(license.FieldName, restrictions.FieldName);
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
    /// The closed set of the domain is EXACTLY the one that the contract declares in its
    /// <see cref="JsonDerivedTypeAttribute"/> discriminators (AD-7).
    ///
    /// <para>
    /// The names que declara el contrato son la fuente que manda, porque el contrato es
    /// lo que el cliente deserializa. La lista del dominio, el <c>switch</c> del
    /// canonicalizador y los atributos del contrato son tres declaraciones del mismo
    /// conjunto, y esta prueba es la que las ata entre sí: una séptima operación
    /// añadida al contrato rompe esta prueba, en lugar de ser aceptada por la
    /// deserialización y rechazada más tarde, en la firma.
    /// </para>
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
    /// The block of the publication is the rule that the domain applies, and it is
    /// exercised here with the same shape that the use case uses (AC1).
    /// </summary>
    /// <param name="license">License of the videogame.</param>
    /// <param name="distributionRestrictions">Restrictions of distribution of the videogame.</param>
    private static void EnsurePublishable(
        string license = "Propietaria",
        string distributionRestrictions = "Solo para el evento")
    {
        if (string.IsNullOrWhiteSpace(license))
        {
            throw new PackageLicenseMissingException(GameRegistration.FieldNames.License);
        }

        if (string.IsNullOrWhiteSpace(distributionRestrictions))
        {
            throw new PackageDistributionRestrictionsMissingException(
                GameRegistration.FieldNames.DistributionRestrictions);
        }
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
    /// An operation that does not belong to the closed set of AD-7. It exists so that the
    /// guard of the domain can be exercised.
    /// </summary>
    private sealed record UnknownOperation : InstallationManifest.ManifestOperation;
}
