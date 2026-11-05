using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Client.Application;

/// <summary>
/// An installation as it is persisted in the database of the client (AD-22, AC1).
///
/// <para>
/// La instalación la posee el cliente y su CLAVE es el <c>gameId</c> (AD-22). El
/// <c>installDirectoryId</c> es el identificador opaco de la raíz de instalación, y
/// <c>installPath</c> es su ruta local, que NUNCA sale del cliente (AD-22).
/// </para>
/// <para>
/// El <c>installDirectoryId</c> NO cambia ante una reparación ni ante la reinstalación
/// del binario, así que la fila conserva el identificador que ya tenía (AC1).
/// </para>
/// </summary>
/// <param name="GameId">Identifier of the videogame that is installed.</param>
/// <param name="InstalledVersion">Version of the videogame that is installed.</param>
/// <param name="ValidationState">State of the installation, as the name of the value of the machine state.</param>
/// <param name="InstallPath">Local path of the installation root. It never leaves the client.</param>
/// <param name="InstallDirectoryId">Opaque identifier of the installation root.</param>
public sealed record ClientInstallation(
    string GameId,
    string InstalledVersion,
    InstallationState ValidationState,
    string InstallPath,
    string InstallDirectoryId);
