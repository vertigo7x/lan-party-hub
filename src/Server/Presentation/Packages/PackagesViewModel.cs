using LANPartyHub.Server.Application.Packages;
using LANPartyHub.Server.Presentation.Formatting;
using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Server.Presentation.Packages;

/// <summary>
/// View model of the section of the packages of the event (AD-1, AC1).
///
/// <para>
/// The view model holds the list of the published packages and it offers the publication of
/// a new one. It holds no rule of the domain: the validation of the publication belongs to
/// the domain (AD-1).
/// </para>
/// </summary>
public sealed class PackagesViewModel
{
    private readonly List<PackageDescriptor> _packages = [];

    /// <summary>Creates the view model of the section of the packages.</summary>
    public PackagesViewModel()
    {
    }

    /// <summary>
    /// Creates the view model of the section of the packages with the packages that are
    /// already published.
    /// </summary>
    /// <param name="packages">Packages that are published.</param>
    public PackagesViewModel(IEnumerable<PackageDescriptor> packages)
    {
        ArgumentNullException.ThrowIfNull(packages);

        _packages.AddRange(packages);
    }

    /// <summary>
    /// The published packages, as the list shows them.
    /// </summary>
    public IReadOnlyList<PackageDescriptor> Packages => _packages;

    /// <summary>
    /// The published packages with the size already derived for the interface (AD-7).
    /// </summary>
    public IReadOnlyList<PackageRow> Rows =>
        _packages
            .Select(package => new PackageRow(
                package.PackageId,
                package.GameId,
                package.Version,
                SizeFormatter.FormatValue(package.SizeBytes),
                SizeFormatter.Unit,
                package.Sha256))
            .ToArray();

    /// <summary>
    /// The number of packages that are published.
    /// </summary>
    public int Count => _packages.Count;

    /// <summary>
    /// Replaces the published packages with the ones that the section shows.
    /// </summary>
    /// <param name="packages">Packages that are published.</param>
    public void Replace(IEnumerable<PackageDescriptor> packages)
    {
        ArgumentNullException.ThrowIfNull(packages);

        _packages.Clear();
        _packages.AddRange(packages);
    }
}

/// <summary>
/// A published package as the list of the section shows it (AC1).
///
/// <para>
/// The row carries the values that the list shows, and the size already derived: the
/// numeric value in decimal gigabytes and the unit apart, so the view can put each one in
/// its own token (AD-7, AC4).
/// </para>
/// </summary>
/// <param name="PackageId">Opaque UUID v7 identifier of the package.</param>
/// <param name="GameId">Opaque UUID v7 identifier of the videogame.</param>
/// <param name="Version">Version published in the catalog, as an opaque string.</param>
/// <param name="SizeValue">Numeric value of the size, in decimal gigabytes.</param>
/// <param name="SizeUnit">Unit of the size.</param>
/// <param name="Sha256">Hexadecimal sha256 of the package bytes.</param>
public sealed record PackageRow(
    string PackageId,
    string GameId,
    string Version,
    string SizeValue,
    string SizeUnit,
    string Sha256);
