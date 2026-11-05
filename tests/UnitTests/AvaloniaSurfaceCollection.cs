using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The COLLECTION of the tests that touch Avalonia, para que NO se ejecuten en paralelo
/// (AC2, UX-DR-37).
///
/// <para>
/// La plataforma headless de Avalonia tiene UN dispatcher con un hilo propio, y las
/// pruebas que construyen superficies de Avalonia lo usan desde el hilo que las ejecuta:
/// si dos de ellas corren a la vez, una toca un control desde el hilo que no lo posee y
/// la prueba falla con «the calling thread cannot access this object» (AC2, UX-DR-37).
/// </para>
/// <para>
/// La colección NO desactiva el paralelismo del ENSEMBLADO, así que el resto de las
/// pruebas siguen corriendo en paralelo (AD-16).
/// </para>
/// </summary>
[CollectionDefinition(AvaloniaSurfaceCollection.Name, DisableParallelization = true)]
public sealed class AvaloniaSurfaceCollection
{
    /// <summary>The name of the collection of the tests that touch Avalonia.</summary>
    public const string Name = "AvaloniaSurfaces";
}
