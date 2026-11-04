using LANPartyHub.Server.Infrastructure.Networking;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// Reparto equitativo del enlace entre los clientes que descargan a la vez (AD-14).
///
/// <para>
/// La unidad de reparto es el CLIENTE: un cliente que abre varias descargas no puede
/// ocupar más huecos de los que su límite admite, y un cliente con más descargas no
/// puede dejar a los demás clientes sin turno (AD-14).
/// </para>
/// </summary>
public sealed class PerClientShareLimiterTests
{
    [Fact]
    public async Task A_client_takes_a_slot_and_frees_it_when_the_lease_is_disposed()
    {
        var limiter = new PerClientShareLimiter(defaultLimit: 1);

        var lease = await limiter.AcquireAsync("cliente-1", TestContext.Current.CancellationToken);

        lease.Dispose();

        // El hueco vuelve a estar libre, así que otro turno del mismo cliente pasa.
        var second = await limiter.AcquireAsync("cliente-1", TestContext.Current.CancellationToken);

        Assert.NotNull(second);

        second.Dispose();
    }

    [Fact]
    public async Task A_client_cannot_take_more_slots_than_its_limit()
    {
        // AD-14: el límite es POR CLIENTE y acota las respuestas de rango simultáneas.
        var limiter = new PerClientShareLimiter(defaultLimit: 1);

        var lease = await limiter.AcquireAsync("cliente-1", TestContext.Current.CancellationToken);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => limiter.AcquireAsync("cliente-1", cancellation.Token));

        lease.Dispose();
    }

    [Fact]
    public async Task Two_clients_take_their_slots_at_the_same_time()
    {
        // AD-14: el límite de un cliente NO es el límite del otro: dos clientes que
        // descargan a la vez beben de cubos distintos.
        var limiter = new PerClientShareLimiter(defaultLimit: 1);

        var first = await limiter.AcquireAsync("cliente-1", TestContext.Current.CancellationToken);
        var second = await limiter.AcquireAsync("cliente-2", TestContext.Current.CancellationToken);

        Assert.NotNull(first);
        Assert.NotNull(second);

        first.Dispose();
        second.Dispose();
    }

    [Fact]
    public async Task A_second_slot_of_the_same_client_is_available_when_the_limit_is_two()
    {
        var limiter = new PerClientShareLimiter(defaultLimit: 2);

        var first = await limiter.AcquireAsync("cliente-1", TestContext.Current.CancellationToken);
        var second = await limiter.AcquireAsync("cliente-1", TestContext.Current.CancellationToken);

        Assert.NotNull(first);
        Assert.NotNull(second);

        first.Dispose();
        second.Dispose();
    }

    [Fact]
    public void The_default_limit_has_to_be_positive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PerClientShareLimiter(defaultLimit: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PerClientShareLimiter(defaultLimit: -1));
    }

    [Fact]
    public async Task A_disposed_lease_frees_the_slot_only_once()
    {
        // La liberación tiene que ser idempotente: un doble <c>Dispose</c> no puede
        // abrir un hueco de más y dejar pasar más respuestas de las que admite el límite.
        var limiter = new PerClientShareLimiter(defaultLimit: 1);

        var lease = await limiter.AcquireAsync("cliente-1", TestContext.Current.CancellationToken);

        lease.Dispose();
        lease.Dispose();

        var first = await limiter.AcquireAsync("cliente-1", TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        Assert.NotNull(first);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => limiter.AcquireAsync("cliente-1", cancellation.Token));

        first.Dispose();
    }
}
