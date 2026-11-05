using System.Security.Cryptography;
using System.Text;

namespace LANPartyHub.Client.Domain.Install;

/// <summary>
/// The certification of an installation (AD-7, AD-9, AD-10, AC1, AC5, AC6).
///
/// <para>
/// La certificación es una REGLA PURA del dominio: recibe las tres CONDICIONES y
/// devuelve un veredicto, sin tocar el disco, sin red y sin conocer el adaptador. Es la
/// ÚNICA definición de «instalación válida» y de «reparable» del sistema, así que las
/// historias que ramifican sobre ella leen el mismo veredicto (AC7).
/// </para>
/// <para>
/// <c>Installed</c> exige las TRES condiciones —paquete validado contra el ancla fijada,
/// <c>verify</c> del manifiesto sobre el árbol, y toda ruta escrita dentro de la raíz— y
/// NINGUNA sustituye a otra (AC5).
/// </para>
/// <para>
/// El identificador de la raíz de instalación se deriva de forma DETERMINISTA de su ruta
/// canónica, así que es estable ante una reparación y ante la reinstalación del binario:
/// el identificador no depende de ningún estado, porque no hay ningún estado que
/// generarlo (AC1, AD-22).
/// </para>
/// </summary>
public static class InstallationCertification
{
    /// <summary>
    /// Number of characters of the hexadecimal identifier of an installation root
    /// (AC1).
    /// </summary>
    public const int InstallDirectoryIdLength = 64;

    /// <summary>
    /// Certifies an installation over its three conditions (AC5, AC6).
    /// </summary>
    /// <param name="packageValidatedAgainstAnchor">
    /// Whether the package was validated against the FIXED trust anchor and its
    /// <c>sha256</c> still matches the one of the SIGNED manifest (AD-2, AD-8).
    /// </param>
    /// <param name="manifestVerifyPassed">
    /// Whether the <c>verify</c> operation of the manifest passes over the installed
    /// tree (AD-7).
    /// </param>
    /// <param name="pathsConfinedToRoot">
    /// Whether every written path resolves inside the configured installation root
    /// (AD-9).
    /// </param>
    /// <param name="repairablePackageAvailable">
    /// Whether the package that was already validated is STILL AVAILABLE and its
    /// <c>sha256</c> STILL matches, so the installation can be redone from it without
    /// downloading again (AC6).
    /// </param>
    /// <returns>The verdict of the certification.</returns>
    public static InstallationVerdict Certify(
        bool packageValidatedAgainstAnchor,
        bool manifestVerifyPassed,
        bool pathsConfinedToRoot,
        bool repairablePackageAvailable) =>
        InstallationVerdict.Certify(
            packageValidatedAgainstAnchor,
            manifestVerifyPassed,
            pathsConfinedToRoot,
            repairablePackageAvailable);

    /// <summary>
    /// Derives the opaque identifier of an installation root from its CANONICAL path
    /// (AC1, AD-22).
    ///
    /// <para>
    /// El identificador es el <c>sha256</c> hexadecimal de la ruta canónica de la raíz, así
    /// que es estable ante una reparación (la raíz no se mueve) y ante la reinstalación
    /// del binario (la carpeta de datos sobrevive al binario). No hay estado nuevo que
    /// mantenerlo (AC1).
    /// </para>
    /// </summary>
    /// <param name="canonicalRootPath">Canonical absolute path of the installation root.</param>
    /// <returns>The opaque identifier of the root, in lowercase hexadecimal.</returns>
    /// <exception cref="ArgumentException">The path is empty or whitespace.</exception>
    public static string DeriveInstallDirectoryId(string canonicalRootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalRootPath);

        // La ruta canónica se normaliza a minúsculas porque el identificador describe la
        // MISMA raíz y no una forma de escribirla: en Windows, «C:\Juegos» y
        // «c:\juegos» son la misma carpeta (AC1).
        var normalized = canonicalRootPath.Trim().ToLowerInvariant();

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));

        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
