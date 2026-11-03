using LANPartyHub.Client.Application.Downloads;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// Progress of a download operation (AC2, UX-DR-5, UX-DR-13).
///
/// <para>
/// La barra y el texto salen de UNA SOLA fuente: el porcentaje es un entero, el par de
/// tamaños es «&lt;a&gt; GB de &lt;t&gt; GB» con el total siempre segundo, y la fracción
/// que la barra dibuja es EXACTAMENTE la que el porcentaje declara (UX-DR-5, UX-DR-13).
/// </para>
/// </summary>
public sealed class DownloadProgressTests
{
    [Theory]
    [InlineData(0, 1000, 0)]
    [InlineData(250, 1000, 25)]
    [InlineData(420, 1000, 42)]
    [InlineData(999, 1000, 99)]
    [InlineData(1000, 1000, 100)]
    public void The_percentage_is_an_integer_derived_from_the_bytes(long transferred, long total, int expected)
    {
        var progress = new DownloadProgress(transferred, total);

        Assert.Equal(expected, progress.Percentage);
    }

    [Fact]
    public void The_bar_draws_the_same_fraction_that_the_percentage_declares()
    {
        var progress = new DownloadProgress(3_200_000_000, 7_600_000_000);

        // UX-DR-5, UX-DR-13: la barra y el texto NUNCA se contradicen.
        Assert.Equal(progress.Percentage / 100d, progress.Fraction, 10);
        Assert.Equal(42, progress.Percentage);
    }

    [Fact]
    public void The_pair_of_sizes_puts_the_total_second()
    {
        var progress = new DownloadProgress(3_200_000_000, 7_600_000_000);

        Assert.Equal("3,2 GB de 7,6 GB", progress.SizesText);
    }

    [Fact]
    public void The_text_of_the_progress_puts_the_percentage_first()
    {
        var progress = new DownloadProgress(3_200_000_000, 7_600_000_000);

        Assert.Equal("42 % · 3,2 GB de 7,6 GB", progress.Text);
    }

    [Fact]
    public void The_sizes_use_the_decimal_gigabyte_with_one_decimal()
    {
        // UX-DR-5: los tamaños son gigabytes DECIMALES con un solo decimal y la coma
        // decimal española. Nunca «7,60 GB», ni «7 GB», ni «7,6 GiB».
        var progress = new DownloadProgress(7_600_000_000, 7_600_000_000);

        Assert.Equal("7,6 GB de 7,6 GB", progress.SizesText);
        Assert.Equal("7,6", DownloadProgress.FormatGigabytes(7_600_000_000));
        Assert.Equal("0,0", DownloadProgress.FormatGigabytes(0));
        Assert.Equal("1,0", DownloadProgress.FormatGigabytes(1_000_000_000));
    }

    [Fact]
    public void The_size_rounds_the_tenth_to_the_nearest_one()
    {
        // El redondeo se hace con enteros, así que no depende de la precisión de un
        // flotante: 0,05 GB sube a 0,1 y 0,04 GB se queda en 0,0.
        Assert.Equal("0,1", DownloadProgress.FormatGigabytes(50_000_000));
        Assert.Equal("0,0", DownloadProgress.FormatGigabytes(40_000_000));
        Assert.Equal("0,1", DownloadProgress.FormatGigabytes(60_000_000));
    }

    [Fact]
    public void The_percentage_is_zero_while_the_total_is_unknown()
    {
        // Un porcentaje sobre un total que no se conoce sería una mentira.
        var progress = new DownloadProgress(1_000_000, null);

        Assert.Equal(0, progress.Percentage);
        Assert.Equal(0d, progress.Fraction);
        Assert.Equal("1,0 GB", progress.SizesText);
    }

    [Fact]
    public void The_percentage_never_goes_beyond_a_hundred()
    {
        var progress = new DownloadProgress(2_000, 1_000);

        Assert.Equal(100, progress.Percentage);
        Assert.Equal(1d, progress.Fraction);
    }

    [Fact]
    public void A_negative_offset_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DownloadProgress(-1, 1000));
    }

    [Fact]
    public void A_negative_total_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DownloadProgress(0, -1));
    }
}
