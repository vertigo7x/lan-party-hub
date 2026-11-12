using LANPartyHub.Client.Domain.Install;
using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Client.Application.Ports;

/// <summary>
/// Port of the INSPECTION of an installation before launching a videogame (AD-19,
/// AD-9, AC1).
///
/// <para>
/// El port existe para que el caso de uso del lanzamiento NO conozca el disco: la
/// certificación de la instalación y la resolución de la raíz son reglas que viven en
/// otros sitios, y el lanzador las CONSUME a través de este puerto (AD-1, AD-19).
/// </para>
/// <para>
/// La configuración de lanzamiento que el port entrega es la del MANIFIESTO FIRMADO, no
/// la del snapshot del catálogo: el snapshot no lleva firma y lo que se ejecuta no puede
/// depender de un dato sin firmar (AD-2).
/// </para>
/// </summary>
public interface IInstallationInspector
{
    /// <summary>
    /// Canonical absolute path of the installation root of a videogame, the folder that
    /// confines ITS installation (AD-9, FR-8).
    /// </summary>
    /// <param name="gameId">Identifier of the videogame.</param>
    string GameRootPath(string gameId);

    /// <summary>
    /// Reads the installation that the client registered for a videogame, and CERTIFIES
    /// it (AD-19, AC1).
    ///
    /// <para>
    /// La certificación es la ÚNICA definición de «instalación válida» del sistema, así
    /// que el lanzador NO declara por su cuenta si la instalación vale: lee el veredicto
    /// (AC1, AD-1).
    /// </para>
    /// </summary>
    /// <param name="gameId">Identifier of the videogame.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>
    /// The inspection of the installation, or <c>null</c> when the client has no
    /// installation registered for that videogame.
    /// </returns>
    Task<InstallationInspection?> InspectAsync(string gameId, CancellationToken cancellationToken);
}

/// <summary>
/// What the inspection of an installation asserts (AD-19, AC1).
///
/// <param name="RootPath">Canonical absolute path of the installation root.</param>
/// <param name="Verdict">Verdict of the certification of the installation.</param>
/// <param name="LaunchSettings">
/// Launch configuration of the SIGNED manifest: the executable path relative to the
/// root, its arguments and its working directory (AD-2).
/// </param>
public sealed record InstallationInspection(
    string RootPath,
    InstallationVerdict Verdict,
    InstallationManifest.LaunchSettings LaunchSettings);
