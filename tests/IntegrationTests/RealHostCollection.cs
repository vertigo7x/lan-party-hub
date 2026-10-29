using Xunit;

namespace LANPartyHub.IntegrationTests;

/// <summary>
/// Collection of the tests that start the real host against a real SQLite file
/// (AD-16).
///
/// <para>
/// The tests of the catalog of the videogames start the real host, with the real Kestrel
/// and the real sockets, y eso abre listeners en el proceso del test runner. Los tests
/// que observan la tabla de sockets del proceso para afirmar que el servidor solo abre
/// su puerto HTTP tienen que leerla sin que otro test esté abriendo listeners a la vez,
/// de modo que esta colección no corre en paralelo con ninguna otra.
/// </para>
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RealHostCollection
{
    /// <summary>Name of the collection.</summary>
    public const string Name = "Host real";
}
