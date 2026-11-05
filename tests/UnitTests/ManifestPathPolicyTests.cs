using LANPartyHub.Client.Application.Install;
using LANPartyHub.Shared.Contracts;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The policy of the paths of the installation manifest (AD-7, AD-9, NFR-5, AC2).
///
/// <para>
/// El manifiesto NO admite rutas absolutas y NO admite un <c>..</c> que sale de la
/// raÃ­z. El rechazo ocurre ANTES de tocar el disco, y su causa es el cÃ³digo estable
/// del catÃ¡logo (AD-15, AD-25, AD-27).
/// </para>
/// </summary>
public sealed class ManifestPathPolicyTests
{
    [Theory]
    [InlineData(@"C:\juegos\data.bin")]
    [InlineData(@"D:\otro\lugar")]
    [InlineData(@"\\servidor\recurso\archivo.txt")]
    public void An_absolute_path_is_rejected(string path)
    {
        Assert.True(ManifestPathPolicy.IsRejected(path, out var reason));

        Assert.False(string.IsNullOrWhiteSpace(reason));

        var exception = Assert.Throws<PathEscapeException>(
            () => ManifestPathPolicy.RequireRelative(path, "copyFile"));

        Assert.Equal(path, exception.RelativePath);
        Assert.Equal("copyFile", exception.Operation);
        Assert.Equal(ErrorCatalog.InstallPathOutsideRootCode, exception.Cause.Code);
    }

    [Fact]
    public void A_relative_path_is_admissible()
    {
        Assert.True(ManifestPathPolicy.IsAdmissible("game/data.bin"));
        Assert.True(ManifestPathPolicy.IsAdmissible("game/sub/data.bin"));
        Assert.True(ManifestPathPolicy.IsAdmissible("datos.txt"));
    }

    [Fact]
    public void A_relative_path_that_stays_inside_the_root_with_a_dot_dot_is_admissible()
    {
        // Un `..` que sube y baja DENTRO de la raÃ­z no es una escapada.
        Assert.True(ManifestPathPolicy.IsAdmissible("game/sub/../data.bin"));
        Assert.True(ManifestPathPolicy.IsAdmissible("game/../data.bin"));
    }

    [Theory]
    [InlineData("../fuera.txt")]
    [InlineData("game/../../fuera.txt")]
    [InlineData("..")]
    [InlineData("..\\fuera.txt")]
    public void A_relative_path_that_leaves_the_root_is_rejected(string path)
    {
        Assert.True(ManifestPathPolicy.IsRejected(path, out var reason));

        Assert.False(string.IsNullOrWhiteSpace(reason));

        var exception = Assert.Throws<PathEscapeException>(
            () => ManifestPathPolicy.RequireRelative(path, "extract"));

        Assert.Equal(path, exception.RelativePath);
        Assert.Equal(ErrorCatalog.InstallPathOutsideRootCode, exception.Cause.Code);
    }

    [Fact]
    public void An_empty_path_is_rejected()
    {
        Assert.True(ManifestPathPolicy.IsRejected("   ", out var reason));

        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    [Fact]
    public void The_rejection_carries_the_three_parts_of_the_catalog()
    {
        var exception = Assert.Throws<PathEscapeException>(
            () => ManifestPathPolicy.RequireRelative(@"C:\juegos\data.bin", "copyFile"));

        Assert.Equal(ErrorCatalog.InstallPathOutsideRootCode, exception.Cause.Code);
        Assert.False(string.IsNullOrWhiteSpace(exception.Cause.Message));
        Assert.False(string.IsNullOrWhiteSpace(exception.Cause.Cause));
        Assert.False(string.IsNullOrWhiteSpace(exception.Cause.NextStep));
    }

    [Fact]
    public void The_manifest_with_an_absolute_path_is_rejected_before_any_write()
    {
        // AD-9, AC2: la evaluaciÃ³n del manifiesto entero ocurre ANTES de la primera
        // escritura, asÃ­ que el rechazo no deja nada escrito (AD-27).
        var manifest = ManifestWith(
            new InstallationManifest.CreateDirectory("game"),
            new InstallationManifest.CopyFile("data.bin", @"C:\fuera\data.bin"));

        var exception = Assert.Throws<PathEscapeException>(() => ManifestPathPolicy.Evaluate(manifest));

        Assert.Equal("copyFile", exception.Operation);
    }

    [Fact]
    public void The_manifest_with_a_relative_path_is_admitted()
    {
        var manifest = ManifestWith(
            new InstallationManifest.CreateDirectory("game"),
            new InstallationManifest.CopyFile("data.bin", "game/data.bin"),
            new InstallationManifest.WriteConfig("game/config.ini", "clave=valor"),
            new InstallationManifest.Verify("game/data.bin"),
            new InstallationManifest.Extract("paquete.zip", "game"),
            new InstallationManifest.SetLaunchConfig(
                new InstallationManifest.LaunchSettings("game/game.exe", [], "game")));

        ManifestPathPolicy.Evaluate(manifest);
    }

    [Fact]
    public void The_launch_configuration_is_evaluated_too()
    {
        var manifest = ManifestWith(
            new InstallationManifest.SetLaunchConfig(
                new InstallationManifest.LaunchSettings(@"C:\juegos\game.exe", [], "game")));

        var exception = Assert.Throws<PathEscapeException>(() => ManifestPathPolicy.Evaluate(manifest));

        Assert.Equal("setLaunchConfig", exception.Operation);
    }

    [Fact]
    public void The_working_directory_of_the_launch_is_evaluated_too()
    {
        var manifest = ManifestWith(
            new InstallationManifest.SetLaunchConfig(
                new InstallationManifest.LaunchSettings("game/game.exe", [], "../fuera")));

        var exception = Assert.Throws<PathEscapeException>(() => ManifestPathPolicy.Evaluate(manifest));

        Assert.Equal("setLaunchConfig", exception.Operation);
    }

    [Fact]
    public void The_manifest_with_no_operations_is_admitted()
    {
        var manifest = ManifestWith();

        ManifestPathPolicy.Evaluate(manifest);
    }

    /// <summary>
    /// Builds a manifest with the given operations, so that the policy can be exercised
    /// over the real contract of AD-7.
    /// </summary>
    /// <param name="operations">Operations of the manifest.</param>
    /// <returns>The manifest.</returns>
    private static InstallationManifest ManifestWith(
        params InstallationManifest.ManifestOperation[] operations) =>
        new(
            1,
            "0193f2a1-b2c3-4d4e-5f60-718293a4b5c6",
            "1.0.0",
            1024,
            new string('a', 64),
            new InstallationManifest.LaunchSettings("game/game.exe", [], "game"),
            operations);
}
