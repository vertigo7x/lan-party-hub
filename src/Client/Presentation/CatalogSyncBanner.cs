using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LANPartyHub.Client.Application.CatalogSync;

namespace LANPartyHub.Client.Presentation;

/// <summary>
/// Notice of the pending synchronization of the catalog (AC3, UX-DR-32).
///
/// <para>
/// The notice is a DISCREET bar that lives above the content of the window and it does NOT
/// block the navigation: it is not a modal, it does not disable the surfaces and it does
/// not take the focus away from what the player is doing (AC3, UX-DR-32).
/// </para>
/// <para>
/// The text is the one that the experience of the event fixes: «Catálogo desactualizado ·
/// sincronizado hace 12 min», and the action is «Sincronizar ahora» (AC3, UX-DR-32).
/// </para>
/// </summary>
public sealed class CatalogSyncBanner : StackPanel
{
    /// <summary>
    /// Text of the notice of the pending synchronization (AC3, UX-DR-32).
    /// </summary>
    public const string PendingTitle = "Catálogo desactualizado";

    /// <summary>
    /// Text of the action that synchronizes the catalog now (AC3, UX-DR-32).
    /// </summary>
    public const string SynchronizeNowAction = "Sincronizar ahora";

    private readonly TextBlock _message;
    private readonly Button _synchronize;

    /// <summary>Creates the notice of the pending synchronization.</summary>
    /// <param name="onSynchronizeNow">
    /// Action that the player triggers with «Sincronizar ahora».
    /// </param>
    public CatalogSyncBanner(Func<Task> onSynchronizeNow)
    {
        ArgumentNullException.ThrowIfNull(onSynchronizeNow);

        Orientation = Orientation.Horizontal;
        Spacing = 12;
        Margin = new Thickness(16, 16, 16, 0);

        _message = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };

        _synchronize = new Button
        {
            Content = SynchronizeNowAction,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _synchronize.Click += async (_, _) =>
        {
            await onSynchronizeNow().ConfigureAwait(true);
        };

        Children.Add(_message);
        Children.Add(_synchronize);
    }

    /// <summary>
    /// Shows the notice of the pending synchronization with the age of the last catalog
    /// that the client applied (AC3, UX-DR-32).
    /// </summary>
    /// <param name="state">State of the synchronization of the catalog.</param>
    public void Show(CatalogSyncState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        _message.Text = BuildMessage(state);

        IsVisible = true;
    }

    /// <summary>
    /// Hides the notice, because the catalog is up to date.
    /// </summary>
    public void Hide() => IsVisible = false;

    /// <summary>
    /// Builds the text of the notice: «Catálogo desactualizado · sincronizado hace 12
    /// min» (AC3, UX-DR-32).
    /// </summary>
    /// <param name="state">State of the synchronization of the catalog.</param>
    /// <returns>The text of the notice.</returns>
    public static string BuildMessage(CatalogSyncState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var age = FormatAge(state.LastSynchronizedAt);

        return age is null
            ? PendingTitle
            : $"{PendingTitle} · sincronizado hace {age}";
    }

    /// <summary>
    /// Formats the age of the last catalog that the client applied, in the unit that the
    /// player reads best: the minutes when it fits in them and the hours when it does not
    /// (AC3, UX-DR-32).
    /// </summary>
    /// <param name="lastSynchronizedAt">Instant of the last successful synchronization.</param>
    /// <param name="now">Instant that the age is measured against.</param>
    /// <returns>The age, or <c>null</c> when the client never synchronized.</returns>
    public static string? FormatAge(DateTimeOffset? lastSynchronizedAt, DateTimeOffset? now = null)
    {
        if (lastSynchronizedAt is null)
        {
            return null;
        }

        var current = now ?? DateTimeOffset.UtcNow;
        var elapsed = current - lastSynchronizedAt.Value;

        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        if (elapsed.TotalMinutes < 1)
        {
            return "menos de 1 min";
        }

        if (elapsed.TotalHours < 1)
        {
            var minutes = (int)Math.Floor(elapsed.TotalMinutes);

            return $"{minutes} min";
        }

        var hours = (int)Math.Floor(elapsed.TotalHours);

        return $"{hours} h";
    }
}
