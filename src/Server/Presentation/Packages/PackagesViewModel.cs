using LANPartyHub.Server.Application.Ports;
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
/// <para>
/// The row of the list carries the videogame, the version, the exact size in bytes, the
/// license, the restrictions of distribution and the state of the withdrawal, so the
/// organizer sees everything that the catalog demands before deciding (AC1, FR-1, NFR-8).
/// </para>
/// </summary>
public sealed class PackagesViewModel
{
    private readonly List<PublishedPackageRow> _packages = [];

    /// <summary>Creates the view model of the section of the packages.</summary>
    public PackagesViewModel()
    {
    }

    /// <summary>
    /// Creates the view model of the section of the packages with the packages that are
    /// already published.
    /// </summary>
    /// <param name="packages">Packages that are published.</param>
    public PackagesViewModel(IEnumerable<PublishedPackageRow> packages)
    {
        ArgumentNullException.ThrowIfNull(packages);

        _packages.AddRange(packages);
    }

    /// <summary>
    /// The published packages, as the list shows them.
    /// </summary>
    public IReadOnlyList<PublishedPackageRow> Packages => _packages;

    /// <summary>
    /// The published packages with the size already derived for the interface (AD-7).
    /// </summary>
    public IReadOnlyList<PackageRow> Rows =>
        _packages
            .Select(package => new PackageRow(
                package.PackageId,
                package.GameId,
                package.GameName,
                package.Version,
                package.SizeBytes,
                SizeFormatter.FormatValue(package.SizeBytes),
                SizeFormatter.Unit,
                package.License,
                package.DistributionRestrictions,
                package.WithdrawnAt is null
                    ? null
                    : StateDisplayMap.GetText(OperationOutcome.PackageWithdrawn)))
            .ToArray();

    /// <summary>
    /// The number of packages that are published.
    /// </summary>
    public int Count => _packages.Count;

    /// <summary>
    /// Replaces the published packages with the ones that the section shows.
    /// </summary>
    /// <param name="packages">Packages that are published.</param>
    public void Replace(IEnumerable<PublishedPackageRow> packages)
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
/// its own token (AD-7, AC4). The exact size in bytes travels too, because the bytes are
/// the value that the model holds and the gigabytes are only its presentation (AD-7).
/// </para>
/// <para>
/// The state of the withdrawal is the text of the single table of microcopy of the system
/// for the result <c>PackageWithdrawn</c>, and the view never writes a text of its own
/// (AD-10, AD-25, AD-33).
/// </para>
/// </summary>
/// <param name="PackageId">Opaque UUID v7 identifier of the package.</param>
/// <param name="GameId">Opaque UUID v7 identifier of the videogame.</param>
/// <param name="GameName">Human readable name of the videogame.</param>
/// <param name="Version">Version published in the catalog, as an opaque string.</param>
/// <param name="SizeBytes">Exact size of the package in bytes.</param>
/// <param name="SizeValue">Numeric value of the size, in decimal gigabytes.</param>
/// <param name="SizeUnit">Unit of the size.</param>
/// <param name="License">License of the videogame.</param>
/// <param name="DistributionRestrictions">Restrictions of distribution of the videogame.</param>
/// <param name="WithdrawalText">
/// Text of the state of the withdrawal, or <c>null</c> when the package is still in the
/// catalog.
/// </param>
public sealed record PackageRow(
    string PackageId,
    string GameId,
    string GameName,
    string Version,
    long SizeBytes,
    string SizeValue,
    string SizeUnit,
    string License,
    string DistributionRestrictions,
    string? WithdrawalText);
