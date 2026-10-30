using LANPartyHub.Client.Application.Catalog;
using LANPartyHub.Client.Presentation.Formatting;
using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Client.Presentation.Catalog;

/// <summary>
/// View model of the ficha del videojuego (AC1, AC2, AC3, AC4, FR-2, UX-DR-2, UX-DR-5,
/// UX-DR-10, UX-DR-27, AR-5, AR-8).
///
/// <para>
/// The ficha shows the EXACT values of the videogame: the version, the size, the number of
/// players, the operating system, the requirements and the license. The values come from
/// the snapshot that the client already synchronized and from the installations that the
/// client registered: the ficha NEVER goes back to the server (AD-5, AD-26).
/// </para>
/// <para>
/// The size is expressed in decimal gigabytes with EXACTLY one decimal and the Spanish
/// decimal separator: the numeric value goes in the token of sizes and the unit «GB» goes
/// in the token of metadata, and the version goes in the monospaced token (AC2, UX-DR-5,
/// UX-DR-2).
/// </para>
/// <para>
/// The badge of the view state is DERIVED by the Application layer and its text comes
/// EXCLUSIVELY from the single table of microcopy <see cref="StateDisplayMap"/>: the view
/// does not define a single text of state (AC3, AC4, AR-8, R-MVP-7).
/// </para>
/// <para>
/// The ficha does NOT show the path of the installation: the path is local and it never
/// leaves the client (AD-22).
/// </para>
/// </summary>
public sealed class GameDetailViewModel
{
    private readonly GameDescriptor _game;

    /// <summary>
    /// Creates the view model of the ficha of a videogame.
    /// </summary>
    /// <param name="game">Videogame of the snapshot that the client synchronized.</param>
    /// <param name="viewState">Derived view state of the videogame.</param>
    /// <param name="installedVersion">
    /// Version that the client has installed, or <c>null</c> when it has no installation.
    /// </param>
    public GameDetailViewModel(
        GameDescriptor game,
        CatalogViewState viewState,
        string? installedVersion = null)
    {
        ArgumentNullException.ThrowIfNull(game);

        _game = game;

        ViewState = viewState;

        // El texto de la insignia sale de la tabla de microcopy y de NINGÚN otro sitio
        // (AC4, AR-8, R-MVP-7).
        ViewStateText = StateDisplayMap.GetText(viewState);

        InstalledVersion = installedVersion;
    }

    /// <summary>Raised when the ficha has to be rendered again.</summary>
    public event Action? Changed;

    /// <summary>Identifier of the videogame.</summary>
    public string GameId => _game.GameId;

    /// <summary>Name of the videogame.</summary>
    public string Name => _game.Name;

    /// <summary>Description of the videogame.</summary>
    public string Description => _game.Description;

    /// <summary>Version published in the catalog, in its EXACT value (AC2, UX-DR-2).</summary>
    public string Version => _game.Version;

    /// <summary>
    /// Numeric value of the size, in decimal gigabytes, with exactly one decimal and the
    /// Spanish decimal separator (AC2, UX-DR-5).
    /// </summary>
    public string SizeValue => SizeFormatter.FormatValue(_game.SizeBytes);

    /// <summary>Unit of the size, which goes in the token of metadata (AC2, UX-DR-5).</summary>
    public string SizeUnit => SizeFormatter.Unit;

    /// <summary>Minimum number of players, in its EXACT value (AC2, UX-DR-2).</summary>
    public int MinPlayers => _game.MinPlayers;

    /// <summary>Maximum number of players, in its EXACT value (AC2, UX-DR-2).</summary>
    public int MaxPlayers => _game.MaxPlayers;

    /// <summary>Operating system the game runs on, in its EXACT value (AC2, UX-DR-2).</summary>
    public string OperatingSystem => _game.OperatingSystem;

    /// <summary>Requirements of the game, in their EXACT values (AC2).</summary>
    public IReadOnlyList<string> Requirements => _game.Dependencies;

    /// <summary>License of the game, in its EXACT value (AC2).</summary>
    public string License => _game.License;

    /// <summary>Derived view state of the videogame (AC3).</summary>
    public CatalogViewState ViewState { get; }

    /// <summary>
    /// Text of the badge of the view state, taken from the single table of microcopy
    /// (AC4, AR-8).
    /// </summary>
    public string ViewStateText { get; }

    /// <summary>
    /// Version that the client has installed, or <c>null</c> when it has no installation.
    /// </summary>
    public string? InstalledVersion { get; }

    /// <summary>
    /// Text of the line of the installed version, or <c>null</c> when the client has no
    /// installation.
    ///
    /// <para>
    /// The word of the line es el de la tabla de microcopy
    /// (<see cref="InstallationState.Installed"/>) y NUNCA uno escrito en la vista
    /// (AC4, AR-8).
    /// </para>
    /// </summary>
    public string? InstalledVersionText =>
        InstalledVersion is null
            ? null
            : $"{StateDisplayMap.GetText(InstallationState.Installed)}: {InstalledVersion}";

    /// <summary>
    /// Text of the requirements of the game, ready to show (AC2).
    /// </summary>
    public string RequirementsText =>
        Requirements.Count == 0 ? string.Empty : string.Join(" · ", Requirements);

    /// <summary>
    /// Text of the number of players of the game, ready to show (AC2, UX-DR-2).
    /// </summary>
    public string PlayersText => $"{MinPlayers} - {MaxPlayers}";

    /// <summary>
    /// Tells whether the client has an installation of the game whose version is older
    /// than the published one (AC3).
    /// </summary>
    public bool HasOutdatedInstallation =>
        ViewState is CatalogViewState.InstalledOutdatedVersion;

    /// <summary>
    /// Notifies that the ficha has to be rendered again.
    /// </summary>
    public void Refresh() => Changed?.Invoke();
}
