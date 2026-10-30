using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Client.Application.Catalog;

/// <summary>
/// Derivation of the view state of a videogame of the catalog (AC3, AC4, AR-5, AR-8,
/// AD-10, AD-18).
///
/// <para>
/// The derivation is PURE and it lives in the Application layer: it knows the snapshot
/// that the client synchronized, the version that the server publishes and the
/// installations that the client registered, and it does NOT talk to the server and does
/// NOT know the framework of the interface (AD-1).
/// </para>
/// <para>
/// The rule is FIXED and it has a single order: a client whose snapshot is older than the
/// version that the server publishes, or that has NO snapshot at all, shows
/// <c>sincronizando</c>; a game without a validated installation shows <c>disponible</c>; a
/// game whose validated installation is older than the published version shows
/// <c>instalado con versión desactualizada</c>; and a game whose installation is up to
/// date shows the text of <see cref="InstallationState.Installed"/>, which is the text that
/// the single table of microcopy already has (AC3, AR-5).
/// </para>
/// <para>
/// The comparison of the versions NEVER uses the lexicographic order of the text: it uses
/// the semantic order that the catalog declares in
/// <see cref="CatalogVersionOrder"/>, so «1.10» is newer than «1.9» (AC3, AR-5).
/// </para>
/// </summary>
public static class CatalogViewStateResolver
{
    /// <summary>
    /// Derives the view state of a videogame (AC3).
    ///
    /// <param name="game">Videogame of the snapshot that the client synchronized.</param>
    /// <param name="snapshotCatalogVersion">
    /// Version of the snapshot that the client applied, or <c>null</c> when the client
    /// never applied one.
    /// </param>
    /// <param name="serverCatalogVersion">
    /// Version of the catalog that the server publishes, or <c>null</c> when the server
    /// did not answer and the client does NOT know it.
    /// </param>
    /// <param name="installation">
    /// Installation that the client registered for the videogame, or <c>null</c> when the
    /// client has none.
    /// </param>
    /// <returns>The derived view state of the videogame.</returns>
    public static CatalogViewState Resolve(
        GameDescriptor? game,
        long? snapshotCatalogVersion,
        long? serverCatalogVersion,
        InstalledGameView? installation)
    {
        // El cliente sin snapshot, o con un snapshot ANTERIOR al que publica el servidor,
        // todavía no conoce el estado real del juego: la insignia dice «sincronizando»
        // (AC3).
        if (game is null ||
            snapshotCatalogVersion is null ||
            (serverCatalogVersion is not null && serverCatalogVersion > snapshotCatalogVersion))
        {
            return CatalogViewState.Synchronizing;
        }

        // Sin instalación validada el juego está DISPONIBLE: se puede descargar (AC3).
        if (installation is null || !IsValidated(installation.ValidationState))
        {
            return CatalogViewState.Available;
        }

        // La instalación validada cuya versión es anterior a la publicada está
        // DESACTUALIZADA (AC3, AR-5).
        if (CatalogVersionOrder.IsOlder(installation.InstalledVersion, game.Version))
        {
            return CatalogViewState.InstalledOutdatedVersion;
        }

        // La instalación validada que está al día no necesita un estado de vista nuevo: el
        // texto de «Instalado» ya existe en la tabla de microcopy (AC3, AD-10).
        return CatalogViewState.Available;
    }

    /// <summary>
    /// Tells whether an installation is VALIDATED, which is the only state that makes the
    /// installed version comparable with the published one (AC3).
    /// </summary>
    /// <param name="state">State of the installation.</param>
    /// <returns>Whether the installation is validated.</returns>
    private static bool IsValidated(InstallationState state) =>
        state is InstallationState.Installed or InstallationState.Repairable;
}

/// <summary>
/// The installation of a videogame as the derivation of the view state sees it (AC3).
///
/// <para>
/// It is NOT a contract of <c>Shared</c>: it is a projection of the local state of the
/// client that the derivation consumes, and it carries the version that is installed and
/// the state of the installation (AD-18, AD-22).
/// </para>
/// </summary>
/// <param name="GameId">Identifier of the game that is installed.</param>
/// <param name="InstalledVersion">Version of the game that is installed.</param>
/// <param name="ValidationState">State of the installation.</param>
public sealed record InstalledGameView(
    string GameId,
    string InstalledVersion,
    InstallationState ValidationState);
