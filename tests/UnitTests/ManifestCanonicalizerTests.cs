using LANPartyHub.Server.Domain.Packages;
using LANPartyHub.Shared.Contracts;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The canonical form of the installation manifest (AD-2, AD-7, AD-8).
///
/// <para>
/// The signature covers the canonical form of the manifest, so the canonical form is the
/// piece that the signer and the verifier share: it has to be stable, free of superfluous
/// whitespace and independent of the order of declaration of the types (AD-2).
/// </para>
/// </summary>
public sealed class ManifestCanonicalizerTests
{
    /// <summary>
    /// The canonical text carries no superfluous whitespace (AD-2).
    /// </summary>
    [Fact]
    public void The_canonical_text_carries_no_superfluous_whitespace()
    {
        var canonical = ManifestCanonicalizer.Canonicalize(BuildManifest());

        Assert.DoesNotContain("\n", canonical, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", canonical, StringComparison.Ordinal);
        Assert.DoesNotContain("\t", canonical, StringComparison.Ordinal);
        Assert.DoesNotContain(": ", canonical, StringComparison.Ordinal);
        Assert.DoesNotContain(", ", canonical, StringComparison.Ordinal);
    }

    /// <summary>
    /// The keys of the manifest are sorted by name, and not by the order of declaration of
    /// the contract (AD-2).
    /// </summary>
    [Fact]
    public void The_keys_of_the_manifest_are_sorted_by_name()
    {
        var canonical = ManifestCanonicalizer.Canonicalize(BuildManifest());

        var launch = canonical.IndexOf("\"launch\"", StringComparison.Ordinal);
        var operations = canonical.IndexOf("\"operations\"", StringComparison.Ordinal);
        var packageId = canonical.IndexOf("\"packageId\"", StringComparison.Ordinal);
        var packageVersion = canonical.IndexOf("\"packageVersion\"", StringComparison.Ordinal);
        var schemaVersion = canonical.IndexOf("\"schemaVersion\"", StringComparison.Ordinal);
        var sha256 = canonical.IndexOf("\"sha256\"", StringComparison.Ordinal);
        var sizeBytes = canonical.IndexOf("\"sizeBytes\"", StringComparison.Ordinal);

        Assert.True(launch >= 0);
        Assert.True(launch < operations);
        Assert.True(operations < packageId);
        Assert.True(packageId < packageVersion);
        Assert.True(packageVersion < schemaVersion);
        Assert.True(schemaVersion < sha256);
        Assert.True(sha256 < sizeBytes);
    }

    /// <summary>
    /// The same manifest always produces the same canonical text, so the signature of a
    /// manifest is reproducible (AD-2).
    /// </summary>
    [Fact]
    public void The_same_manifest_always_produces_the_same_canonical_text()
    {
        var first = ManifestCanonicalizer.Canonicalize(BuildManifest());
        var second = ManifestCanonicalizer.Canonicalize(BuildManifest());

        Assert.Equal(first, second);
    }

    /// <summary>
    /// The canonical text is the exact one that is signed, and the signature covers it
    /// (AD-2).
    /// </summary>
    [Fact]
    public void The_canonical_text_is_the_one_that_the_signature_covers()
    {
        var canonical = ManifestCanonicalizer.Canonicalize(BuildManifest());

        Assert.True(LANPartyHub.Server.Infrastructure.Signing.Ed25519ManifestSigner.Verify(
            canonical,
            Sign(canonical),
            Environment.MachineName));
    }

    /// <summary>
    /// The discriminator of the polymorphic operations travels with the properties of its
    /// derived type, and the keys of the operation are sorted as well (AD-2, AD-7).
    /// </summary>
    [Fact]
    public void The_operations_carry_their_discriminator_and_sorted_keys()
    {
        var canonical = ManifestCanonicalizer.Canonicalize(BuildManifest());

        Assert.Contains("\"operation\":\"extract\"", canonical, StringComparison.Ordinal);
        Assert.Contains("\"operation\":\"createDirectory\"", canonical, StringComparison.Ordinal);
        Assert.Contains("\"operation\":\"setLaunchConfig\"", canonical, StringComparison.Ordinal);

        // The keys of the extract operation are sorted by name: the archive path comes
        // before the destination path (AD-2).
        var archive = canonical.IndexOf("\"archivePath\"", StringComparison.Ordinal);
        var destination = canonical.IndexOf("\"destinationPath\"", StringComparison.Ordinal);
        var extract = canonical.IndexOf("\"operation\":\"extract\"", StringComparison.Ordinal);

        Assert.True(archive >= 0);
        Assert.True(archive < destination);

        // The discriminator leads the object, so it comes before the properties of the
        // derived type.
        Assert.True(extract < archive);
    }

    /// <summary>
    /// The discriminator of an operation LEADS its object: it is the type tag of the
    /// operation and the serializer of the contract of <c>Shared</c> requires it to be the
    /// first property, so a canonical text that puts it after the properties cannot be
    /// read back (AD-2, AD-7).
    /// </summary>
    [Fact]
    public void The_discriminator_leads_the_object_of_its_operation()
    {
        var canonical = ManifestCanonicalizer.Canonicalize(BuildManifest());

        var operation = canonical.IndexOf("\"operation\":\"extract\"", StringComparison.Ordinal);
        var objectStart = canonical.LastIndexOf('{', operation);
        var archive = canonical.IndexOf("\"archivePath\"", StringComparison.Ordinal);

        Assert.True(operation >= 0);
        Assert.True(objectStart >= 0);
        Assert.True(archive >= 0);

        // The discriminator is the first key of the object: the opening brace is
        // immediately followed by the name of the property.
        Assert.StartsWith("\"operation\"", canonical[(objectStart + 1)..], StringComparison.Ordinal);
        Assert.Equal(objectStart + 1, operation);

        // And it comes before the properties of the derived type.
        Assert.True(operation < archive);
    }

    /// <summary>
    /// The canonical text is READABLE BACK by the serializer of the contract of
    /// <c>Shared</c>, which is what makes it verifiable by the client of the Story 6.1: a
    /// canonical text that nobody can deserialize does not authenticate anything (AD-2).
    /// </summary>
    [Fact]
    public void The_canonical_text_can_be_read_back_by_the_contract()
    {
        var manifest = BuildManifest();

        var canonical = ManifestCanonicalizer.Canonicalize(manifest);

        var read = System.Text.Json.JsonSerializer.Deserialize<InstallationManifest>(
            canonical,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

        Assert.NotNull(read);
        Assert.Equal(manifest.SchemaVersion, read!.SchemaVersion);
        Assert.Equal(manifest.PackageId, read.PackageId);
        Assert.Equal(manifest.PackageVersion, read.PackageVersion);
        Assert.Equal(manifest.SizeBytes, read.SizeBytes);
        Assert.Equal(manifest.Sha256, read.Sha256);
        Assert.Equal(manifest.Launch.ExecutablePath, read.Launch!.ExecutablePath);
        Assert.Equal(manifest.Launch.WorkingDirectory, read.Launch.WorkingDirectory);
        Assert.Equal(manifest.Launch.Arguments, read.Launch.Arguments);

        Assert.Equal(manifest.Operations.Count, read.Operations.Count);
        Assert.IsType<InstallationManifest.CreateDirectory>(read.Operations[0]);
        Assert.IsType<InstallationManifest.Extract>(read.Operations[1]);
        Assert.IsType<InstallationManifest.SetLaunchConfig>(read.Operations[2]);
        Assert.Equal("game.zip", Assert.IsType<InstallationManifest.Extract>(read.Operations[1]).ArchivePath);
        Assert.Equal("game", Assert.IsType<InstallationManifest.Extract>(read.Operations[1]).DestinationPath);
    }

    /// <summary>
    /// The exact bytes of the canonical form of a fixed manifest are pinned, so a change
    /// of the canonical form breaks this test instead of silently invalidating the
    /// signatures that were produced with it (AD-2).
    /// </summary>
    [Fact]
    public void The_canonical_text_of_a_fixed_manifest_is_exactly_the_pinned_one()
    {
        const string expected =
            "{\"launch\":{\"arguments\":[\"-windowed\"],\"executablePath\":\"game\\\\game.exe\",\"workingDirectory\":\"game\"},"
            + "\"operations\":["
            + "{\"operation\":\"createDirectory\",\"path\":\"game\"},"
            + "{\"operation\":\"extract\",\"archivePath\":\"game.zip\",\"destinationPath\":\"game\"},"
            + "{\"operation\":\"setLaunchConfig\","
            + "\"launch\":{\"arguments\":[\"-windowed\"],\"executablePath\":\"game\\\\game.exe\",\"workingDirectory\":\"game\"}}],"
            + "\"packageId\":\"0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e40\","
            + "\"packageVersion\":\"1.0.0\","
            + "\"schemaVersion\":1,"
            + "\"sha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\","
            + "\"sizeBytes\":1024}";

        var canonical = ManifestCanonicalizer.Canonicalize(BuildManifest());

        Assert.Equal(expected, canonical);
    }

    /// <summary>
    /// The order of the operations is the order of application, so it is preserved in the
    /// canonical text (AD-7).
    /// </summary>
    [Fact]
    public void The_order_of_the_operations_is_preserved()
    {
        var canonical = ManifestCanonicalizer.Canonicalize(BuildManifest());

        var create = canonical.IndexOf("\"operation\":\"createDirectory\"", StringComparison.Ordinal);
        var extract = canonical.IndexOf("\"operation\":\"extract\"", StringComparison.Ordinal);
        var launch = canonical.IndexOf("\"operation\":\"setLaunchConfig\"", StringComparison.Ordinal);

        Assert.True(create < extract);
        Assert.True(extract < launch);
    }

    /// <summary>
    /// A manifest that is not one of the six operations of the closed set is rejected by
    /// the canonicalizer, and it never reaches the signature (AD-7, AD-8).
    /// </summary>
    [Fact]
    public void An_operation_outside_the_closed_set_is_rejected()
    {
        var manifest = BuildManifest() with
        {
            Operations = [new UnknownOperation()],
        };

        Assert.Throws<ArgumentException>(() => ManifestCanonicalizer.Canonicalize(manifest));
    }

    private static string Sign(string canonicalManifest)
    {
        var signer = new LANPartyHub.Server.Infrastructure.Signing.Ed25519ManifestSigner();

        return signer.Sign(canonicalManifest);
    }

    private static InstallationManifest BuildManifest() => new(
        1,
        "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e40",
        "1.0.0",
        1024,
        new string('a', 64),
        new InstallationManifest.LaunchSettings("game\\game.exe", ["-windowed"], "game"),
        [
            new InstallationManifest.CreateDirectory("game"),
            new InstallationManifest.Extract("game.zip", "game"),
            new InstallationManifest.SetLaunchConfig(
                new InstallationManifest.LaunchSettings("game\\game.exe", ["-windowed"], "game")),
        ]);

    /// <summary>
    /// An operation that does not belong to the closed set of AD-7. It exists so that the
    /// guard of the canonicalizer can be exercised.
    /// </summary>
    private sealed record UnknownOperation : InstallationManifest.ManifestOperation;
}
