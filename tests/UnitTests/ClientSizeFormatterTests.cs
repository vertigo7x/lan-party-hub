using LANPartyHub.Client.Presentation.Formatting;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The rule of the sizes AND of the progress in the CLIENT (AD-7, AC1, UX-DR-5).
///
/// <para>
/// There is ONE rule for the sizes: decimal gigabytes, with the Spanish decimal separator
/// and with exactly one decimal. There is no MiB and no GiB, no two decimals and no size
/// without its unit. The client has its own copy of the rule, because the application
/// centre cannot depend on the presentation of the server (AD-18), and the two copies are
/// verified by the SAME expectations: the ficha of the client and the list of the
/// organizer never tell the player two different sizes.
/// </para>
/// </summary>
public sealed class ClientSizeFormatterTests
{
    /// <summary>
    /// A size is in decimal gigabytes, with exactly one decimal and the Spanish decimal
    /// separator (AD-7, UX-DR-5).
    /// </summary>
    [Theory]
    [InlineData(0L, "0,0 GB")]
    [InlineData(1_000_000_000L, "1,0 GB")]
    [InlineData(3_200_000_000L, "3,2 GB")]
    [InlineData(7_600_000_000L, "7,6 GB")]
    public void A_size_is_in_decimal_gigabytes(long sizeBytes, string expected)
    {
        Assert.Equal(expected, SizeFormatter.Format(sizeBytes));
    }

    /// <summary>
    /// La copia del cliente y la del servidor DICEN lo mismo: la regla es una sola
    /// (AD-18, UX-DR-5).
    /// </summary>
    [Theory]
    [InlineData(0L)]
    [InlineData(1_000_000_000L)]
    [InlineData(3_200_000_000L)]
    [InlineData(7_600_000_000L)]
    public void The_two_copies_of_the_rule_say_the_same(long sizeBytes)
    {
        Assert.Equal(
            LANPartyHub.Server.Presentation.Formatting.SizeFormatter.Format(sizeBytes),
            SizeFormatter.Format(sizeBytes));
    }

    /// <summary>
    /// Un PAR de magnitudes se expresa en la MISMA unidad y con la MISMA precisión, y el
    /// total va SIEMPRE segundo (UX-DR-5).
    /// </summary>
    [Fact]
    public void A_pair_is_expressed_in_the_same_unit_with_the_total_second()
    {
        Assert.Equal(
            "3,2 GB de 7,6 GB",
            SizeFormatter.FormatPair(3_200_000_000L, 7_600_000_000L));

        // Sin total conocido, la magnitud va sola.
        Assert.Equal("3,2 GB", SizeFormatter.FormatPair(3_200_000_000L, null));
    }

    /// <summary>
    /// El progreso de una operación es el porcentaje ENTERO y el tamaño, y la barra dibuja
    /// la misma fracción (UX-DR-5, UX-DR-13).
    /// </summary>
    [Fact]
    public void The_progress_is_the_whole_percentage_and_the_size()
    {
        Assert.Equal(
            "42 % · 3,2 GB de 7,6 GB",
            SizeFormatter.FormatProgress(42, 3_200_000_000L, 7_600_000_000L));

        // Una operación completada: el porcentaje entero y el tamaño.
        Assert.Equal(
            "100 % · 7,6 GB",
            SizeFormatter.FormatProgress(100, 7_600_000_000L, 7_600_000_000L));
    }
}
