using LANPartyHub.Client.Domain.Install;
using LANPartyHub.Server.Domain.Packages;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// Parity between the TWO copies of the rule of the form of a path (AD-9, AD-18).
///
/// <para>
/// El cliente y el servidor son proyectos separados y `Shared` contiene solo
/// contratos, asi que la regla esta escrita dos veces: una en el dominio del cliente
/// y otra en el del servidor. Es la MISMA regla, y este test es lo que lo demuestra:
/// si una copia cambia y la otra no, la suite lo dice.
/// </para>
/// </summary>
public sealed class PathFormParityTests
{
    /// <summary>
    /// The paths that decide the rule, and what each copy has to answer.
    /// </summary>
    /// <param name="path">Path as it was written.</param>
    /// <param name="absolute">Whether the path is absolute by its form.</param>
    [Theory]
    [InlineData(@"C:\juegos", true)]
    [InlineData(@"C:\juegos\game", true)]
    [InlineData("C:/juegos", true)]
    [InlineData("c:\\juegos", true)]
    [InlineData("D:", true)]
    [InlineData(@"\\servidor\recurso", true)]
    [InlineData(@"\\servidor\recurso\juego.exe", true)]
    [InlineData("/juegos", true)]
    [InlineData("/juegos/game", true)]
    [InlineData(@"\juegos", true)]
    [InlineData("juegos", false)]
    [InlineData("juegos/game.exe", false)]
    [InlineData(@"juegos\game.exe", false)]
    [InlineData(@"..\juegos", false)]
    [InlineData("../juegos", false)]
    [InlineData(".", false)]
    [InlineData("..", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("1:\\juegos", false)]
    [InlineData("?:\\juegos", false)]
    public void The_two_copies_of_the_rule_answer_the_same(
        string path,
        bool absolute)
    {
        Assert.Equal(absolute, InstallPathConfine.IsAbsoluteByForm(path));
        Assert.Equal(absolute, PathForm.IsAbsoluteByForm(path));
    }

    /// <summary>
    /// A path that is NOT a path at all: the two copies say the same about it.
    /// </summary>
    [Fact]
    public void The_two_copies_of_the_rule_agree_about_a_path_that_is_not_one()
    {
        Assert.Equal(
            InstallPathConfine.IsAbsoluteByForm(null),
            PathForm.IsAbsoluteByForm(null));

        Assert.False(InstallPathConfine.IsAbsoluteByForm(null));
        Assert.False(PathForm.IsAbsoluteByForm(null));
    }
}
