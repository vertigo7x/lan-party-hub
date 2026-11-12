using System.Text.RegularExpressions;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The tokens of the design: the component layer is built ONLY on the tokens approved in
/// DESIGN.md (DESIGN, UX-DR-1, AC1).
///
/// <para>
/// El guard lee el DESIGN.md y el código REAL de la capa de presentación, así que
/// observa lo que está commiteado y no una copia: un token que no exista en el diseño,
/// o un color o un radio escrito a mano en un componente, se ven aquí (AC1).
/// </para>
/// </summary>
public sealed class DesignTokensTests
{
    private static readonly string DesignFile = Path.Combine(
        RepositoryLayout.Root,
        "_bmad-output",
        "planning-artifacts",
        "ux-designs",
        "ux-lan-party-hub-2026-10-26",
        "DESIGN.md");

    private static readonly string PresentationDirectory = Path.Combine(
        RepositoryLayout.Root,
        "src",
        "Client",
        "Presentation");

    private static readonly string TokensFile = Path.Combine(
        PresentationDirectory,
        "Design",
        "DesignTokens.cs");

    /// <summary>
    /// Every COLOUR declared in the token class exists in the design (AC1).
    /// </summary>
    [Fact]
    public void Every_colour_of_the_tokens_exists_in_the_design()
    {
        var design = DesignMap("colors");
        var declared = DeclaredColours();

        Assert.NotEmpty(declared);

        var missing = declared
            .Where(entry => !design.ContainsValue(entry.Value))
            .Select(entry => $"{entry.Name} ({entry.Value})")
            .ToArray();

        Assert.True(
            missing.Length == 0,
            $"Hay colores en DesignTokens que no existen en DESIGN.md: {string.Join(", ", missing)}.");
    }

    /// <summary>
    /// Every RADIUS declared in the token class exists in the design (AC1, UX-DR-4).
    /// </summary>
    [Fact]
    public void Every_radius_of_the_tokens_exists_in_the_design()
    {
        var design = DesignMap("rounded");
        var declared = DeclaredRadii();

        Assert.NotEmpty(declared);

        var missing = declared
            .Where(entry => !design.Values.Contains($"{entry.Value}px"))
            .Select(entry => $"{entry.Name} ({entry.Value}px)")
            .ToArray();

        Assert.True(
            missing.Length == 0,
            $"Hay radios en DesignTokens que no existen en DESIGN.md: {string.Join(", ", missing)}.");
    }

    /// <summary>
    /// Every SPACING declared in the token class exists in the design (AC1, UX-DR-4).
    /// </summary>
    [Fact]
    public void Every_spacing_of_the_tokens_exists_in_the_design()
    {
        var design = DesignMap("spacing");
        var declared = DeclaredSpacings();

        Assert.NotEmpty(declared);

        var missing = declared
            .Where(entry => !design.Values.Contains($"{entry.Value}px"))
            .Select(entry => $"{entry.Name} ({entry.Value}px)")
            .ToArray();

        Assert.True(
            missing.Length == 0,
            $"Hay espaciados en DesignTokens que no existen en DESIGN.md: {string.Join(", ", missing)}.");
    }

    /// <summary>
    /// Ningún componente declara un color o un radio PROPIO: los dos vienen del token
    /// (AC1, UX-DR-1).
    /// </summary>
    [Fact]
    public void No_component_declares_a_colour_or_a_radius_of_its_own()
    {
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(PresentationDirectory, "*.cs", SearchOption.AllDirectories))
        {
            if (Path.GetFullPath(file) == Path.GetFullPath(TokensFile))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            var name = Path.GetFileName(file);

            if (Regex.IsMatch(text, @"Color\.(FromRgb|Parse)\("))
            {
                offenders.Add($"{name}: declara un color propio.");
            }

            if (Regex.IsMatch(text, @"new CornerRadius\(\s*\d"))
            {
                offenders.Add($"{name}: declara un radio propio.");
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"Hay tokens propios en los componentes: {string.Join(" ", offenders)}");
    }

    /// <summary>
    /// La sombra proyectada NO es un indicador de hover ni de jerarquía: la profundidad se
    /// expresa con las superficies y la línea de pelo. La ÚNICA sombra admitida es la del
    /// diálogo modal y la del menú desplegable (AC1, UX-DR-3).
    /// </summary>
    [Fact]
    public void The_only_shadow_is_the_one_of_the_modal_and_the_drop_down()
    {
        var allowed = new[]
        {
            "PrivilegedOperationDialog.cs",
            "DownloadCancelDialog.cs",
            "ErrorToast.cs",
        };

        var offenders = Directory
            .EnumerateFiles(PresentationDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(file => File.ReadAllText(file).Contains("BoxShadow", StringComparison.Ordinal))
            .Select(file => Path.GetFileName(file))
            .Where(name => !allowed.Contains(name, StringComparer.Ordinal))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"La sombra proyectada solo se admite en el diálogo modal y en el menú desplegable: {string.Join(", ", offenders)}");
    }

    /// <summary>
    /// La profundidad se expresa TONALMENTE: tres niveles de superficie y la línea de
    /// pelo, sin sombra (AC1, UX-DR-3).
    /// </summary>
    [Fact]
    public void The_depth_is_expressed_with_three_surfaces_and_the_hairline()
    {
        var surfaces = DeclaredColours()
            .Where(entry => entry.Name.StartsWith("Surface", StringComparison.Ordinal))
            .ToArray();

        Assert.True(surfaces.Length >= 4, "Faltan superficies en los tokens de profundidad.");

        // La escala tonal va del sunken al overlay, y cada peldaño es MAS CLARO que el
        // anterior: la profundidad se expresa con la superficie, no con una sombra.
        var escala = new[] { "SurfaceSunken", "SurfaceBase", "SurfaceRaised", "SurfaceOverlay" };
        var valores = new List<int>();

        foreach (var nombre in escala)
        {
            var entry = surfaces.SingleOrDefault(candidate => candidate.Name == nombre);

            Assert.True(entry.Name is not null, $"Falta la superficie '{nombre}' en los tokens.");

            valores.Add(int.Parse(entry.Value[1..], System.Globalization.NumberStyles.HexNumber));
        }

        for (var nivel = 1; nivel < valores.Count; nivel++)
        {
            Assert.True(
                valores[nivel] > valores[nivel - 1],
                $"La superficie '{escala[nivel]}' tiene que ser mas clara que '{escala[nivel - 1]}'.");
        }

        // Y la linea de pelo existe: separa las superficies sin recur al borde fuerte.
        Assert.Contains(DeclaredColours(), entry => entry.Name == "BorderHairline");
    }

    /// <summary>
    /// Ningún componente escribe un PESO de fuente propio: los pesos viven en los tokens
    /// (AC1, UX-DR-1, UX-DR-2).
    /// </summary>
    [Fact]
    public void No_component_writes_a_font_weight_of_its_own()
    {
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(PresentationDirectory, "*.cs", SearchOption.AllDirectories))
        {
            if (Path.GetFullPath(file) == Path.GetFullPath(TokensFile))
            {
                continue;
            }

            var text = File.ReadAllText(file);

            if (Regex.IsMatch(text, @"FontWeight\s*=\s*(?:Avalonia\.Media\.)?FontWeight\."))
            {
                offenders.Add(Path.GetFileName(file));
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"Hay pesos de fuente escritos a mano: {string.Join(", ", offenders)}");
    }

    /// <summary>
    /// Cada estilo de texto del token existe en el diseño, con su tamaño y su peso
    /// (AC1, UX-DR-2).
    /// </summary>
    [Fact]
    public void Every_text_style_of_the_tokens_exists_in_the_design()
    {
        var design = File.ReadAllText(DesignFile);
        var estilos = new[]
        {
            ("display", "DisplayFontSize", "700"),
            ("headline", "HeadlineFontSize", "600"),
            ("title", "TitleFontSize", "600"),
            ("body", "BodyFontSize", "400"),
            ("body-strong", "BodyStrongFontSize", "600"),
            ("label", "LabelFontSize", "500"),
            ("meta", "MetaFontSize", "500"),
        };

        var tokens = File.ReadAllText(TokensFile);

        foreach (var (estilo, token, peso) in estilos)
        {
            var seccion = design.Substring(design.IndexOf($"  {estilo}:", StringComparison.Ordinal));

            Assert.Contains("fontFamily: 'Inter'", seccion);
            Assert.Contains($"fontWeight: '{peso}'", seccion);
            Assert.Contains(token, tokens);
        }
    }

    /// <summary>
    /// El nombre de un juego se trunca a UNA línea con elipsis y NUNCA se envuelve a
    /// tres (AC1, UX-DR-2, UX-DR-10).
    /// </summary>
    [Fact]
    public void The_name_of_a_game_is_never_wrapped()
    {
        var grid = File.ReadAllText(Path.Combine(PresentationDirectory, "Catalog", "CatalogGrid.cs"));

        // Cada texto que lleva el NOMBRE de un juego se trunca a una línea con elipsis y
        // no se envuelve. El resto de los textos de la superficie pueden envolver: el
        // mensaje de catalogo vacio es una frase, no un nombre.
        var bloques = grid.Split("new TextBlock");
        var conNombre = bloques.Where(bloque => bloque.Contains("Text = game.Name", StringComparison.Ordinal)).ToArray();

        Assert.Equal(2, conNombre.Length);

        foreach (var bloque in conNombre)
        {
            Assert.Contains("TextTrimming = TextTrimming.CharacterEllipsis,", bloque);
            Assert.Contains("MaxLines = 1,", bloque);
            Assert.DoesNotContain("TextWrapping = TextWrapping.Wrap,", bloque);
        }
    }

    /// <summary>
    /// Reads the flat map of a section of the YAML front matter of DESIGN.md.
    /// </summary>
    /// <param name="section">Name of the section, as it is declared in the design.</param>
    /// <returns>The entries of the section, with their raw value.</returns>
    private static Dictionary<string, string> DesignMap(string section)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var inFrontMatter = false;
        var inSection = false;

        foreach (var line in File.ReadAllLines(DesignFile))
        {
            if (line.TrimEnd() == "---")
            {
                if (inFrontMatter)
                {
                    break;
                }

                inFrontMatter = true;
                continue;
            }

            if (!inFrontMatter)
            {
                continue;
            }

            if (line.StartsWith(section + ":", StringComparison.Ordinal))
            {
                inSection = true;
                continue;
            }

            if (!inSection)
            {
                continue;
            }

            // La seccion termina cuando vuelve una clave de primer nivel.
            if (line.Length > 0 && !line.StartsWith(" ", StringComparison.Ordinal))
            {
                break;
            }

            if (!line.StartsWith("  ", StringComparison.Ordinal) || line.StartsWith("   ", StringComparison.Ordinal))
            {
                continue;
            }

            var colon = line.IndexOf(':', StringComparison.Ordinal);

            if (colon <= 0)
            {
                continue;
            }

            var name = line[2..colon].Trim().Trim('\'');
            var value = line[(colon + 1)..].Trim().Trim('\'');

            map[name] = value;
        }

        return map;
    }

    /// <summary>Every colour declared in the token class, with its hexadecimal value.</summary>
    /// <returns>The declared colours.</returns>
    private static IReadOnlyList<(string Name, string Value)> DeclaredColours()
    {
        var text = File.ReadAllText(TokensFile);
        var matches = Regex.Matches(
            text,
            @"public static readonly Color (\w+) = Color\.FromRgb\(0x([0-9A-Fa-f]{2}), 0x([0-9A-Fa-f]{2}), 0x([0-9A-Fa-f]{2})\)");

        return matches
            .Cast<Match>()
            .Select(match => (
                match.Groups[1].Value,
                $"#{match.Groups[2].Value}{match.Groups[3].Value}{match.Groups[4].Value}".ToUpperInvariant()))
            .ToArray();
    }

    /// <summary>Every radius declared in the token class, with its value in pixels.</summary>
    /// <returns>The declared radii.</returns>
    private static IReadOnlyList<(string Name, double Value)> DeclaredRadii()
    {
        var text = File.ReadAllText(TokensFile);
        var matches = Regex.Matches(text, @"public const double (Radius\w+) = (\d+);");

        return matches
            .Cast<Match>()
            .Select(match => (match.Groups[1].Value, double.Parse(match.Groups[2].Value)))
            .ToArray();
    }

    /// <summary>Every spacing declared in the token class, with its value in pixels.</summary>
    /// <returns>The declared spacings.</returns>
    private static IReadOnlyList<(string Name, double Value)> DeclaredSpacings()
    {
        var text = File.ReadAllText(TokensFile);
        var matches = Regex.Matches(text, @"public const double ((?:Separator|Spacing)\w+) = (\d+);");

        return matches
            .Cast<Match>()
            .Select(match => (match.Groups[1].Value, double.Parse(match.Groups[2].Value)))
            .ToArray();
    }
}
