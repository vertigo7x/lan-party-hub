using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Infrastructure.Networking;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// Closed registry of the transfer engines (AD-4, AC4, AD-24, AD-28).
///
/// <para>
/// The registry is CLOSED: the client admits exactly one value, the one that names the only
/// implementation of the port. The checks observe the real assembly, so they see the
/// implementations that the client really ships.
/// </para>
/// </summary>
public sealed class TransferEngineRegistryTests
{
    [Fact]
    public void The_only_engine_is_named_exactly_after_its_implementation()
    {
        // AC4: el valor persistido es EXACTAMENTE el identificador de la única
        // implementación del puerto.
        Assert.Equal("HttpRangeTransferEngine", TransferEngineRegistry.HttpRangeTransferEngine);

        Assert.Equal(
            TransferEngineRegistry.HttpRangeTransferEngine,
            typeof(HttpRangeTransferEngine).Name);
    }

    [Fact]
    public void The_registry_holds_exactly_one_engine()
    {
        var known = TransferEngineRegistry.Known;

        Assert.Single(known);
        Assert.Equal(
            TransferEngineRegistry.HttpRangeTransferEngine,
            known[0]);
    }

    [Fact]
    public void The_registry_holds_the_same_value_that_the_only_implementation_persists()
    {
        // La columna `transferEngine` de `DOWNLOAD_OPERATION` guarda el identificador de la
        // única implementación, así que el valor del registro y el nombre del tipo no
        // pueden divergir.
        var implementations = typeof(ITransferEngine).Assembly
            .GetTypes()
            .Where(type => type is { IsAbstract: false, IsInterface: false }
                && typeof(ITransferEngine).IsAssignableFrom(type))
            .ToArray();

        Assert.Single(implementations);
        Assert.Equal(
            TransferEngineRegistry.HttpRangeTransferEngine,
            implementations[0].Name);
    }

    [Fact]
    public void The_active_engine_is_the_only_one_that_the_registry_knows()
    {
        Assert.True(TransferEngineRegistry.IsActive(TransferEngineRegistry.HttpRangeTransferEngine));
    }

    [Fact]
    public void The_registry_rejects_every_unknown_value()
    {
        // AD-24: un registro cuyo `transferEngine` no existe en la versión activa se
        // rechaza en el arranque, en lugar de reinterpretarse.
        Assert.False(TransferEngineRegistry.IsActive(null));
        Assert.False(TransferEngineRegistry.IsActive(string.Empty));
        Assert.False(TransferEngineRegistry.IsActive("   "));
        Assert.False(TransferEngineRegistry.IsActive("HttpRange"));
        Assert.False(TransferEngineRegistry.IsActive("Torrent"));
        Assert.False(TransferEngineRegistry.IsActive("BitTorrentTransferEngine"));
        Assert.False(TransferEngineRegistry.IsActive("httprangetransferengine"));
        Assert.False(TransferEngineRegistry.IsActive("HttpRangeTransferEngine "));
    }

    [Fact]
    public void The_registry_does_not_accept_a_value_that_differs_only_in_the_case()
    {
        // El valor persistido es EXACTO: no hay alias ni reinterpretación.
        Assert.False(TransferEngineRegistry.IsActive("httpRangeTransferEngine"));
        Assert.False(TransferEngineRegistry.IsActive("HTTPRANGETRANSFERENGINE"));
    }
}
