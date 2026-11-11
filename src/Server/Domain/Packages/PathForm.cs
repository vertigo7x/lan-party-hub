namespace LANPartyHub.Server.Domain.Packages;

/// <summary>
/// The form of the paths that the server accepts (AD-9, AD-12).
///
/// <para>
/// La forma de una ruta se reconoce por SUYO, no por la del host: una letra de
/// unidad seguida de dos puntos (<c>C:\</c>) o una ruta que empieza por separador
/// (<c>/</c>), que también cubre un UNC (<c>\\servidor\recurso</c>), son absolutas
/// en cualquier plataforma. La regla es la MISMA que aplica Windows, para que una
/// ruta sea absoluta en una plataforma y en la otra a la vez.
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

        // Una UNIDAD: `C:` o `C:\...`. La letra de la unidad tiene que ser una letra
        // ASCII, que es lo que Windows acepta: `Path.IsPathRooted` de .NET exige una
        // letra de unidad valida, así que `1:\x` NO es una ruta absoluta para Windows
        // y esta regla tampoco lo trata como tal (AD-9).
        if (value.Length >= 2 && char.IsAsciiLetter(value[0]) && value[1] == ':')
        {
            return true;
        }

        // Una ruta que empieza por separador. Un UNC (`\\servidor\recurso`) empieza
        // por dos, así que esta comprobación lo cubre tambien.
        return value[0] == '/' || value[0] == '\\';
    }
}
