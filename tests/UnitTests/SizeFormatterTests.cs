using System.Globalization;
using LANPartyHub.Server.Presentation.Formatting;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The rule of the sizes of the catalog (AD-7, AC4).
///
/// <para>
/// There is a single rule for the sizes: decimal gigabytes, with the Spanish decimal
/// separator and with exactly one decimal. There is no MiB and no GiB, no two decimals and
/// no size without its unit.
/// </para>
/// </summary>
public sealed class SizeFormatterTests
{
    [Fact]
    public void The_size_of_seven_gigabytes_and_a_half_is_seven_comma_six_gigabytes()
    {
        Assert.Equal("7,6 GB", SizeFormatter.Format(7_600_000_000));
    }

    [Fact]
    public void The_numeric_part_of_the_size_travels_apart_from_the_unit()
    {
        // El valor va en el token de tamaño y la unidad en el token de meta, de modo que
        // los dos tienen que poder separarse (AC4).
        var (value, unit) = SizeFormatter.Split(7_600_000_000);

        Assert.Equal("7,6", value);
        Assert.Equal("GB", unit);
    }

    [Fact]
    public void The_decimal_separator_is_the_spanish_comma()
    {
        Assert.Equal("7,6", SizeFormatter.FormatValue(7_600_000_000));
        Assert.DoesNotContain(".", SizeFormatter.FormatValue(7_600_000_000), StringComparison.Ordinal);
    }

    [Fact]
    public void The_decimal_separator_does_not_depend_on_the_culture_of_the_machine()
    {
        var previous = CultureInfo.CurrentCulture;

        try
        {
            // Con una cultura que usa el punto decimal, el texto del tamaño no cambia: el
            // separador se escribe a mano y no por un formateador de la cultura.
            CultureInfo.CurrentCulture = new CultureInfo("en-US");

            Assert.Equal("7,6 GB", SizeFormatter.Format(7_600_000_000));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData(0, "0,0")]
    [InlineData(1, "0,0")]
    [InlineData(500_000_000, "0,5")]
    [InlineData(1_000_000_000, "1,0")]
    [InlineData(1_500_000_000, "1,5")]
    [InlineData(7_600_000_000, "7,6")]
    [InlineData(10_000_000_000, "10,0")]
    [InlineData(123_400_000_000, "123,4")]
    public void The_size_has_exactly_one_decimal(long sizeBytes, string expected)
    {
        Assert.Equal(expected, SizeFormatter.FormatValue(sizeBytes));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7_600_000_000)]
    [InlineData(123_400_000_000)]
    public void The_size_never_carries_two_decimals(long sizeBytes)
    {
        var value = SizeFormatter.FormatValue(sizeBytes);

        Assert.Equal(1, value.Count(character => character == ','));
        Assert.DoesNotContain(".", value, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7_600_000_000)]
    [InlineData(123_400_000_000)]
    public void The_size_never_travels_without_its_unit(long sizeBytes)
    {
        var text = SizeFormatter.Format(sizeBytes);

        Assert.EndsWith(" " + SizeFormatter.Unit, text, StringComparison.Ordinal);
        Assert.Contains(SizeFormatter.Unit, text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_size_never_uses_the_binary_units()
    {
        var text = SizeFormatter.Format(7_600_000_000);

        Assert.DoesNotContain("MiB", text, StringComparison.Ordinal);
        Assert.DoesNotContain("GiB", text, StringComparison.Ordinal);
        Assert.DoesNotContain("MB", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_size_is_computed_in_decimal_gigabytes_and_not_in_binary_gigabytes()
    {
        // Un gigabyte binario son 1.073.741.824 bytes, y un tamaño de 1.073.741.824 bytes
        // son 1,1 GB decimales, no 1,0 GiB.
        Assert.Equal("1,1", SizeFormatter.FormatValue(1_073_741_824));
    }

    [Fact]
    public void The_tenth_of_a_gigabyte_is_rounded_to_the_nearest_one()
    {
        // El resto se calcula con enteros, de modo que el redondeo no depende de la
        // precisión de un número de coma flotante.
        Assert.Equal("1,0", SizeFormatter.FormatValue(1_040_000_000));
        Assert.Equal("1,1", SizeFormatter.FormatValue(1_050_000_000));
        Assert.Equal("1,1", SizeFormatter.FormatValue(1_060_000_000));
    }

    [Fact]
    public void The_size_that_carries_a_whole_gigabyte_does_not_roll_over()
    {
        // El resto exacto de un gigabyte no puede producir un décimo que se lleva a un
        // gigabyte entero.
        Assert.Equal("1,0", SizeFormatter.FormatValue(1_000_000_000));
        Assert.Equal("2,0", SizeFormatter.FormatValue(2_000_000_000));
    }
}
