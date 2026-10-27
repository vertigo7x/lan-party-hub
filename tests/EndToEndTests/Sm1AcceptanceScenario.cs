namespace LANPartyHub.EndToEndTests;

/// <summary>
/// The acceptance scenario of the MVP (PRD §9, SM-1), declared as data so that the end to end
/// level states what it will execute and under which precondition (AD-16).
///
/// The scenario runs over a real LAN with two or more clients and without Internet. Story 1.1
/// declares it; Story 13.1 executes it.
/// </summary>
internal static class Sm1AcceptanceScenario
{
    /// <summary>Identifier of the success criterion that this scenario validates (PRD §8).</summary>
    public const string SuccessCriterionId = "SM-1";

    /// <summary>Name of the test level that owns this scenario.</summary>
    public const string Level = "E2E en LAN real";

    /// <summary>Precondition that has to hold before the scenario can run.</summary>
    public const string Precondition =
        "Una LAN real con dos o más clientes compatibles, sin conexión a Internet, " +
        "con el servidor del evento arrancado y con el mismo paquete íntegro para todos.";

    /// <summary>Environment variable that points the scenario to the server of the event.</summary>
    public const string ServerAddressVariable = "LAN_PARTY_HUB_E2E_SERVER";

    /// <summary>The ten steps of the acceptance scenario of the MVP (PRD §9).</summary>
    public static IReadOnlyList<string> Steps { get; } =
    [
        "Arrancar el servidor.",
        "Conectar dos o más clientes compatibles.",
        "Sincronizar el catálogo.",
        "Mostrar un videojuego disponible.",
        "Descargar su paquete desde el servidor.",
        "Validar su integridad y autenticidad.",
        "Instalarlo en modo portable.",
        "Detectar que ya está instalado.",
        "Ejecutarlo desde el cliente.",
        "Mostrar el estado en el servidor.",
    ];

    /// <summary>Tells whether the precondition of the scenario holds in this machine.</summary>
    public static bool PreconditionHolds(out string reason)
    {
        var serverAddress = Environment.GetEnvironmentVariable(ServerAddressVariable);

        if (string.IsNullOrWhiteSpace(serverAddress))
        {
            reason =
                $"The environment variable '{ServerAddressVariable}' is not set: there is no event server " +
                "of a real LAN available for this run.";
            return false;
        }

        reason = string.Empty;
        return true;
    }
}
