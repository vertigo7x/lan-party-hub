using LANPartyHub.Client.Domain.Install;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The confinement of the paths of the installation (AD-9, NFR-5, AC1, AC3).
///
/// <para>
/// El confine es un CASO UNITARIO: la regla es pura, no toca el disco y no conoce el
/// adaptador (AD-16). El caso que decide es el PREFIXO HERMANO: la raíz
/// <c>C:\juegos</c> no confina a <c>C:\juegos-evil</c>, porque el prefijo de texto
/// coincide pero el separador no.
/// </para>
/// </summary>
public sealed class InstallPathConfinementTests
{
    [Fact]
    public void A_path_inside_the_root_is_confined()
    {
        Assert.True(InstallPathConfine.IsInside(@"C:\juegos", @"C:\juegos\game\data.bin"));
    }

    [Fact]
    public void The_root_itself_is_inside_the_root()
    {
        // La raíz es un destino LEGAL: escribir en la raíz no sale de la raíz (AD-9).
        Assert.True(InstallPathConfine.IsInside(@"C:\juegos", @"C:\juegos"));
    }

    [Fact]
    public void The_root_with_a_trailing_separator_is_the_same_root()
    {
        Assert.True(InstallPathConfine.IsInside(@"C:\juegos\", @"C:\juegos"));
        Assert.True(InstallPathConfine.IsInside(@"C:\juegos", @"C:\juegos\"));
    }

    [Fact]
    public void A_path_that_leaves_the_root_is_outside()
    {
        Assert.True(InstallPathConfine.IsOutside(@"C:\juegos", @"C:\juegos-evil\datos.txt"));
    }

    [Fact]
    public void The_sibling_that_shares_the_prefix_of_the_root_is_outside()
    {
        // AD-9: el prefijo lleva el separador final, así que una carpeta hermana que
        // comparte el NOMBRE de la raíz no está dentro.
        Assert.False(InstallPathConfine.IsInside(@"C:\juegos", @"C:\juegos-evil"));
        Assert.False(InstallPathConfine.IsInside(@"C:\juegos", @"C:\juegosos"));
        Assert.False(InstallPathConfine.IsInside(@"C:\juegos", @"C:\juegos2\data.bin"));
    }

    [Fact]
    public void The_comparison_does_not_distinguish_mayusculas()
    {
        // AD-17: el único objetivo del cliente es Windows, y allí `C:\Juegos` y
        // `c:\juegos` son la MISMA carpeta.
        Assert.True(InstallPathConfine.IsInside(@"C:\Juegos", @"c:\juegos\data.bin"));
    }

    [Fact]
    public void A_path_that_is_a_prefix_of_the_root_but_not_inside_it_is_outside()
    {
        Assert.True(InstallPathConfine.IsOutside(@"C:\juegos\instalado", @"C:\juegos"));
    }

    [Fact]
    public void The_rule_rejects_an_empty_root()
    {
        Assert.Throws<ArgumentException>(() => InstallPathConfine.IsInside("  ", @"C:\juegos"));
    }

    [Fact]
    public void The_rule_rejects_an_empty_path()
    {
        Assert.Throws<ArgumentException>(() => InstallPathConfine.IsInside(@"C:\juegos", "  "));
    }

    [Fact]
    public void Is_outside_is_the_negation_of_is_inside()
    {
        Assert.Equal(
            !InstallPathConfine.IsInside(@"C:\juegos", @"C:\juegos\data.bin"),
            InstallPathConfine.IsOutside(@"C:\juegos", @"C:\juegos\data.bin"));

        Assert.Equal(
            !InstallPathConfine.IsInside(@"C:\juegos", @"C:\otro\data.bin"),
            InstallPathConfine.IsOutside(@"C:\juegos", @"C:\otro\data.bin"));
    }
}
