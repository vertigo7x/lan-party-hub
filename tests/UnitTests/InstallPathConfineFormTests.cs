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
    /// The unit of a path is a DRIVE, and a drive needs a VALID drive letter: Windows
    /// only admits ASCII letters before the colon, and this rule admits the same ones,
    /// so that a path is absolute for one platform and for this rule at once (AD-9).
    /// </summary>
    /// <remarks>
    /// `Path.IsPathRooted` de .NET exige una letra de unidad valida, asi que `1:\\x` NO
    /// es una ruta absoluta para Windows. Antes de esta regla, el cliente la trataba
    /// como relativa y la servidor como absoluta: las dos copias tienen que coincidir
    /// CON WINDOWS, no entre si.
    /// </remarks>
    [Fact]
    public void The_unit_of_a_path_is_the_one_of_windows()
    {
        // Una letra de unidad, en mayuscula o en minuscula.
        Assert.True(InstallPathConfine.IsAbsoluteByForm("C:\\juegos"));
        Assert.True(InstallPathConfine.IsAbsoluteByForm("c:\\juegos"));

        // Un caracter que NO es una letra de unidad no es una unidad: Windows no lo
        // reconoce, y esta regla tampoco.
        Assert.False(InstallPathConfine.IsAbsoluteByForm("1:\\juegos"));
        Assert.False(InstallPathConfine.IsAbsoluteByForm("?:\\juegos"));
    }
}
