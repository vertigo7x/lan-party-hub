using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Client.Presentation.Catalog;

/// <summary>
/// Grid of the cards of the catalog (AC1, AC5, AC6, UX-DR-27, UX-DR-28).
///
/// <para>
/// The grid is FLUID: the width of the card is RECALCULATED with the window, the columns
/// never stretch to fill the row and the last row stays aligned to the left. It has a
/// minimum of 3 columns at 1280 px and up to 6; when the width does NOT allow 3 columns
/// the grid goes to 2, and below 2 it goes to a list of one column (DESIGN).
/// </para>
/// <para>
/// The grid pages by FIXED scrolls with an EXPLICIT pagination and it NEVER scrolls
/// infinitely, so the player can come back to the same position (AC5, UX-DR-27).
/// </para>
/// <para>
/// While there is no card to show the grid shows the SKELETONS of the cards with the real
/// proportion of the grid, and never a «cargando» spinner (AC6, UX-DR-28).
/// </para>
/// </summary>
public sealed class CatalogGrid : UserControl
{
    /// <summary>Width of the window at which the grid reaches 3 columns.</summary>
    public const double ThreeColumnsWidth = 1280;

    /// <summary>Width of the window at which the grid reaches 6 columns.</summary>
    public const double SixColumnsWidth = 1920;

    /// <summary>Width of the window below which the grid is a list of one column.</summary>
    public const double SingleColumnWidth = 720;

    /// <summary>Number of skeletons that the grid shows while it loads.</summary>
    public const int SkeletonCount = 6;

    /// <summary>Space between two cards of the grid, in pixels.</summary>
    public const double CardSpacing = 16;

    /// <summary>
    /// Smallest width that a card can have, in pixels. A card narrower than this would
    /// truncate the name of the game too much (DESIGN).
    /// </summary>
    public const double MinimumCardWidth = 140;

    private readonly WrapPanel _cards;
    private readonly TextBlock _empty;

    private IReadOnlyList<GameDescriptor> _games = [];
    private bool _showsSkeletons;
    private string? _emptyMessage;
    private bool _rebuilding;

    /// <summary>
    /// Creates the grid of the cards of the catalog.
    /// </summary>
    public CatalogGrid()
    {
        _cards = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            ItemSpacing = CardSpacing,
            LineSpacing = CardSpacing,
        };

        _empty = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
        };

        Content = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new ScrollViewer
                {
                    Content = _cards,
                    HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                },
                _empty,
            },
        };

        // La rejilla RECALCULA sus columnas cuando la ventana cambia de tamaño: el ancho
        // de la tarjeta se ajusta y las columnas nunca se estiran para llenar la fila
        // (DESIGN).
        SizeChanged += (_, _) =>
        {
            // La reconstrucción cambia el tamaño de las tarjetas, y el tamaño de las
            // tarjetas vuelve a cambiar el tamaño de la rejilla: sin esta guarda, el
            // desenho entraría en un bucle deInvalidación.
            if (_rebuilding)
            {
                return;
            }

            _rebuilding = true;

            try
            {
                Rebuild();
            }
            finally
            {
                _rebuilding = false;
            }
        };
    }

    /// <summary>
    /// Fills the grid with the videogames of the current page (AC5, AC6).
    /// </summary>
    /// <param name="games">Videogames of the current page.</param>
    /// <param name="showsSkeletons">
    /// Whether the grid has to show the skeletons of the cards instead of the videogames.
    /// </param>
    /// <param name="emptyMessage">
    /// Message of the empty state, or <c>null</c> when the grid has videogames to show.
    /// </param>
    public void Render(
        IReadOnlyList<GameDescriptor> games,
        bool showsSkeletons,
        string? emptyMessage)
    {
        _games = games;
        _showsSkeletons = showsSkeletons;
        _emptyMessage = emptyMessage;

        Rebuild();
    }

    /// <summary>
    /// Counts the columns that the grid has for the width that it has now (DESIGN).
    ///
    /// <para>
    /// The grid has a minimum of 3 columns at 1280 px and up to 6; when the width does NOT
    /// allow 3 columns the grid goes to 2, and below 2 it goes to a list of one column.
    /// </para>
    /// </summary>
    /// <param name="width">Width that the grid has now, in pixels.</param>
    /// <returns>The number of columns of the grid.</returns>
    public static int ColumnsFor(double width)
    {
        if (width < SingleColumnWidth)
        {
            return 1;
        }

        if (width < ThreeColumnsWidth)
        {
            return 2;
        }

        if (width < SixColumnsWidth)
        {
            // De 3 columnas a 1280 px a 6 columnas a 1920 px: las columnas intermedias
            // aparecen de forma proporcional, de modo que la rejilla crece con la ventana
            // sin estirar las tarjetas y sin un salto brusco (DESIGN).
            var columns = 3 + (int)Math.Floor(
                (width - ThreeColumnsWidth) / ((SixColumnsWidth - ThreeColumnsWidth) / 3d));

            return Math.Clamp(columns, 3, 5);
        }

        return 6;
    }

    /// <summary>
    /// Rebuilds the cards of the grid with the width that the grid has now (DESIGN).
    /// </summary>
    private void Rebuild()
    {
        _cards.Children.Clear();

        var width = Math.Max(Bounds.Width, 0);

        var columns = ColumnsFor(width);

        var available = width > 0 ? width : (double)CatalogCardSkeleton.CoverWidth * columns;

        var cardWidth = Math.Max(
            MinimumCardWidth,
            (available - (CardSpacing * (columns - 1))) / columns);

        if (_showsSkeletons)
        {
            for (var index = 0; index < SkeletonCount; index++)
            {
                _cards.Children.Add(new CatalogCardSkeleton(cardWidth));
            }
        }
        else
        {
            foreach (var game in _games)
            {
                _cards.Children.Add(BuildCard(game, cardWidth));
            }
        }

        _empty.Text = _emptyMessage ?? string.Empty;
        _empty.IsVisible = _emptyMessage is not null;
    }

    /// <summary>
    /// Builds the card of a videogame (DESIGN).
    ///
    /// <para>
    /// The name of the game is truncated to ONE line, because a name that wraps to three
    /// lines breaks the grid (DESIGN). The cover keeps the proportion of the skeleton, so
    /// the grid does NOT change its shape when the cards arrive (AC6).
    /// </para>
    /// </summary>
    /// <param name="game">Videogame of the card.</param>
    /// <param name="width">Width of the card, in pixels.</param>
    /// <returns>The control of the card.</returns>
    private static Border BuildCard(GameDescriptor game, double width)
    {
        var coverWidth = width;

        var coverHeight = coverWidth / CatalogCardSkeleton.CoverRatio;

        var name = new TextBlock
        {
            Text = game.Name,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
            FontWeight = FontWeight.SemiBold,
        };

        var meta = new TextBlock
        {
            Text = game.Categories.Count > 0
                ? string.Join(" · ", game.Categories)
                : game.Version,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
            FontSize = 12,
            Opacity = 0.7,
        };

        var content = new StackPanel
        {
            Spacing = 8,
            Width = coverWidth,
            Children =
            {
                new Border
                {
                    Width = coverWidth,
                    Height = coverHeight,
                    CornerRadius = new CornerRadius(12),
                    Background = new SolidColorBrush(Color.FromArgb(0x0F, 0x1A, 0x1F, 0x24)),
                    Child = new TextBlock
                    {
                        Text = game.Name,
                        TextWrapping = TextWrapping.Wrap,
                        TextAlignment = TextAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(12),
                    },
                },
                name,
                meta,
            },
        };

        return new Border
        {
            Width = coverWidth,
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(0, 0, 0, 8),
            Child = content,
        };
    }
}
