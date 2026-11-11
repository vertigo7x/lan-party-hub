using System.Text;

namespace LANPartyHub.IntegrationTests;

/// <summary>
/// The REAL interpreter of the host, and the way a probe script is run with it
/// (AD-16, AD-17).
///
/// <para>
/// El producto es una aplicación de WINDOWS (AD-17), así que el intérprete del producto
/// es <c>cmd.exe</c>. La suite se ejecuta también en otros hosts, y ahí el intérprete
/// real es el de ese host: <c>sh</c>. La prueba necesita un intérprete REAL porque lo
/// que verifica es que el proceso hijo arranca de verdad, recibe los argumentos y
/// corre en el directorio de trabajo (AC1), y eso se verifica igual con cualquiera de
/// los dos.
/// </para>
/// <para>
/// El probe se escribe en el LENGUAJE del intérprete, porque cada uno registra los
/// argumentos y el directorio de trabajo a su manera.
/// </para>
/// </summary>
internal static class HostInterpreter
{
    /// <summary>
    /// The REAL interpreter of the host.
    /// </summary>
    public static string Path => OperatingSystem.IsWindows()
        ? System.IO.Path.Combine(Environment.SystemDirectory, "cmd.exe")
        : "/bin/sh";

    /// <summary>
    /// The extension of the probe script of the host.
    /// </summary>
    public static string ProbeExtension => OperatingSystem.IsWindows() ? ".cmd" : ".sh";

    /// <summary>
    /// The path of the executable, relative to the root of the installation, as the
    /// manifest declares it.
    /// </summary>
    /// <remarks>
    /// El manifiesto describe una instalación de WINDOWS (AD-17), así que en Windows la
    /// ruta es la de siempre. En los demás hosts la ruta se declara con el separador
    /// del host, que es el que la resolución entiende (AD-9).
    /// </remarks>
    public static string ExecutablePath => OperatingSystem.IsWindows()
        ? @"game\cmd.exe"
        : "game/sh";

    /// <summary>
    /// The path of an executable OUTSIDE the root of the installation, as the manifest
    /// declares it.
    /// </summary>
    public static string OutsideRootPath => OperatingSystem.IsWindows()
        ? @"..\fuera\cmd.exe"
        : "../fuera/sh";

    /// <summary>
    /// The arguments of a launch whose process has to KEEP RUNNING.
    /// </summary>
    /// <remarks>
    /// `ping` de Windows y `sleep` del host son procesos que duran, y la prueba
    /// comprueba lo mismo en ambos: que el proceso sigue vivo (AC2).
    /// </remarks>
    public static IReadOnlyList<string> LongRunningArguments => OperatingSystem.IsWindows()
        ? ["/c", "ping", "-n", "30", "127.0.0.1"]
        : ["-c", "sleep 30"];

    /// <summary>
    /// The arguments that make the interpreter run the probe script with the given
    /// arguments.
    /// </summary>
    /// <param name="probePath">Path of the probe script, relative to the root.</param>
    /// <param name="arguments">Arguments that the probe has to receive.</param>
    /// <returns>The arguments of the launch.</returns>
    public static IReadOnlyList<string> ProbeArguments(string probePath, IReadOnlyList<string> arguments)
    {
        if (OperatingSystem.IsWindows())
        {
            // `/u` hace que los redireccionamientos de cmd.exe conserven la ruta de
            // trabajo Unicode; así la prueba no depende de la página OEM del perfil.
            return ["/u", "/c", probePath, .. arguments];
        }

        // `sh` ejecuta el script y le pasa los argumentos que siguen, en orden.
        return [probePath, .. arguments];
    }

    /// <summary>
    /// The content of the probe script, in the language of the interpreter of the host.
    /// </summary>
    public static string ProbeContent => OperatingSystem.IsWindows()
        ? new StringBuilder()
            // El script se interpreta con la página OEM de cmd.exe: escribir
            // rutas absolutas UTF-8 falla cuando el perfil de Windows contiene
            // caracteres no ASCII. El directorio de trabajo ya es la raíz que
            // se verifica, así que las salidas relativas son deliberadas.
            .AppendLine("echo %* > args.txt")
            .AppendLine("cd > cwd.txt")
            .ToString()
        : new StringBuilder()
            // El probe registra los argumentos que recibió y el directorio de trabajo
            // en el que corrió (AD-16). Las salidas son relativas, porque el
            // directorio de trabajo ya es la raíz que se verifica.
            .AppendLine("printf '%s\\n' \"$@\" > args.txt")
            .AppendLine("pwd > cwd.txt")
            .ToString();

    /// <summary>
    /// The encoding in which the interpreter of the host writes the output of the
    /// probe.
    /// </summary>
    /// <remarks>
    /// <c>cmd.exe</c> con <c>/u</c> escribe en UTF-16, y <c>sh</c> escribe en UTF-8.
    /// </remarks>
    public static Encoding ProbeEncoding => OperatingSystem.IsWindows() ? Encoding.Unicode : Encoding.UTF8;
}
