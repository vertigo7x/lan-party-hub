using LANPartyHub.Client.Domain.Install;
using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Client.Application.GameLauncher;

/// <summary>
/// The DECISION about the playability of an installation (FR-9, AC1, AC2, UX-DR-17,
/// UX-DR-18, UX-DR-19).
///
/// <para>
/// La decisión es DATO, no código: dice si la acción «Jugar» está habilitada o
/// bloqueada, cuál es el MOTIVO VISIBLE del bloqueo, cuál es la acción PRIMARIA que
/// repara el problema y cuál es la SECUNDARIA que abre el diagnóstico (AC1, AC2).
/// </para>
/// <para>
/// El motivo visible es el <c>message</c> de una entrada del catálogo de errores, así que
/// la MICROCOPIA es ÚNICA en todo el sistema: el catálogo la declara y esta decisión la
/// lleva (AD-15, AD-25).
/// </para>
/// <para>
/// La decisión NO se persiste: se REDERIVA al pintar, a partir del veredicto de la
/// certificación de 7.1 y del inspector de 8.1, así que no hay columna nueva ni
/// migración (AD-11, AD-22).
/// </para>
/// </summary>
public sealed record PlayabilityDecision
{
    /// <summary>
    /// The installation is CERTIFIED and the action «Jugar» is ENABLED (AC1, UX-DR-17).
    /// </summary>
    public static PlayabilityDecision Playable() => new()
    {
        IsBlocked = false,
        ReasonCode = null,
        ReasonText = null,
        PrimaryAction = PlayabilityAction.Play,
        SecondaryAction = null,
    };

    /// <summary>
    /// The installation is CERTIFIED but its version is OLDER than the version published
    /// in the catalog for the same game, así que la acción primaria es «Actualizar»
    /// (AC1, AC4, FR-11, UX-DR-32).
    ///
    /// <para>
    /// La instalación SIGUE SIENDO VÁLIDA: «Jugar» NO se bloquea, porque el jugador puede
    /// jugar la versión que tiene mientras decide si actualiza (AC4, UX-DR-17).
    /// </para>
    /// </summary>
    /// <param name="publishedVersion">
    /// Version published in the catalog for the game, in its EXACT value (AC3).
    /// </param>
    /// <returns>The decision of the outdated installation.</returns>
    public static PlayabilityDecision Outdated(string publishedVersion) => new()
    {
        IsBlocked = false,
        ReasonCode = null,
        ReasonText = null,
        PrimaryAction = PlayabilityAction.Update,
        SecondaryAction = null,
        PublishedVersion = publishedVersion,
    };

    /// <summary>
    /// The installation is NOT certified and the action «Jugar» is BLOCKED with the
    /// reason VISIBLE next to its label (AC1, AC2, UX-DR-18).
    /// </summary>
    /// <param name="reasonCode">
    /// Stable code of the catalog that carries the visible reason (AD-15).
    /// </param>
    /// <param name="primaryAction">The PRIMARY action that solves the problem (AC1, AC2).</param>
    /// <returns>The decision of the block.</returns>
    public static PlayabilityDecision Blocked(
        string reasonCode,
        PlayabilityAction primaryAction) => new()
    {
        IsBlocked = true,
        ReasonCode = reasonCode,
        ReasonText = ErrorCatalog.Resolve(reasonCode).Message,
        PrimaryAction = primaryAction,
        SecondaryAction = PlayabilityAction.Diagnose,
    };

    /// <summary>
    /// The REPAIR of the installation is IN PROGRESS, así que la acción «Jugar» sigue
    /// BLOQUEADA con su motivo VISIBLE hasta que la instalación se valida (AC2,
    /// UX-DR-17, UX-DR-18).
    ///
    /// <para>
    /// La reparación en curso es un estado con su PROPIA entrada del catálogo de errores,
    /// así que el motivo visible NO se inventa y NO es el de la instalación incompleta
    /// (AD-15, AD-25).
    /// </para>
    /// </summary>
    /// <returns>The decision of the repair in progress.</returns>
    public static PlayabilityDecision RepairInProgress() => new()
    {
        IsBlocked = true,
        ReasonCode = ErrorCatalog.InstallationRepairInProgressCode,
        ReasonText = ErrorCatalog.Resolve(ErrorCatalog.InstallationRepairInProgressCode).Message,
        PrimaryAction = PlayabilityAction.Repair,
        SecondaryAction = null,
    };

    /// <summary>
    /// Whether the action «Jugar» is BLOCKED (AC1).
    /// </summary>
    public bool IsBlocked { get; init; }

    /// <summary>
    /// Whether the action «Jugar» is ENABLED, que es lo contrario de estar bloqueada
    /// (AC1).
    /// </summary>
    public bool CanPlay => !IsBlocked;

    /// <summary>
    /// Stable code of the catalog that carries the VISIBLE reason of the block, or
    /// <c>null</c> when the action «Jugar» is enabled (AD-15, AD-25).
    /// </summary>
    public string? ReasonCode { get; init; }

    /// <summary>
    /// The VISIBLE reason of the block, next to the label of the action «Jugar», or
    /// <c>null</c> when the action is enabled (AC1, UX-DR-18).
    ///
    /// <para>
    /// El motivo NUNCA está en un <em>tooltip</em>, NUNCA sólo en el log de diagnóstico y
    /// NUNCA requiere <em>hover</em> ni foco: aparece junto a la etiqueta (UX-DR-18).
    /// </para>
    /// </summary>
    public string? ReasonText { get; init; }

    /// <summary>
    /// The PRIMARY action of the surface: «Reparar» o «Descargar de nuevo» cuando la
    /// instalación es inválida, y «Jugar» cuando es válida (AC1, AC2, UX-DR-17).
    /// </summary>
    public PlayabilityAction PrimaryAction { get; init; }

    /// <summary>
    /// The SECONDARY action of the surface, que abre el error registrado, or
    /// <c>null</c> when there is none (AC1, AC2, UX-DR-17).
    /// </summary>
    public PlayabilityAction? SecondaryAction { get; init; }

    /// <summary>
    /// The version published in the catalog for the videogame, or <c>null</c> when the
    /// decision does not carry one (AC1, AC3).
    ///
    /// <para>
    /// La versión publicada se transporta en la decisión porque la insignia de la
    /// desactualización necesita MOSTRAR la versión disponible junto a la instalada
    /// (AC3, UX-DR-21).
    /// </para>
    /// </summary>
    public string? PublishedVersion { get; init; }

    /// <summary>
    /// Whether the installation is VALID but its version is OLDER than the version
    /// published in the catalog (AC1, AC4, FR-11).
    /// </summary>
    public bool IsOutdated => PrimaryAction == PlayabilityAction.Update;

    /// <summary>
    /// The three parts of the error of the block, ready to show, or <c>null</c> when the
    /// action «Jugar» is enabled (AD-15, AD-25, UX-DR-36).
    /// </summary>
    public ApiError? ReasonError =>
        ReasonCode is null
            ? null
            : ErrorCatalog.Resolve(ReasonCode);
}

/// <summary>
/// The actions of the table of the states of the installation (AC1, AC2, UX-DR-17).
///
/// <para>
/// El conjunto es CERRADO: la tabla de estados y de acción primaria es ÚNICA, y las
/// dos superficies —Biblioteca y ficha— la aplican igual, así que ninguna de las dos abre
/// un camino de lanzamiento distinto (AC1, AC2, UX-DR-17, UX-DR-19).
/// </para>
/// </summary>
public enum PlayabilityAction
{
    /// <summary>The action «Jugar», which launches the videogame (AC1).</summary>
    Play,

    /// <summary>
    /// The action «Reparar», which redoes the installation from the package that was
    /// already validated (AC1, UX-DR-20).
    /// </summary>
    Repair,

    /// <summary>
    /// The action «Descargar de nuevo», which downloads the package again because the
    /// validated one is not available (AC2).
    /// </summary>
    Redownload,

    /// <summary>
    /// The action «Diagnosticar», which opens the registered error (AC1, AC2).
    /// </summary>
    Diagnose,

    /// <summary>
    /// The action «Actualizar», which replaces an outdated installation with the version
    /// published in the catalog (AC1, AC4, FR-11, UX-DR-32).
    ///
    /// <para>
    /// «Actualizar» SUSTITUYE a la acción primaria: aparece SOLO cuando la instalación
    /// es VÁLIDA y su versión es anterior a la publicada, así que la invalidez de la
    /// instalación manda sobre la actualización (AC4).
    /// </para>
    /// </summary>
    Update,
}
