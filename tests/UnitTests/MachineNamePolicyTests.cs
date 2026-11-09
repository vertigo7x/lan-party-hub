using LANPartyHub.Server.Domain.Clients;
using System.Text;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The rules of the human label of the team (AD-30, AD-31).
///
/// <para>
/// Las reglas son puras y se ejercitan sin servidor y sin red: la longitud se mide en
/// PUNTOS DE CÓDIGO, la comparación de unicidad es por forma canónica y la derivación
/// del nombre efectivo es una función pura de (<c>stem</c>, <c>clientId</c>) que nunca
/// busca nombres libres (AD-30, AD-31).
/// </para>
/// </summary>
public sealed class MachineNamePolicyTests
{
    private const string ClientId = "0193f2a1b2c34d4e5f60718293a4b5c6";

    [Fact]
    public void A_name_that_satisfies_every_rule_of_shape_is_valid()
    {
        Assert.True(MachineNamePolicy.IsValid("EQ-PC"));
        Assert.True(MachineNamePolicy.IsValid("equipo_1.local"));
        Assert.True(MachineNamePolicy.IsValid("Sala 2"));
        Assert.True(MachineNamePolicy.IsValid("ñandú-2"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_name_that_is_empty_once_trimmed_is_not_valid(string? name)
    {
        Assert.False(MachineNamePolicy.IsValid(name));
    }

    [Fact]
    public void A_name_longer_than_sixty_four_code_points_is_not_valid()
    {
        Assert.True(MachineNamePolicy.IsValid(new string('a', 64)));
        Assert.False(MachineNamePolicy.IsValid(new string('a', 65)));
    }

    [Fact]
    public void The_length_is_measured_in_code_points_and_not_in_utf16_code_units()
    {
        // 32 ideogramas CJK ocupan 32 puntos de código y 64 unidades UTF-16: el
        // nombre ADMITE 64 ideogramas y NO admite 65, aunque mida lo mismo en UTF-16
        // (AD-30).
        var sixtyFour = new string('漢', 64);

        Assert.True(MachineNamePolicy.IsValid(sixtyFour));
        Assert.False(MachineNamePolicy.IsValid(new string('漢', 65)));
    }

    [Fact]
    public void A_name_with_a_control_character_is_not_valid()
    {
        Assert.False(MachineNamePolicy.IsValid("EQ-PC\n"));
        Assert.False(MachineNamePolicy.IsValid("EQ\tPC"));
        Assert.False(MachineNamePolicy.IsValid("EQ-PC\0"));
    }

    [Fact]
    public void A_name_with_a_leading_or_trailing_space_is_not_valid()
    {
        Assert.False(MachineNamePolicy.IsValid(" EQ-PC"));
        Assert.False(MachineNamePolicy.IsValid("EQ-PC "));
    }

    [Fact]
    public void A_name_with_consecutive_spaces_is_not_valid()
    {
        Assert.False(MachineNamePolicy.IsValid("EQ  PC"));
        Assert.False(MachineNamePolicy.IsValid("Sala  2"));
    }

    [Theory]
    [InlineData("EQ/PC")]
    [InlineData("EQ:PC")]
    [InlineData("EQ;PC")]
    [InlineData("EQ\\PC")]
    [InlineData("EQ+PC")]
    [InlineData("EQ*PC")]
    [InlineData("EQ!PC")]
    public void A_name_with_a_character_outside_the_admitted_set_is_not_valid(string name)
    {
        Assert.False(MachineNamePolicy.IsValid(name));
    }

    [Fact]
    public void The_canonical_form_collapses_the_case_and_the_diacritical_marks()
    {
        Assert.Equal("eq-pc", MachineNamePolicy.Canonicalize("EQ-PC"));
        Assert.Equal("eq-pc", MachineNamePolicy.Canonicalize("eq-pc"));
        Assert.Equal("eqpc", MachineNamePolicy.Canonicalize("eqpc"));
        Assert.Equal("equipo", MachineNamePolicy.Canonicalize("ÉQUIPO"));
    }

    [Fact]
    public void The_canonical_form_collapses_the_compatibility_variants()
    {
        // NFKC colapsa las variantes de COMPATIBILIDAD: la A de ancho completo es la
        // misma letra que la A, así que los dos nombres son el MISMO nombre (AD-30).
        Assert.Equal(
            MachineNamePolicy.Canonicalize("eq-pc"),
            MachineNamePolicy.Canonicalize("eq－pc"));
        Assert.Equal(
            MachineNamePolicy.Canonicalize("sala2"),
            MachineNamePolicy.Canonicalize("sala²"));
    }

    [Fact]
    public void The_canonical_form_keeps_the_hexadecimal_tail_of_a_derived_name_intact()
    {
        // AD-31: las tres operaciones de la forma canónica son la IDENTIDAD sobre el
        // guion y sobre los dígitos hexadecimales en minúsculas, así que la cola de 33
        // puntos de código sobrevive intacta.
        var derived = MachineNamePolicy.Derive("EQ-PC", ClientId);

        var canonical = MachineNamePolicy.Canonicalize(derived);

        Assert.EndsWith($"-{ClientId}", canonical, StringComparison.Ordinal);
        Assert.Equal(33, canonical.EnumerateRunes().Count() - canonical[..^33].EnumerateRunes().Count());
    }

    [Fact]
    public void A_derived_name_carries_the_fixed_suffix_of_the_client_identifier()
    {
        var derived = MachineNamePolicy.Derive("EQ-PC", ClientId);

        Assert.Equal($"EQ-PC-{ClientId}", derived);
    }

    [Fact]
    public void A_derived_name_never_exceeds_sixty_four_code_points()
    {
        var stem = new string('a', 64);

        var derived = MachineNamePolicy.Derive(stem, ClientId);

        Assert.Equal(64, derived.EnumerateRunes().Count());
    }

    [Fact]
    public void A_derived_name_trims_the_stem_to_thirty_one_code_points()
    {
        var stem = new string('a', 64);

        var derived = MachineNamePolicy.Derive(stem, ClientId);

        Assert.StartsWith(new string('a', 31), derived, StringComparison.Ordinal);
    }

    [Fact]
    public void A_derived_name_trims_a_non_latin_stem_by_code_points_and_never_splits_it()
    {
        // Un stem de 64 ideogramas CJK se recorta a 31 ideogramas, NO a 21: la longitud
        // se mide en puntos de código (AD-31).
        var stem = new string('漢', 64);

        var derived = MachineNamePolicy.Derive(stem, ClientId);

        Assert.Equal(64, derived.EnumerateRunes().Count());

        var stemOfDerived = derived[..^33];

        Assert.Equal(31, stemOfDerived.EnumerateRunes().Count());
        Assert.Equal(31, stemOfDerived.Count(character => character == '漢'));
    }

    [Fact]
    public void A_derived_name_never_splits_an_emoji()
    {
        // Un emoji de varias unidades UTF-16 no se parte por la mitad: el corte cae
        // siempre en un límite de punto de código (AD-31).
        const string Emoji = "😀";

        var stem = string.Concat(Enumerable.Repeat(Emoji, 64));

        var derived = MachineNamePolicy.Derive(stem, ClientId);

        // La longitud se mide en PUNTOS DE CÓDIGO, así que el emoji cuenta por uno y el
        // nombre efectivo queda en 31 + 33 = 64 puntos de código (AD-31).
        Assert.Equal(64, derived.EnumerateRunes().Count());

        var stemOfDerived = derived[..^33];

        Assert.Equal(31, stemOfDerived.EnumerateRunes().Count());
        Assert.StartsWith(
            string.Concat(Enumerable.Repeat(Emoji, 31)),
            derived,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_derived_name_uses_the_reserve_stem_when_the_trim_would_empty_it()
    {
        var derived = MachineNamePolicy.Derive(string.Empty, ClientId);

        Assert.Equal($"equipo-{ClientId}", derived);

        // El stem de reserva «equipo» tiene 6 puntos de código y el sufijo 33, así que
        // el nombre efectivo queda en 39 (AD-31).
        Assert.Equal(39, derived.EnumerateRunes().Count());
    }

    [Fact]
    public void A_derived_name_is_a_pure_function_of_the_stem_and_the_client_identifier()
    {
        var first = MachineNamePolicy.Derive("EQ-PC", ClientId);
        var second = MachineNamePolicy.Derive("EQ-PC", ClientId);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Two_different_clients_never_receive_the_same_derived_name()
    {
        var other = "0193f2a1b2c34d4e5f60718293a4b5c7";

        var first = MachineNamePolicy.Derive("EQ-PC", ClientId);
        var second = MachineNamePolicy.Derive("EQ-PC", other);

        Assert.NotEqual(first, second);
        Assert.NotEqual(
            MachineNamePolicy.Canonicalize(first),
            MachineNamePolicy.Canonicalize(second));
    }

    [Fact]
    public void A_derived_name_satisfies_the_rules_of_shape_again()
    {
        // AD-31: el nombre efectivo derivado SIEMPRE es admisible por las mismas
        // reglas que el propuesto, así que el servidor nunca rechaza un nombre que él
        // mismo acaba de derivar.
        var stem = new string('a', 64);

        var derived = MachineNamePolicy.Derive(stem, ClientId);

        Assert.True(MachineNamePolicy.IsValid(derived));
    }

    [Fact]
    public void A_derived_name_of_a_non_latin_stem_satisfies_the_rules_of_shape_again()
    {
        var derived = MachineNamePolicy.Derive(new string('漢', 64), ClientId);

        Assert.True(MachineNamePolicy.IsValid(derived));
    }
}
