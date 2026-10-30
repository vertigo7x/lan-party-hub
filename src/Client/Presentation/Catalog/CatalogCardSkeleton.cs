using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace LANPartyHub.Client.Presentation.Catalog;

/// <summary>
/// Skeleton of a card of the catalog (AC6, UX-DR-28).
///
/// <para>
/// The skeleton has the REAL proportion of the card of the grid, so the layout does NOT
/// jump when the cards arrive, and there is NO «cargando» spinner (AC6, UX-DR-28).
/// </para>
/// <para>
/// The surface of the skeleton is the sunken one of the design, and the content of the card
/// is NEVER truncated to more than one line of the name (DESIGN).
/// </para>
/// </summary>
public sealed class CatalogCardSkeleton : Border
{
    /// <summary>
    /// Proportion of the cover of a card: the design fixes the carátula in 3:2.
    /// </summary>
    public const double CoverRatio = 3d / 2d;

    /// <summary>
    /// Width of the cover of a card, in pixels.
    /// </summary>
    public const double CoverWidth = 168;

    /// <summary>
    /// Height of the cover of a card, in pixels. The cover keeps the proportion of 3:2
    /// that the design fixes para la carátula.
    /// </summary>
    public const double CoverHeight = CoverWidth / CoverRatio;

    /// <summary>Height of the bar of the name of a card, in pixels.</summary>
    public const double NameBarHeight = 16;

    /// <summary>Height of the bar of the metadata of a card, in pixels.</summary>
    public const double MetaBarHeight = 12;

    /// <summary>
    /// Creates the skeleton of a card of the catalog.
    /// </summary>
    /// <param name="width">Width of the card, in pixels.</param>
    public CatalogCardSkeleton(double width = CoverWidth)
    {
        Width = width;

        var coverWidth = width;

        var coverHeight = coverWidth / CoverRatio;

        Height = coverHeight + NameBarHeight + MetaBarHeight + 24;

        CornerRadius = new CornerRadius(12);
        Background = new SolidColorBrush(Color.FromArgb(0x14, 0x1A, 0x1F, 0x24));

        Child = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new Border
                {
                    Width = coverWidth,
                    Height = coverHeight,
                    CornerRadius = new CornerRadius(12),
                    Background = new SolidColorBrush(Color.FromArgb(0x0F, 0x1A, 0x1F, 0x24)),
                },
                new Border
                {
                    Width = coverWidth - 24,
                    Height = NameBarHeight,
                    CornerRadius = new CornerRadius(4),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Background = new SolidColorBrush(Color.FromArgb(0x1F, 0x1A, 0x1F, 0x24)),
                },
                new Border
                {
                    Width = (coverWidth - 24) / 2,
                    Height = MetaBarHeight,
                    CornerRadius = new CornerRadius(4),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Background = new SolidColorBrush(Color.FromArgb(0x14, 0x1A, 0x1F, 0x24)),
                },
            },
        };
    }
}
