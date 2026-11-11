using LANPartyHub.Client.Domain.Install;
using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Client.Application.Install;

/// <summary>
/// The policy of the paths of the installation manifest (AD-7, AD-9, NFR-5, AC2).
///
/// <para>
/// La política es una REGLA PURA de la APLICACIÓN: evalúa el manifiesto y RECHAZA las
/// rutas que no son admisibles, sin tocar el disco y sin conocer el adaptador (AD-1,
/// AD-16).
/// </para>
/// <para>
/// El manifiesto NO admite rutas absolutas: una ruta absoluta es una forma de salirse
/// de la raíz, y el conjunto cerrado de operaciones de AD-7 se evalúa en la raíz
/// configurada, así que el destino se escribe SIEMPRE como relativo (AD-9, AC2).
/// </para>
/// <para>
/// Tampoco admite <c>..</c>: un <c>..</c> que no sale de la raíz se puede normalizar
/// sin problema, pero uno que sí sale es una escapada, y el rechazo tiene que ocurrir
/// ANTES de tocar el disco para que no quede ninguna escritura (AD-27).
/// </para>
/// <para>
/// Una ruta VACÍA no es una escapada: los destinos de las operaciones que ESCRIBEN en el
/// disco tienen que estar declarados, así que una ruta vacía ahí se rechaza; pero las
/// rutas de la configuración de lanzamiento NO escriben en el disco y son opcionales, de
/// modo que su ausencia se admite y sólo se evalúan las DECLARADAS (AD-7, AD-9).
/// </para>
/// </summary>
public static class ManifestPathPolicy
{
    /// <summary>
    /// Evaluates the whole manifest and rejects the first path that is not admissible
    /// (AD-9, AC2).
    ///
    /// <para>
    /// El rechazo PRECEDE a cualquier escritura: el manifiesto entero se evalúa antes
    /// de aplicar la primera operación, así que un manifiesto con una ruta que escapa no
    /// deja ni un directorio ni un archivo creados (AD-27).
    /// </para>
    /// </summary>
    /// <param name="manifest">Manifest whose paths are evaluated.</param>
    /// <exception cref="PathEscapeException">
    /// The manifest declares a path that is absolute or that traverses out of the root
    /// (AD-9, AC2).
    /// </exception>
    public static void Evaluate(InstallationManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        // AD-7: las seis operaciones del conjunto cerrado son las ÚNICAS que declaran
        // rutas de destino, y cada una se evalúa con su propio nombre, para que el
        // rechazo diga QUÉ operación lo intentó (AD-15, AD-25).
        foreach (var operation in manifest.Operations)
        {
            switch (operation)
            {
                case InstallationManifest.Extract extract:
                    RequireRelative(extract.DestinationPath, "extract");

                    break;

                case InstallationManifest.CreateDirectory createDirectory:
                    RequireRelative(createDirectory.Path, "createDirectory");

                    break;

                case InstallationManifest.CopyFile copyFile:
                    RequireRelative(copyFile.DestinationPath, "copyFile");

                    break;

                case InstallationManifest.WriteConfig writeConfig:
                    RequireRelative(writeConfig.Path, "writeConfig");

                    break;

                case InstallationManifest.Verify verify:
                    RequireRelative(verify.Path, "verify");

                    break;

                case InstallationManifest.SetLaunchConfig setLaunchConfig:
                    // La configuración de lanzamiento NO escribe en el disco, así que sus
                    // rutas son OPCIONALES: una publicación puede declararlas vacías
                    // (AD-7). Sólo se evalúan las DECLARADAS, y se rechazan las que
                    // son absolutas o que escapan de la raíz, igual que los destinos de
                    // las demás operaciones (AD-9, AC2).
                    RequireDeclaredRelative(setLaunchConfig.Launch?.ExecutablePath, "setLaunchConfig");
                    RequireDeclaredRelative(setLaunchConfig.Launch?.WorkingDirectory, "setLaunchConfig");

                    break;

                default:
                    // AD-7: una operación fuera del conjunto cerrado NUNCA se ejecuta, y
                    // su rechazo lleva su propia causa, no la del confine (AD-15, AD-25).
                    break;
            }
        }
    }

    /// <summary>
    /// Tells whether a path of the manifest is ADMISSIBLE: relativa, sin <c>..</c> y sin
    /// raíz de unidad (AD-9, AC2).
    /// </summary>
    /// <param name="path">Path of the manifest, as the manifest wrote it.</param>
    /// <returns><c>true</c> when the path is admissible.</returns>
    public static bool IsAdmissible(string? path) => !IsRejected(path, out _);

    /// <summary>
    /// Rejects a path of the manifest that is absolute or that traverses out of the root
    /// (AD-9, AC2).
    /// </summary>
    /// <param name="path">Path of the manifest, as the manifest wrote it.</param>
    /// <param name="operation">
    /// Name of the operation of the manifest that declared the path, or <c>null</c>
    /// when the path is not bound to an operation.
    /// </param>
    /// <exception cref="PathEscapeException">
    /// The path is absolute or it traverses out of the root (AD-9, AC2).
    /// </exception>
    public static void RequireRelative(string? path, string? operation = null)
    {
        if (IsRejected(path, out _))
        {
            throw new PathEscapeException(
                path ?? string.Empty,
                path ?? string.Empty,
                string.Empty,
                operation,
                ErrorCatalog.Resolve(ErrorCatalog.InstallPathOutsideRootCode));
        }
    }

    /// <summary>
    /// Rejects a DECLARED path of the manifest that is absolute or that traverses out of
    /// the root (AD-9, AC2).
    ///
    /// <para>
    /// A diferencia de <see cref="RequireRelative"/>, una ruta VACÍA no se rechaza: la
    /// ruta no está DECLARADA, y una operación que no escribe en el disco puede no
    /// declararla (AD-7). La ausencia no es una escapada: lo que se rechaza es la ruta
    /// ABSOLUTA o la que sale de la raíz.
    /// </para>
    /// </summary>
    /// <param name="path">Path of the manifest, as the manifest wrote it.</param>
    /// <param name="operation">
    /// Name of the operation of the manifest that declared the path, or <c>null</c>
    /// when the path is not bound to an operation.
    /// </param>
    /// <exception cref="PathEscapeException">
    /// The path is absolute or it traverses out of the root (AD-9, AC2).
    /// </exception>
    public static void RequireDeclaredRelative(string? path, string? operation = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        RequireRelative(path, operation);
    }

    /// <summary>
    /// Tells whether a path of the manifest is rejected, and says WHY (AD-9, AC2).
    /// </summary>
    /// <param name="path">Path of the manifest, as the manifest wrote it.</param>
    /// <param name="reason">Human readable reason of the rejection.</param>
    /// <returns><c>true</c> when the path is rejected.</returns>
    public static bool IsRejected(string? path, out string reason)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            reason = "La ruta está vacía.";

            return true;
        }

        // AD-9, AC2: una ruta ABSOLUTA no se admite. Se rechaza por su FORMA, y no por
        // donde resuelva: el manifiesto declara destinos RELATIVOS a la raíz
        // configurada, así que una raíz de unidad o un UNC no tienen forma admitida.
        //
        // La forma se reconoce por SUYO, no por la del host: el manifiesto puede
        // declarar una ruta de Windows aunque la comprobación corra en otra
        // plataforma, y una ruta absoluta que el host no reconozca no por eso deja
        // de ser absoluta.
        if (InstallPathConfine.IsAbsoluteByForm(path))
        {
            reason = $"La ruta '{path}' es absoluta y el manifiesto sólo admite rutas relativas a la raíz de instalación.";

            return true;
        }

        // AD-9, AC2: un `..` que sale de la raíz es una ESCAPADA. Se normaliza la ruta
        // para saber si sale, sin tocar el disco.
        if (EscapesRoot(path))
        {
            reason = $"La ruta '{path}' sale de la raíz de instalación con segmentos '..'.";

            return true;
        }

        reason = string.Empty;

        return false;
    }

    /// <summary>
    /// Tells whether a relative path leaves the root when the <c>..</c> segments are
    /// resolved (AD-9, AC2).
    ///
    /// <para>
    /// La cuenta de los segmentos es la de un CAMINO RELATIVO: un <c>..</c> sube un
    /// nivel y un nombre baja uno, y salir por encima de la raíz es lo que se busca. Un
    /// <c>..</c> que sube y baja dentro de la raíz NO es una escapada.
    /// </para>
    /// </summary>
    /// <param name="relativePath">Relative path of the manifest.</param>
    /// <returns><c>true</c> when the path leaves the root.</returns>
    private static bool EscapesRoot(string relativePath)
    {
        var depth = 0;

        // Los segmentos se separan por AMBOS separadores, no por los del host: un
        // manifiesto declara rutas con cualquiera de los dos, y un `..` que sale de
        // la raíz lo es con independencia de la plataforma que comprueba.
        var segments = relativePath.Split(
            ['/', '\\'],
            StringSplitOptions.RemoveEmptyEntries);

        foreach (var segment in segments)
        {
            if (segment == "..")
            {
                depth--;

                if (depth < 0)
                {
                    return true;
                }
            }
            else if (segment != ".")
            {
                depth++;
            }
        }

        return false;
    }
}
