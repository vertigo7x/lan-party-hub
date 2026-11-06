using LANPartyHub.Client.Domain.Install;
using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Client.Application.GameLauncher;

/// <summary>
/// The rule that DERIVES the playability of an installation (FR-9, AC1, AC2, UX-DR-17,
/// UX-DR-18, AD-1, AD-10).
///
/// <para>
/// La regla es PURA: recibe el VEREDICTO de la certificación de 7.1 y devuelve la
/// DECISIÓN, sin tocar el disco, sin red y sin conocer la interfaz (AD-1).
/// </para>
/// <para>
/// El veredicto es la ÚNICA definición de «instalación válida» y de «instalación
/// reparable» del sistema, así que esta regla NO declara por su cuenta si la
/// instalación vale: LEE el veredicto (AD-10, AC5).
/// </para>
/// <para>
/// La decisión NO se persiste: se REderiva al pintar, a partir del veredicto y del
/// inspector de 8.1, así que no hay columna nueva ni migración (AD-11, AD-22).
/// </para>
/// <para>
/// El motivo visible es el <c>message</c> de una entrada del catálogo de errores, así que
/// la MICROCOPIA es ÚNICA en todo el sistema (AD-15, AD-25).
/// </para>
/// </summary>
public static class PlayabilityResolver
{
    /// <summary>
    /// Derives the playability of an installation from its verdict (AC1, AC2).
    ///
    /// <para>
    /// Una instalación CERTIFICADA habilita «Jugar». Una instalación inválida y REPARABLE
    /// bloquea «Jugar» con el motivo visible y hace «Reparar» la primaria. Una
    /// instalación inválida y NO reparable bloquea «Jugar» con el motivo visible y hace
    /// «Descargar de nuevo» la única primaria. En los dos casos bloqueados la
    /// secundaria es «Diagnosticar» (AC1, AC2, UX-DR-17).
    /// </para>
    /// </summary>
    /// <param name="verdict">
    /// Verdict of the certification of the installation, or <c>null</c> when the client
    /// has no installation registered for the videogame.
    /// </param>
    /// <returns>The decision about the playability of the installation.</returns>
    public static PlayabilityDecision Resolve(InstallationVerdict? verdict)
    {
        // Sin instalación no hay nada que lanzar: la acción se bloquea con el motivo
        // visible de la instalación INCOMPLETA (AC1, AC3).
        if (verdict is null)
        {
            return PlayabilityDecision.Blocked(
                ErrorCatalog.InstallationIncompleteCode,
                PlayabilityAction.Redownload);
        }

        if (verdict.IsInstalled)
        {
            return PlayabilityDecision.Playable();
        }

        var reasonCode = ReasonCodeOf(verdict);

        // La acción primaria depende de la REPARABILIDAD, que es lo único que decide si
        // la instalación puede rehacerse sin volver a descargar (AC1, AC2, UX-DR-20).
        var primaryAction = verdict.IsRepairable
            ? PlayabilityAction.Repair
            : PlayabilityAction.Redownload;

        return PlayabilityDecision.Blocked(reasonCode, primaryAction);
    }

    /// <summary>
    /// The stable code that carries the VISIBLE reason of the block (AC1, AD-15).
    ///
    /// <para>
    /// El código se deriva de la condición que falló, y NO del <c>code</c> que el
    /// veredicto ya lleva: el <c>code</c> del veredicto es un dato interno, y el motivo
    /// visible es una entrada NUEVA del catálogo con su microcopia propia (AD-15,
    /// AD-25).
    /// </para>
    /// </summary>
    /// <param name="verdict">Verdict of the certification of the installation.</param>
    /// <returns>The stable code of the visible reason.</returns>
    private static string ReasonCodeOf(InstallationVerdict verdict)
    {
        // La condición que falló es la que explica el bloqueo: el paquete que no
        // conserva su validación, el árbol que no verifica o la ruta que sale de la raíz
        // son tres diagnósticos distintos (AD-15, AD-25).
        foreach (var condition in verdict.FailedConditions)
        {
            switch (condition)
            {
                case InstallationVerdict.Condition.PackageValidatedAgainstAnchor:
                    return ErrorCatalog.InstallationPackageMismatchCode;

                case InstallationVerdict.Condition.ManifestVerifyPassed:
                    return ErrorCatalog.GameExecutableMissingCode;

                case InstallationVerdict.Condition.PathsConfinedToRoot:
                    return ErrorCatalog.InstallationIncompleteCode;
            }
        }

        // Una instalación que no está certificada pero cuyo veredicto no declara
        // condiciones fallidas es una instalación INCOMPLETA: el motivo visible nunca se
        // inventa, así que se usa la entrada del catálogo que la describe (AD-15).
        return ErrorCatalog.InstallationIncompleteCode;
    }
}
