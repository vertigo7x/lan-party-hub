using LANPartyHub.Client.Application.GameLauncher;
using LANPartyHub.Client.Presentation.GameLauncher;
using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Client.Presentation.Library;

/// <summary>
/// View model of the Biblioteca of the player (FR-9, AC1, AC2, AC3, AC8, UX-DR-17,
/// UX-DR-18, UX-DR-28).
///
/// <para>
/// La Biblioteca lista las instalaciones que el cliente registró y les aplica la MISMA
/// tabla de acciones que la ficha, así que ninguna de las dos superficies abre un camino
/// de lanzamiento distinto (AC1, AC2, UX-DR-17, UX-DR-19).
/// </para>
/// <para>
/// El estado vacío es «No tienes juegos instalados todavía.» y ofrece «Ver catálogo»,
/// así que ningún estado vacío queda SIN salida accionable (AC8, UX-DR-28).
/// </para>
/// </summary>
public sealed class LibrarySurfaceViewModel
{
    /// <summary>
    /// Text that the Biblioteca shows when the client has NO installation (AC8,
    /// UX-DR-28).
    /// </summary>
    public const string EmptyLibraryText = "No tienes juegos instalados todavía.";

    /// <summary>
    /// Text of the action that the empty Biblioteca offers (AC8, UX-DR-28).
    /// </summary>
    public const string ViewCatalogActionText = "Ver catálogo";

    private readonly List<LibraryEntryViewModel> _entries = [];

    /// <summary>
    /// Creates the view model of the Biblioteca.
    /// </summary>
    public LibrarySurfaceViewModel()
    {
    }

    /// <summary>Raised when the Biblioteca has to be rendered again.</summary>
    public event Action? Changed;

    /// <summary>
    /// The installed videogames, con sus acciones y su motivo VISIBLE (AC1, AC2).
    /// </summary>
    public IReadOnlyList<LibraryEntryViewModel> Entries => _entries;

    /// <summary>
    /// Whether the client has NO installation at all (AC8, UX-DR-28).
    /// </summary>
    public bool IsEmpty => _entries.Count == 0;

    /// <summary>
    /// Text that the Biblioteca shows when the client has NO installation (AC8,
    /// UX-DR-28).
    /// </summary>
    public string EmptyMessage => EmptyLibraryText;

    /// <summary>
    /// Text of the action that the empty Biblioteca offers (AC8, UX-DR-28).
    /// </summary>
    public string EmptyActionText => ViewCatalogActionText;

    /// <summary>
    /// Replaces the installations that the client registered, con la decisión sobre su
    /// jugabilidad (AC1, AC2, AC8).
    ///
    /// <para>
    /// La lectura es LOCAL: las instalaciones pertenecen al cliente y la Biblioteca nunca
    /// va al servidor por ellas (AD-5, AD-22).
    /// </para>
    /// </summary>
    /// <param name="installations">
    /// Installations that the client registered, with the decision about their
    /// playability.
    /// </param>
    public void ApplyInstallations(IReadOnlyList<InstalledGamePlayability> installations)
    {
        _entries.Clear();

        foreach (var installation in installations ?? [])
        {
            _entries.Add(new LibraryEntryViewModel(installation));
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// Notifies that the Biblioteca has to be rendered again.
    /// </summary>
    public void Refresh() => Changed?.Invoke();

    /// <summary>
    /// Applies the decision of a REPAIR IN PROGRESS a la fila del juego que se repara,
    /// así que la acción «Jugar» queda bloqueada con el motivo VISIBLE (AC2, UX-DR-17,
    /// UX-DR-18).
    ///
    /// <para>
    /// La fila se REPONE porque el estado de la reparación vive FUERA de la proyección:
    /// la proyección se REderiva al terminar, y mientras tanto la fila dice que se está
    /// reparando (AC2, AC3).
    /// </para>
    /// </summary>
    /// <param name="gameId">Identifier of the videogame that is being repaired.</param>
    public void ApplyRepairInProgress(string gameId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);

        foreach (var entry in _entries)
        {
            if (!string.Equals(entry.GameId, gameId, StringComparison.Ordinal))
            {
                continue;
            }

            entry.ApplyDecision(PlayabilityDecision.RepairInProgress());
        }

        Changed?.Invoke();
    }
}

/// <summary>
/// One installed videogame of the Biblioteca, con su tabla de acciones (AC1, AC2, AC8).
///
/// <para>
/// La fila NO lleva la ruta de la instalación: la ruta es local y nunca sale del cliente
/// (AD-22).
/// </para>
/// </summary>
public sealed class LibraryEntryViewModel
{
    /// <summary>
    /// Creates the row of an installed videogame of the Biblioteca.
    /// </summary>
    /// <param name="installation">Installation that the client registered.</param>
    public LibraryEntryViewModel(InstalledGamePlayability installation)
    {
        ArgumentNullException.ThrowIfNull(installation);

        GameId = installation.GameId;

        InstalledVersion = installation.InstalledVersion;

        ValidationState = installation.ValidationState;

        PublishedVersion = installation.PublishedVersion;

        IsOutdated = installation.Decision.IsOutdated;

        Actions = new GamePlayActionsViewModel(installation.GameId, installation.Decision);
    }

    /// <summary>Identifier of the videogame that is installed.</summary>
    public string GameId { get; }

    /// <summary>Version of the videogame that is installed.</summary>
    public string InstalledVersion { get; }

    /// <summary>State of the installation.</summary>
    public InstallationState ValidationState { get; }

    /// <summary>
    /// Version that the catalog publishes for the videogame, or <c>null</c> when the client
    /// does not know it (AC1, AC3).
    /// </summary>
    public string? PublishedVersion { get; }

    /// <summary>
    /// Whether the installation is VALID but its version is OLDER than the version
    /// published in the catalog (AC1, AC4).
    /// </summary>
    public bool IsOutdated { get; }

    /// <summary>
    /// The table of the actions of the videogame, con el motivo VISIBLE del bloqueo
    /// (AC1, AC2, UX-DR-17).
    /// </summary>
    public GamePlayActionsViewModel Actions { get; }

    /// <summary>
    /// Replaces the decision about the playability of the videogame, así que la fila
    /// muestra el motivo VISIBLE que le corresponde (AC2, UX-DR-17).
    /// </summary>
    /// <param name="decision">The new decision about the playability of the videogame.</param>
    public void ApplyDecision(PlayabilityDecision decision) => Actions.Apply(decision);

    /// <summary>
    /// Text of the state of the installation, taken from the single table of microcopy
    /// (AC4, AR-8).
    /// </summary>
    public string ValidationStateText => StateDisplayMap.GetText(ValidationState);

    /// <summary>
    /// Text of the badge of the view state of the videogame, taken from the single table
    /// of microcopy (AC1, AC4, AR-8).
    ///
    /// <para>
    /// Una instalación desactualizada lleva el texto «instalado con versión
    /// desactualizada», y CUALQUIER otra instalación lleva el texto de su estado de
    /// instalación (AC1, AD-10).
    /// </para>
    /// </summary>
    public string ViewStateText => IsOutdated
        ? StateDisplayMap.GetText(CatalogViewState.InstalledOutdatedVersion)
        : ValidationStateText;

    /// <summary>
    /// Text of the line of the installed version, composed con la tabla de microcopia
    /// (AC3, UX-DR-6, AR-8).
    ///
    /// <para>
    /// La insignia dice «Instalado · v1.2»: el texto del estado sale de
    /// <see cref="StateDisplayMap"/> y la versión se muestra en su forma EXACTA
    /// (UX-DR-6).
    /// </para>
    /// </summary>
    public string InstalledVersionText =>
        StateDisplayMap.GetInstalledBadge(ValidationState, InstalledVersion);

    /// <summary>
    /// Text of the line of the PUBLISHED version, or <c>null</c> when the client does not
    /// know it (AC1, AC3).
    ///
    /// <para>
    /// La línea dice «Disponible · v1.3» con la versión EXACTA que publica el catálogo,
    /// así que el jugador compara la que tiene con la que hay (AC3, UX-DR-21).
    /// </para>
    /// </summary>
    public string? AvailableVersionText => PublishedVersion is null
        ? null
        : $"{AvailableVersionLabel}{StateDisplayMap.InstalledVersionSeparator}{PublishedVersion}";

    /// <summary>
    /// Label of the line of the PUBLISHED version (AC3, UX-DR-21).
    /// </summary>
    public const string AvailableVersionLabel = "Disponible";
}
