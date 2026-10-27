using System.Text.RegularExpressions;
using Xunit;

namespace LANPartyHub.EndToEndTests;

/// <summary>
/// End to end level (AD-16): the acceptance scenario of the MVP over a real LAN with two or
/// more clients (PRD §9, SM-1).
///
/// Story 1.1 declares the scenario, its level and its precondition. The execution itself
/// belongs to Story 13.1, which drives the real server and two real clients.
/// </summary>
public sealed class Sm1AcceptanceScenarioTests
{
    [Fact]
    public void The_acceptance_scenario_declares_its_level_and_its_precondition()
    {
        Assert.Equal("SM-1", Sm1AcceptanceScenario.SuccessCriterionId);
        Assert.Equal("E2E en LAN real", Sm1AcceptanceScenario.Level);
        Assert.Contains("LAN real", Sm1AcceptanceScenario.Precondition, StringComparison.Ordinal);
        Assert.Contains("dos o más clientes", Sm1AcceptanceScenario.Precondition, StringComparison.Ordinal);
        Assert.Contains("sin conexión a Internet", Sm1AcceptanceScenario.Precondition, StringComparison.Ordinal);
    }

    [Fact]
    public void The_acceptance_scenario_declares_the_ten_steps_of_the_mvp()
    {
        Assert.Equal(10, Sm1AcceptanceScenario.Steps.Count);
        Assert.Equal("Arrancar el servidor.", Sm1AcceptanceScenario.Steps[0]);
        Assert.Equal("Conectar dos o más clientes compatibles.", Sm1AcceptanceScenario.Steps[1]);
        Assert.Equal("Sincronizar el catálogo.", Sm1AcceptanceScenario.Steps[2]);
        Assert.Equal("Mostrar un videojuego disponible.", Sm1AcceptanceScenario.Steps[3]);
        Assert.Equal("Descargar su paquete desde el servidor.", Sm1AcceptanceScenario.Steps[4]);
        Assert.Equal("Validar su integridad y autenticidad.", Sm1AcceptanceScenario.Steps[5]);
        Assert.Equal("Instalarlo en modo portable.", Sm1AcceptanceScenario.Steps[6]);
        Assert.Equal("Detectar que ya está instalado.", Sm1AcceptanceScenario.Steps[7]);
        Assert.Equal("Ejecutarlo desde el cliente.", Sm1AcceptanceScenario.Steps[8]);
        Assert.Equal("Mostrar el estado en el servidor.", Sm1AcceptanceScenario.Steps[9]);
    }

    [Fact]
    public void The_declared_steps_are_the_steps_of_the_product_requirements()
    {
        var declaredSteps = ExtractAcceptanceScenarioSteps(File.ReadAllText(RepositoryLayout.ProductRequirementsPath));

        Assert.Equal(10, declaredSteps.Count);
        Assert.Equal(Sm1AcceptanceScenario.Steps, declaredSteps);
    }

    [Fact]
    public void The_acceptance_scenario_needs_a_real_lan_with_two_or_more_clients_to_run()
    {
        if (!Sm1AcceptanceScenario.PreconditionHolds(out var reason))
        {
            Assert.Skip(
                $"The precondition of the scenario is not satisfied: {reason} " +
                "The execution over a real LAN belongs to Story 13.1.");
        }

        var serverAddress = Environment.GetEnvironmentVariable(Sm1AcceptanceScenario.ServerAddressVariable);

        Assert.False(string.IsNullOrWhiteSpace(serverAddress));
        Assert.Equal(10, Sm1AcceptanceScenario.Steps.Count);
    }

    /// <summary>
    /// Extracts the numbered steps of the acceptance scenario section of the product
    /// requirements, so the declared scenario cannot drift away from its source.
    /// </summary>
    private static IReadOnlyList<string> ExtractAcceptanceScenarioSteps(string productRequirements)
    {
        var steps = new List<string>();
        var insideSection = false;

        foreach (var rawLine in productRequirements.Split('\n'))
        {
            var line = rawLine.Trim();

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                if (insideSection)
                {
                    break;
                }

                insideSection = line.Contains("Escenario de aceptación del MVP", StringComparison.Ordinal);
                continue;
            }

            if (!insideSection || line.Length == 0)
            {
                continue;
            }

            var match = Regex.Match(line, @"^\d+\.\s+(.+)$", RegexOptions.CultureInvariant);

            if (match.Success)
            {
                steps.Add(match.Groups[1].Value.Trim());
            }
        }

        return steps;
    }
}
