using Xunit;

namespace LANPartyHub.IntegrationTests;

/// <summary>
/// Collection of the tests that read the table of sockets of the test runner process to
/// assert the network surface of the server (AD-16).
///
/// <para>
/// Los tests de esta clase miden los listeners y los sockets UDP del proceso del runner
/// con un diff antes y después del arranque. Si otra clase levanta un servidor a la vez,
/// ese servidor abre listeners en el mismo proceso y el diff los atribuye al servidor
/// bajo prueba, así que la aserción falla de forma intermitente. Por eso esta colección
/// no corre en paralelo con ninguna otra, sin quitar el paralelismo al resto de la suite.
/// </para>
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ServerStartupSocketIsolationCollection
{
    /// <summary>Name of the collection.</summary>
    public const string Name = "Server startup socket isolation";
}
