using LANPartyHub.Shared.Contracts;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The semantic order of the versions of the catalog (AC3, AR-5, AD-10).
///
/// <para>
/// The version of a videogame is an opaque string that the editor writes, and the catalog
/// declares the order of its versions. The order is NEVER the lexicographic one: that one
/// says that «1.10» is older than «1.9», which would mark a game as outdated when it is
/// not.
/// </para>
/// </summary>
public sealed class CatalogVersionOrderTests
{
    [Fact]
    public void A_version_with_two_digits_is_newer_than_the_one_with_one_digit()
    {
        // El orden LEXICOGRÁFICO diría que «1.10» es anterior a «1.9»: la comparación
        // numérica por segmentos es la que manda (AC3, AR-5).
        Assert.True(CatalogVersionOrder.IsNewer("1.10", "1.9"));
        Assert.False(CatalogVersionOrder.IsOlder("1.10", "1.9"));
        Assert.True(CatalogVersionOrder.IsOlder("1.9", "1.10"));
    }

    [Fact]
    public void The_order_of_the_segments_is_the_order_of_the_catalog()
    {
        Assert.True(CatalogVersionOrder.IsNewer("1.2.0", "1.1.9"));
        Assert.True(CatalogVersionOrder.IsNewer("2.0.0", "1.99.99"));
        Assert.True(CatalogVersionOrder.IsNewer("1.10.3", "1.9.3"));
    }

    [Fact]
    public void The_same_version_is_not_older_and_not_newer()
    {
        Assert.Equal(0, CatalogVersionOrder.Compare("1.10", "1.10"));
        Assert.Equal(0, CatalogVersionOrder.Compare("1.10.0", "1.10.0"));
        Assert.False(CatalogVersionOrder.IsOlder("1.10", "1.10"));
        Assert.False(CatalogVersionOrder.IsNewer("1.10", "1.10"));
    }

    [Fact]
    public void A_version_with_more_segments_is_newer()
    {
        Assert.True(CatalogVersionOrder.IsNewer("1.2.1", "1.2"));
        Assert.True(CatalogVersionOrder.IsOlder("1.2", "1.2.1"));
    }

    [Fact]
    public void The_comparison_ignores_the_spaces_around_the_segments()
    {
        Assert.Equal(0, CatalogVersionOrder.Compare(" 1 . 10 ", "1.10"));
    }

    [Fact]
    public void An_empty_version_is_older_than_any_version()
    {
        Assert.True(CatalogVersionOrder.IsOlder(string.Empty, "1.0"));
        Assert.True(CatalogVersionOrder.IsNewer("1.0", string.Empty));
        Assert.Equal(0, CatalogVersionOrder.Compare(null, null));
        Assert.True(CatalogVersionOrder.IsOlder(null, "1.0"));
    }

    [Fact]
    public void A_segment_that_is_not_a_number_is_compared_as_a_text()
    {
        // La versión es opaca: el catálogo no rechaza una versión por no ser numérica, así
        // que la regla la compara como texto (AD-10).
        Assert.True(CatalogVersionOrder.IsNewer("1.0.beta", "1.0.alpha"));
        Assert.True(CatalogVersionOrder.IsOlder("1.0.alpha", "1.0.beta"));
    }

    [Fact]
    public void A_numeric_segment_is_newer_than_a_segment_that_is_not_a_number()
    {
        Assert.True(CatalogVersionOrder.IsNewer("1.2", "1.beta"));
    }

    [Fact]
    public void The_order_is_the_one_that_the_catalog_declares_and_not_the_lexicographic_one()
    {
        // La prueba que falla con el orden lexicográfico: la comparación por segmentos
        // numéricos es la regla única (AC3, AR-5).
        var versions = new[] { "1.9", "1.10", "1.2", "1.10.1" };

        var ordered = versions
            .OrderBy(version => version, Comparer<string>.Create(CatalogVersionOrder.Compare))
            .ToArray();

        Assert.Equal(["1.2", "1.9", "1.10", "1.10.1"], ordered);
    }
}
