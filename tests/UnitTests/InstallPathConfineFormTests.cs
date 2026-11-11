using LANPartyHub.Client.Domain.Install;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The form of a path, recognized WITHOUT the host (AD-9, AC2).
///
/// <para>
/// La forma absoluta se reconoce por SUYO, no por la del host: una ruta que Windows
/// da por absoluta lo es aunque la comprobación corra en otra plataforma, y el
/// manifiesto puede declarar rutas de Windows (AD-9).
/// </para>
/// </summary>
public sealed class InstallPathConfineFormTests
{
    /// <summary>
    /// A path that is ABSOLUTE by its form: a drive, a UNC, or a leading separator.
    /// </summary>
    /// <param name="path">Path as it was written.</param>
    [Theory]
    [InlineData(@"C:\juegos")]
    [InlineData(@"C:\juegos\game")]
    [InlineData("C:/juegos")]
    [InlineData(@"D:")]
    [InlineData(@"\\servidor\recurso")]
    [InlineData(@"\\servidor\recurso\juego.exe")]
    [InlineData("/juegos")]
    [InlineData("/juegos/game")]
    [InlineData(@"\juegos")]
    public void A_path_that_is_absolute_by_its_form_is_recognized(string path)
    {
        Assert.True(InstallPathConfine.IsAbsoluteByForm(path));
    }

    /// <summary>
    /// A RELATIVE path, with or without a separator inside.
    /// </summary>
    /// <param name="path">Path as it was written.</param>
    [Theory]
    [InlineData("juegos")]
    [InlineData("juegos/game.exe")]
    [InlineData(@"juegos\game.exe")]
    [InlineData(@"..\juegos")]
    [InlineData("../juegos")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("")]
    [InlineData("   ")]
    public void A_relative_path_is_not_absolute_by_its_form(string path)
    {
        Assert.False(InstallPathConfine.IsAbsoluteByForm(path));
    }

    /// <summary>
    /// A path that is NOT a path at all.
    /// </summary>
    [Fact]
    public void A_path_that_is_not_a_path_is_not_absolute()
    {
        Assert.False(InstallPathConfine.IsAbsoluteByForm(null));
    }

    /// <summary>
    /// The unit of a path is a DRIVE, and the drive is what the host calls it: Windows
    /// admits any character before the colon as a drive, and this rule admits the same
    /// ones, so that a path is absolute for one platform and for this rule at once
    /// (AD-9).
    /// </summary>
    [Fact]
    public void The_unit_of_a_path_is_the_one_of_windows()
    {
        // Windows da por ABSOLUTA una ruta con cualquier carácter antes de los dos
        // puntos, y esta regla tiene que coincidir con él.
        Assert.True(InstallPathConfine.IsAbsoluteByForm("1:\\juegos"));
        Assert.True(InstallPathConfine.IsAbsoluteByForm("?:\\juegos"));
    }
}
