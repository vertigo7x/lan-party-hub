using LANPartyHub.Client.Domain.Install;
using LANPartyHub.Shared.Contracts;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The rule that says which operation of the installation manifest is PRIVILEGED (AD-7,
/// AC2).
///
/// <para>
/// La regla es PURA y vive en el dominio, así que el caso unitario la ejercita sin disco
/// y sin red (AD-16).
/// </para>
/// </summary>
public sealed class PrivilegedOperationPolicyTests
{
    /// <summary>
    /// The operation that writes CONFIGURATION is privileged: it writes content that is
    /// NOT the bytes of the validated package (AD-7, AC2).
    /// </summary>
    [Fact]
    public void The_operation_that_writes_a_configuration_is_privileged()
    {
        var operation = new InstallationManifest.WriteConfig("config/options.ini", "modo=1");

        Assert.True(PrivilegedOperationPolicy.IsPrivileged(operation));
    }

    /// <summary>
    /// The operation that MATERIALIZES the launch configuration is privileged: it writes
    /// outside the bytes of the package (AD-7, AC2).
    /// </summary>
    [Fact]
    public void The_operation_that_materializes_the_launch_configuration_is_privileged()
    {
        var operation = new InstallationManifest.SetLaunchConfig(
            new InstallationManifest.LaunchSettings("game.exe", ["-novid"], "game"));

        Assert.True(PrivilegedOperationPolicy.IsPrivileged(operation));
    }

    /// <summary>
    /// The operation that EXTRACTS only materializes the bytes that the signed manifest
    /// already authorized, so it does NOT ask (AD-7, AC2).
    /// </summary>
    [Fact]
    public void The_extraction_does_not_ask_because_it_only_materializes_signed_bytes()
    {
        var operation = new InstallationManifest.Extract("data.zip", "data");

        Assert.False(PrivilegedOperationPolicy.IsPrivileged(operation));
    }

    /// <summary>
    /// The operation that COPIES a file only materializes signed bytes, so it does NOT
    /// ask (AD-7, AC2).
    /// </summary>
    [Fact]
    public void The_copy_does_not_ask_because_it_only_materializes_signed_bytes()
    {
        var operation = new InstallationManifest.CopyFile("readme.txt", "docs/readme.txt");

        Assert.False(PrivilegedOperationPolicy.IsPrivileged(operation));
    }

    /// <summary>
    /// The operation that CREATES a directory only materializes signed bytes, so it does
    /// NOT ask (AD-7, AC2).
    /// </summary>
    [Fact]
    public void The_creation_of_a_directory_does_not_ask()
    {
        var operation = new InstallationManifest.CreateDirectory("docs");

        Assert.False(PrivilegedOperationPolicy.IsPrivileged(operation));
    }

    /// <summary>
    /// The operation that VERIFIES the tree does not write anything, so it does NOT ask
    /// (AD-7, AC2).
    /// </summary>
    [Fact]
    public void The_verification_does_not_ask_because_it_writes_nothing()
    {
        var operation = new InstallationManifest.Verify("game/game.exe");

        Assert.False(PrivilegedOperationPolicy.IsPrivileged(operation));
    }

    /// <summary>
    /// EXACTLY TWO of the six operations of the closed set are privileged, so the rule is
    /// a rule and not a coincidence operation by operation (AD-7, AC2).
    /// </summary>
    [Fact]
    public void Exactly_two_of_the_six_operations_of_the_closed_set_are_privileged()
    {
        InstallationManifest.ManifestOperation[] operations =
        [
            new InstallationManifest.Extract("data.zip", "data"),
            new InstallationManifest.CreateDirectory("docs"),
            new InstallationManifest.CopyFile("readme.txt", "docs/readme.txt"),
            new InstallationManifest.WriteConfig("config/options.ini", "modo=1"),
            new InstallationManifest.SetLaunchConfig(
                new InstallationManifest.LaunchSettings("game.exe", ["-novid"], "game")),
            new InstallationManifest.Verify("game/game.exe"),
        ];

        Assert.Equal(6, operations.Length);

        Assert.Equal(
            2,
            operations.Count(PrivilegedOperationPolicy.IsPrivileged));
    }

    /// <summary>
    /// The rule by NAME is the SAME rule as the rule by TYPE: the interface and the
    /// executor cannot disagree about what is privileged (AD-1, AD-7, AC2).
    /// </summary>
    [Fact]
    public void The_rule_by_name_is_the_same_rule_as_the_rule_by_type()
    {
        InstallationManifest.ManifestOperation[] operations =
        [
            new InstallationManifest.Extract("data.zip", "data"),
            new InstallationManifest.CreateDirectory("docs"),
            new InstallationManifest.CopyFile("readme.txt", "docs/readme.txt"),
            new InstallationManifest.WriteConfig("config/options.ini", "modo=1"),
            new InstallationManifest.SetLaunchConfig(
                new InstallationManifest.LaunchSettings("game.exe", ["-novid"], "game")),
            new InstallationManifest.Verify("game/game.exe"),
        ];

        foreach (var operation in operations)
        {
            var name = PrivilegedOperationPolicy.NameOf(operation);

            Assert.NotNull(name);
            Assert.Equal(
                PrivilegedOperationPolicy.IsPrivileged(operation),
                PrivilegedOperationPolicy.IsPrivileged(name));
        }
    }

    /// <summary>
    /// The names of the six operations are the ones that the discriminator del contrato
    /// writes, so the rule names the operation the way the contract does (AD-7, AD-15).
    /// </summary>
    [Fact]
    public void The_names_of_the_operations_are_the_ones_of_the_contract()
    {
        Assert.Equal(
            "extract",
            PrivilegedOperationPolicy.NameOf(new InstallationManifest.Extract("a.zip", "a")));

        Assert.Equal(
            "createDirectory",
            PrivilegedOperationPolicy.NameOf(new InstallationManifest.CreateDirectory("docs")));

        Assert.Equal(
            "copyFile",
            PrivilegedOperationPolicy.NameOf(
                new InstallationManifest.CopyFile("readme.txt", "docs/readme.txt")));

        Assert.Equal(
            "writeConfig",
            PrivilegedOperationPolicy.NameOf(
                new InstallationManifest.WriteConfig("config/options.ini", "modo=1")));

        Assert.Equal(
            "setLaunchConfig",
            PrivilegedOperationPolicy.NameOf(new InstallationManifest.SetLaunchConfig(
                new InstallationManifest.LaunchSettings("game.exe", ["-novid"], "game"))));

        Assert.Equal(
            "verify",
            PrivilegedOperationPolicy.NameOf(new InstallationManifest.Verify("game/game.exe")));
    }

    /// <summary>
    /// An operation that is NOT part of the closed set is never privileged: it never
    /// executes, so there is nothing to confirm (AD-7, AC2).
    /// </summary>
    [Fact]
    public void An_operation_outside_the_closed_set_is_never_privileged()
    {
        Assert.False(PrivilegedOperationPolicy.IsPrivileged((InstallationManifest.ManifestOperation?)null));
        Assert.False(PrivilegedOperationPolicy.IsPrivileged("runCommand"));
        Assert.False(PrivilegedOperationPolicy.IsPrivileged("WRITECONFIG"));
        Assert.Null(PrivilegedOperationPolicy.NameOf(null));
    }
}
