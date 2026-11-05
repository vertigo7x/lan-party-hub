using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Client.Application.Install;

/// <summary>
/// A path of the installation manifest ESCAPES the installation root (AD-9, NFR-5,
/// AC2, AC3).
///
/// <para>
/// El rechazo ocurre ANTES de tocar el disco: el manifiesto no admite rutas absolutas
/// y ningún destino puede quedar fuera de la raíz configurada, así que el rechazo no
/// deja ni una escritura ni un archivo parcial (AD-27).
/// </para>
/// <para>
/// El rechazo nombra la ruta que lo intentó y la operación del manifiesto que la
/// declaró, y lleva la CAUSA del catálogo, que es el código estable
/// <see cref="ErrorCatalog.InstallPathOutsideRootCode"/> (AD-15, AD-25).
/// </para>
/// </summary>
public sealed class PathEscapeException : Exception
{
    /// <summary>
    /// Creates the exception of a path of the manifest that escapes the root (AD-9).
    /// </summary>
    /// <param name="relativePath">Path of the manifest, as the manifest wrote it.</param>
    /// <param name="canonicalPath">Canonical absolute path that the path resolved to.</param>
    /// <param name="rootPath">Canonical absolute path of the installation root.</param>
    /// <param name="operation">
    /// Name of the operation of the manifest that declared the path, or <c>null</c>
    /// when the escape was detected before evaluating any operation.
    /// </param>
    /// <param name="cause">Cause of the rejection, from the catalog of errors.</param>
    public PathEscapeException(
        string relativePath,
        string canonicalPath,
        string rootPath,
        string? operation = null,
        ApiError? cause = null)
        : base($"La ruta '{relativePath}' del manifiesto de instalación resuelve en '{canonicalPath}', que está FUERA de la raíz de instalación '{rootPath}'.")
    {
        RelativePath = relativePath;
        CanonicalPath = canonicalPath;
        RootPath = rootPath;
        Operation = operation;
        Cause = cause ?? ErrorCatalog.Resolve(ErrorCatalog.InstallPathOutsideRootCode);
    }

    /// <summary>Path of the manifest, as the manifest wrote it.</summary>
    public string RelativePath { get; }

    /// <summary>Canonical absolute path that the path resolved to.</summary>
    public string CanonicalPath { get; }

    /// <summary>Canonical absolute path of the installation root.</summary>
    public string RootPath { get; }

    /// <summary>
    /// Name of the operation of the manifest that declared the path, or <c>null</c> when
    /// the escape was detected before evaluating any operation.
    /// </summary>
    public string? Operation { get; }

    /// <summary>
    /// Cause of the rejection, taken from the catalog of errors (AD-15, AD-25).
    /// </summary>
    public ApiError Cause { get; }
}
