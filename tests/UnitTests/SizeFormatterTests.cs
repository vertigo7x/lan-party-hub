using LANPartyHub.Server.Presentation.Formatting;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The single rule of the sizes of the interface (AD-7, AC4).
///
/// <para>
/// Every size is expressed in decimal gigabytes, with the Spanish decimal separator and
/// with exactly one decimal. There is no MiB and no GiB, no two decimals and no size
/// without its unit.
/// </para>
/// </summary>
public sealed class SizeFormatterTests
{
    [Fact]
    public void Seven_comma_six_gigabytes_is_the_size_of_seven_billion_six_hundred_million_bytes()
    {
        Assert.Equal("7,6 GB", SizeFormatter.Format(7_600_000_000));
    }

    [Fact]
    public void The_numeric_value_and_the_unit_are_two_parts()
    {
        var (value, unit) = SizeFormatter.Split(7_600_000_000);

        Assert.Equal("7,6", value);
        Assert.Equal("GB", unit);
    }

    [Theory]
    [InlineData(0L, "0,0 GB")]
    [InlineData(1L, "0,0 GB")]
    [InlineData(100_000_000L, "0,1 GB")]
    [InlineData(150_000_000L, "0,2 GB")]
    [InlineData(1_000_000_000L, "1,0 GB")]
    [InlineData(3_200_000_000L, "3,2 GB")]
    [InlineData(7_600_000_000L, "7,6 GB")]
    [InlineData(42_000_000_000L, "42,0 GB")]
    public void The_size_is_expressed_in_decimal_gigabytes_with_one_decimal(long sizeBytes, string expected)
    {
        Assert.Equal(expected, SizeFormatter.Format(sizeBytes));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(999_999_999L)]
    [InlineData(1_000_000_000L)]
    [InlineData(7_600_000_000L)]
    [InlineData(999_999_999_999L)]
    public void The_decimal_separator_is_the_spanish_comma_and_never_the_point(long sizeBytes)
    {
        var formatted = SizeFormatter.Format(sizeBytes);

        Assert.DoesNotContain(".", formatted, StringComparison.Ordinal);
        Assert.Contains(",", formatted, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(999_999_999L)]
    [InlineData(1_000_000_000L)]
    [InlineData(7_600_000_000L)]
    [InlineData(1_050_000_000L)]
    [InlineData(999_999_999_999L)]
    public void The_size_has_exactly_one_decimal_and_never_two(long sizeBytes)
    {
        var formatted = SizeFormatter.Format(sizeBytes);

        var separator = formatted.IndexOf(',', StringComparison.Ordinal);

        Assert.True(separator >= 0, $"The size '{formatted}' has no decimal separator.");

        var decimals = formatted[(separator + 1)..];

        Assert.DoesNotContain(",", decimals, StringComparison.Ordinal);
        Assert.Single(decimals, character => char.IsDigit(character));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(7_600_000_000L)]
    [InlineData(999_999_999_999L)]
    public void The_size_always_carries_its_unit(long sizeBytes)
    {
        var formatted = SizeFormatter.Format(sizeBytes);

        Assert.EndsWith(" GB", formatted, StringComparison.Ordinal);
        Assert.DoesNotContain("MiB", formatted, StringComparison.Ordinal);
        Assert.DoesNotContain("GiB", formatted, StringComparison.Ordinal);
        Assert.DoesNotContain("MB", formatted, StringComparison.Ordinal);
        Assert.DoesNotContain("KB", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void The_formatting_does_not_depend_on_the_culture_of_the_machine()
    {
        var original = Thread.CurrentThread.CurrentCulture;

        try
        {
            // A culture whose decimal separator is the point is installed, and the text of
            // the size has to be the same all the same.
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("en-US");

            Assert.Equal("7,6 GB", SizeFormatter.Format(7_600_000_000));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }

    [Fact]
    public void A_rounding_that_carries_to_the_next_gigabyte_does_not_lose_the_decimal()
    {
        // 1,95 GB rounds to 2,0 GB and not to 2 GB.
        Assert.Equal("2,0 GB", SizeFormatter.Format(1_950_000_000));
    }
}
