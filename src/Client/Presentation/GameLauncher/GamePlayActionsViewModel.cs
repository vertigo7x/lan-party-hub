using LANPartyHub.Client.Application.GameLauncher;
using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Client.Presentation.GameLauncher;

/// <summary>
/// The table of the actions of a videogame, with the VISIBLE reason of the block
/// (FR-9, AC1, AC2, AC3, AC5, AC6, UX-DR-17, UX-DR-18, UX-DR-26, UX-DR-38).
///
/// <para>
/// La tabla es la MISMA en la Biblioteca y en la ficha, así que ninguna de las dos
/// superficies abre un camino de lanzamiento distinto (AC1, AC2, UX-DR-17, UX-DR-19).
/// </para>
/// <para>
/// La acción «Jugar» bloqueada NO se deshabilita: con <c>IsEnabled=false</c> Avalonia la
/// saca del recorrido de <c>Tab</c>, y el control bloqueado TIENE que recibir foco con
/// su indicador de foco visible (AC5, UX-DR-26). Lo que REHUSA actuar es el comando:
/// <c>Enter</c> sobre «Jugar» bloqueada no produce ningún cambio de estado, no inicia el
/// juego y no dispara descarga, instalación ni reparación implícita (AC6).
/// </para>
/// <para>
/// El motivo del bloqueo aparece JUNTO a la etiqueta de «Jugar», y NO en un
/// <em>tooltip</em>, ni sólo en el log de diagnóstico, ni depende de <em>hover</em> o foco
/// (AC3, UX-DR-18).
/// </para>
/// </summary>
public sealed class GamePlayActionsViewModel
{
    /// <summary>
    /// Text of the action that launches the videogame (AC1).
    ///
    /// <para>
    /// La acción se llama «Jugar» y su TEXTO no es un texto de estado: es una acción, y
    /// por eso vive en el cliente y no en la tabla de microcopy de <c>Shared</c>
    /// (AD-18, AR-3).
    /// </para>
    /// </summary>
    public const string PlayActionText = "Jugar";

    /// <summary>
    /// Text of the PRIMARY action that repairs an invalid installation (AC1, UX-DR-17).
    /// </summary>
    public const string RepairActionText = "Reparar";

    /// <summary>
    /// Text of the PRIMARY action that downloads the package again, because the
    /// validated one is not available (AC2, UX-DR-17).
    /// </summary>
    public const string RedownloadActionText = "Descargar de nuevo";

    /// <summary>
    /// Text of the SECONDARY action that opens the registered error (AC1, AC2,
    /// UX-DR-17).
    /// </summary>
    public const string DiagnoseActionText = "Diagnosticar";

    /// <summary>
    /// Text of the PRIMARY action that replaces an outdated installation with the version
    /// published in the catalog (AC1, AC4, FR-11, UX-DR-32).
    ///
    /// <para>
    /// La acción se llama «Actualizar» y su TEXTO no es un texto de estado: es una acción,
    /// y por eso vive en el cliente y no en la tabla de microcopy de <c>Shared</c>
    /// (AD-18, AR-3).
    /// </para>
    /// </summary>
    public const string UpdateActionText = "Actualizar";

    /// <summary>
    /// Text that NAMES the CONSEQUENCE of the repair, así que el jugador sabe lo que va a
    /// pasar ANTES de que ocurra (AC1, UX-DR-20).
    ///
    /// <para>
    /// La consecuencia se NOMBRA y no se CONFIRMA: la reparación no pide un modal porque
    /// no destruye trabajo recuperable —reutiliza el paquete ya validado— y una
    /// confirmación sin consecuencia sería ruido (UX-DR-20, UX-DR-22).
    /// </para>
    /// </summary>
    public const string RepairConsequence =
        "Se reparará la instalación; la descarga no se repite";

    /// <summary>
    /// Text that NAMES the CONSEQUENCE of the update, así que el jugador sabe lo que va a
    /// pasar ANTES de que ocurra (AC1, FR-11).
    ///
    /// <para>
    /// La consecuencia se NOMBRA y no se CONFIRMA: la actualización descarga el paquete
    /// COMPLETO de la versión publicada y la instala encima de la anterior, así que el
    /// aviso dice exactamente qué va a pasar (R-MVP-4, UX-DR-22).
    /// </para>
    /// </summary>
    public const string UpdateConsequence =
        "Se descargará la versión completa y sustituirá la versión instalada";

    private PlayabilityDecision _decision;

    /// <summary>
    /// Creates the table of the actions of a videogame.
    /// </summary>
    /// <param name="gameId">Identifier of the videogame.</param>
    /// <param name="decision">Decision about the playability of the installation.</param>
    public GamePlayActionsViewModel(string gameId, PlayabilityDecision decision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);

        GameId = gameId;

        _decision = decision ?? PlayabilityDecision.Playable();
    }

    /// <summary>Raised when the table of the actions has to be rendered again.</summary>
    public event Action? Changed;

    /// <summary>Identifier of the videogame.</summary>
    public string GameId { get; }

    /// <summary>
    /// The decision that the table paints (AC1).
    /// </summary>
    public PlayabilityDecision Decision => _decision;

    /// <summary>
    /// Whether the action «Jugar» is BLOCKED (AC1).
    /// </summary>
    public bool IsBlocked => _decision.IsBlocked;

    /// <summary>
    /// Whether the action «Jugar» is ENABLED, que es lo contrario de estar bloqueada
    /// (AC1).
    /// </summary>
    public bool CanPlay => _decision.CanPlay;

    /// <summary>
    /// Text of the label of the action «Jugar» (AC1).
    /// </summary>
    public string PlayLabel => PlayActionText;

    /// <summary>
    /// The VISIBLE reason of the block, next to the label of the action «Jugar», or
    /// <c>null</c> when the action is enabled (AC1, AC3, UX-DR-18).
    /// </summary>
    public string? ReasonText => _decision.ReasonText;

    /// <summary>
    /// Stable code of the catalog that carries the visible reason, or <c>null</c> when
    /// the action is enabled (AD-15, AD-25).
    /// </summary>
    public string? ReasonCode => _decision.ReasonCode;

    /// <summary>
    /// Whether the reason of the block is VISIBLE next to the label (AC3, UX-DR-18).
    /// </summary>
    public bool ShowsReason => _decision.ReasonText is not null;

    /// <summary>
    /// Whether the REPAIR of the installation is IN PROGRESS (AC2, UX-DR-17).
    /// </summary>
    public bool IsRepairInProgress =>
        _decision.ReasonCode == ErrorCatalog.InstallationRepairInProgressCode;

    /// <summary>
    /// The text that NAMES the CONSEQUENCE of the repair, que se muestra JUNTO a la acción
    /// «Reparar» (AC1, UX-DR-20).
    ///
    /// <para>
    /// La consecuencia se nombra, NO se confirma: no hay modal, porque la reparación no
    /// destruye trabajo recuperable y reutiliza el paquete ya validado (UX-DR-20).
    /// </para>
    /// </summary>
    public string? RepairConsequenceText =>
        IsPrimaryRepair ? RepairConsequence : null;

    /// <summary>
    /// The text that NAMES the CONSEQUENCE of the update, que se muestra JUNTO a la acción
    /// «Actualizar» (AC1, FR-11).
    ///
    /// <para>
    /// La consecuencia se nombra, NO se confirma: el aviso dice que se descarga la versión
    /// COMPLETA y que sustituye a la instalada, así que el jugador sabe lo que va a pasar
    /// antes de que ocurra (R-MVP-4, UX-DR-22).
    /// </para>
    /// </summary>
    public string? UpdateConsequenceText =>
        IsPrimaryUpdate ? UpdateConsequence : null;

    /// <summary>
    /// The CONSEQUENCE of the PRIMARY action, que nombra lo que va a pasar tanto si la
    /// primaria es «Reparar» como si es «Actualizar» (AC1, FR-11, UX-DR-20, UX-DR-22).
    /// </summary>
    public string? ConsequenceText =>
        RepairConsequenceText ?? UpdateConsequenceText;

    /// <summary>
    /// The three parts of the error of the block, or <c>null</c> when the action is
    /// enabled (AD-15, AD-25, UX-DR-36).
    /// </summary>
    public ApiError? ReasonError => _decision.ReasonError;

    /// <summary>
    /// Text of the PRIMARY action of the surface (AC1, AC2, UX-DR-17).
    /// </summary>
    public string PrimaryActionText => _decision.PrimaryAction switch
    {
        PlayabilityAction.Repair => RepairActionText,
        PlayabilityAction.Redownload => RedownloadActionText,
        PlayabilityAction.Update => UpdateActionText,
        _ => PlayActionText,
    };

    /// <summary>
    /// The PRIMARY action of the surface (AC1, AC2, UX-DR-17).
    /// </summary>
    public PlayabilityAction PrimaryAction => _decision.PrimaryAction;

    /// <summary>
    /// Text of the SECONDARY action of the surface, or <c>null</c> when there is none
    /// (AC1, AC2, UX-DR-17).
    /// </summary>
    public string? SecondaryActionText => _decision.SecondaryAction switch
    {
        PlayabilityAction.Diagnose => DiagnoseActionText,
        _ => null,
    };

    /// <summary>
    /// The SECONDARY action of the surface, or <c>null</c> when there is none (AC1, AC2,
    /// UX-DR-17).
    /// </summary>
    public PlayabilityAction? SecondaryAction => _decision.SecondaryAction;

    /// <summary>
    /// Whether the PRIMARY action is «Reparar» (AC1, UX-DR-17).
    /// </summary>
    public bool IsPrimaryRepair => _decision.PrimaryAction == PlayabilityAction.Repair;

    /// <summary>
    /// Whether the PRIMARY action is «Descargar de nuevo» (AC2, UX-DR-17).
    /// </summary>
    public bool IsPrimaryRedownload => _decision.PrimaryAction == PlayabilityAction.Redownload;

    /// <summary>
    /// Whether the PRIMARY action is «Actualizar», que aparece SOLO con una instalación
    /// VÁLIDA cuya versión es anterior a la publicada (AC1, AC4, UX-DR-32).
    /// </summary>
    public bool IsPrimaryUpdate => _decision.PrimaryAction == PlayabilityAction.Update;

    /// <summary>
    /// Whether the installation is VALID but OUTDATED, así que «Actualizar» es la primaria
    /// y «Jugar» sigue disponible (AC1, AC4).
    /// </summary>
    public bool IsOutdated => _decision.IsOutdated;

    /// <summary>
    /// The version published in the catalog for the videogame, or <c>null</c> when the
    /// client does not know it (AC1, AC3).
    /// </summary>
    public string? PublishedVersion => _decision.PublishedVersion;

    /// <summary>
    /// The ACCESSIBLE NAME of the action «Jugar», que el lector de pantalla anuncia con
    /// el nombre de la acción, su estado bloqueado y el motivo visible (AC5, UX-DR-26,
    /// UX-DR-38).
    ///
    /// <para>
    /// Con la acción habilitada el nombre es el de la acción; con la acción bloqueada el
    /// nombre lleva el estado y el motivo, así que el lector de pantalla no depende del
    /// color de la insignia (AC5, UX-DR-38).
    /// </para>
    /// </summary>
    public string PlayAccessibleName =>
        _decision.IsBlocked
            ? $"{PlayActionText} (bloqueado): {_decision.ReasonText}"
            : PlayActionText;

    /// <summary>
    /// The ACCESSIBLE NAME of the PRIMARY action, con su estado cuando está bloqueada
    /// (AC5, UX-DR-38).
    /// </summary>
    public string PrimaryAccessibleName =>
        _decision.PrimaryAction == PlayabilityAction.Play
            ? PlayActionText
            : PrimaryActionText;

    /// <summary>
    /// Replaces the decision that the table paints (AC1).
    ///
    /// <para>
    /// La decisión se REderiva al pintar, así que una nueva certificación cambia la
    /// tabla sin tocar el disco (AD-11, AD-22).
    /// </para>
    /// </summary>
    /// <param name="decision">The new decision.</param>
    public void Apply(PlayabilityDecision decision)
    {
        _decision = decision ?? PlayabilityDecision.Playable();

        Changed?.Invoke();
    }

    /// <summary>
    /// Notifies that the table of the actions has to be rendered again.
    /// </summary>
    public void Refresh() => Changed?.Invoke();
}

/// <summary>
/// The command of the action «Jugar» (AC1, AC6, UX-DR-19).
///
/// <para>
/// El comando es el que REHUSA actuar cuando la instalación no está certificada: el
/// botón queda HABILITADO para que <c>Tab</c> lo alcance, pero <c>Enter</c> no produce
/// ningún cambio de estado, no inicia el juego y no dispara descarga, instalación ni
/// reparación implícita (AC5, AC6, UX-DR-26).
/// </para>
/// <para>
/// El comando es la ÚNICA vía de lanzamiento: el panel de estado, la ficha y el error de
/// lanzamiento NO ofrecen ejecutar el juego (AC4, UX-DR-19).
/// </para>
/// </summary>
public sealed class GamePlayCommand
{
    private readonly Func<string, CancellationToken, Task<bool>> _launch;

    /// <summary>
    /// Creates the command of the action «Jugar».
    /// </summary>
    /// <param name="launch">
    /// Action that launches the videogame, which receives the identifier of the game and
    /// the token that cancels the operation, and answers whether the launch happened.
    /// </param>
    public GamePlayCommand(Func<string, CancellationToken, Task<bool>> launch)
    {
        ArgumentNullException.ThrowIfNull(launch);

        _launch = launch;
    }

    /// <summary>
    /// Whether the last invocation of the command launched the videogame (AC6).
    /// </summary>
    public bool LastLaunchSucceeded { get; private set; }

    /// <summary>
    /// Invokes the command of the action «Jugar» (AC1, AC6).
    ///
    /// <para>
    /// Con la instalación CERTIFICADA, el comando lanza el juego a través del módulo
    /// <c>GameLauncher</c>, que es su ÚNICA vía de arranque (AC4, UX-DR-19).
    /// </para>
    /// <para>
    /// Con la instalación NO certificada, el comando REHUSA: no lanza ningún proceso, no
    /// descarga, no instala y no repara, así que el estado no cambia (AC6).
    /// </para>
    /// </summary>
    /// <param name="decision">
    /// Decision about the playability of the installation, que decide si el comando
    /// actúa o rehusa.
    /// </param>
    /// <param name="gameId">Identifier of the videogame.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>Whether the videogame was launched.</returns>
    public async Task<bool> ExecuteAsync(
        PlayabilityDecision decision,
        string gameId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);

        // AC6: el comando rehusa y NO toca nada: ni el juego, ni la descarga, ni la
        // instalación, ni la reparación.
        if (decision.IsBlocked)
        {
            LastLaunchSucceeded = false;

            return false;
        }

        LastLaunchSucceeded = await _launch(gameId, cancellationToken).ConfigureAwait(true);

        return LastLaunchSucceeded;
    }
}
