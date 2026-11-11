namespace LANPartyHub.Server.Domain.Packages;

/// <summary>
/// The form of the paths that the server accepts (AD-9, AD-12).
///
/// <para>
/// La forma de una ruta se reconoce por SUYO, no por la del host: una letra de
/// unidad seguida de dos puntos (<c>C:\</c>), una ruta que empieza por separador
/// (<c>/</c>), o un UNC (<c>\\servidor\recurso</c>) son absolutas en cualquier
/// plataforma. El host puede reconocer mas formas de las que esta, pero no menos:
/// una ruta que es absoluta para el sistema que la escribio lo es para el que la
/// comprueba.
/// </para>
/// <para>
/// El servidor y el cliente son proyectos separados y <c>Shared</c> contiene solo
/// contratos (AD-18), así que la regla vive en el dominio de cada uno: es la MISMA
/// regla, escrita dos veces.
/// </para>
/// </summary>
public static class PathForm
{
    /// <summary>
    /// Tells whether a value is ABSOLUTE by its own form, whatever the host is
    /// (AD-9, AD-12).
    /// </summary>
    /// <param name="value">Value to check.</param>
    /// <returns><c>true</c> when the value is absolute by its form.</returns>
    public static bool IsAbsoluteByForm(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        // Una unidad: `C:` o `C:\...`. Se admite CUALQUIER carácter antes de los dos
        // puntos, como hace Windows, para que la regla no dependa del host (AD-9).
        if (value.Length >= 2 && value[1] == ':')
        {
            return true;
        }

        // Un UNC: `\\servidor\recurso`.
        if (value.Length >= 2 && value[0] == '\\' && value[1] == '\\')
        {
            return true;
        }

        // Una ruta que empieza por separador.
        return value[0] == '/' || value[0] == '\\';
    }
}
