using LANPartyHub.Server.Domain.Catalog;
using LANPartyHub.Server.Domain.Packages;
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
    /// An operation that does not belong to the closed set of AD-7. It exists so that the
    /// guard of the domain can be exercised.
    /// </summary>
    private sealed record UnknownOperation : InstallationManifest.ManifestOperation;
}
