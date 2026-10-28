using LANPartyHub.Server.Host;
using LANPartyHub.Server.Infrastructure.Http;
using LANPartyHub.Server.Infrastructure.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// Correlation of the requests of the client (AD-15, AD-23). The unit of work is counted
/// while the request is being served, and it stops being counted when it ends.
/// </summary>
public sealed class CorrelationMiddlewareTests
{
    private const string ClientId = "0193f2a1b2c34d4e5f60718293a4b5c6";

    [Fact]
    public async Task The_unit_of_work_is_counted_while_the_request_is_in_flight()
    {
        using var loggerFactory = LoggerFactory.Create(logging => logging.SetMinimumLevel(LogLevel.Information));

        var metrics = new HostMetrics();
        var gate = new TaskCompletionSource();
        var middleware = CreateMiddleware(_ => gate.Task, metrics, loggerFactory);

        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationMiddleware.ClientIdHeaderName] = ClientId;

        var serving = middleware.InvokeAsync(context);

        // The request is being served: the unit of work is counted.
        Assert.Equal(1, metrics.ActiveOperations);

        gate.SetResult();

        await serving;

        // The request has ended: the unit of work is not counted anymore.
        Assert.Equal(0, metrics.ActiveOperations);
    }

    [Fact]
    public async Task A_rejected_request_is_a_unit_of_work_served()
    {
        using var loggerFactory = LoggerFactory.Create(logging => logging.SetMinimumLevel(LogLevel.Information));

        var metrics = new HostMetrics();
        var middleware = CreateMiddleware(_ => Task.CompletedTask, metrics, loggerFactory);

        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context);

        // The rejection is served, so it is counted while it runs and released when it
        // ends.
        Assert.Equal(0, metrics.ActiveOperations);
        Assert.Single(metrics.ErrorsByCode);
    }

    [Fact]
    public async Task The_stream_of_a_middleware_that_runs_after_is_not_discarded_in_silence()
    {
        using var loggerFactory = LoggerFactory.Create(logging => logging.SetMinimumLevel(LogLevel.Information));

        var metrics = new HostMetrics();
        var installed = new MemoryStream();

        var middleware = CreateMiddleware(
            context =>
            {
                // A middleware that runs after this one replaces the stream of the
                // response.
                context.Response.Body = installed;

                return Task.CompletedTask;
            },
            metrics,
            loggerFactory);

        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationMiddleware.ClientIdHeaderName] = ClientId;

        var original = context.Response.Body;

        await middleware.InvokeAsync(context);

        // The original stream is restored and the one of the downstream is closed, so its
        // content is not lost in silence.
        Assert.Same(original, context.Response.Body);
        Assert.False(installed.CanWrite);
    }

    private static CorrelationMiddleware CreateMiddleware(
        RequestDelegate next,
        HostMetrics metrics,
        ILoggerFactory loggerFactory) =>
        new(
            next,
            new CorrelationScopeFactory(loggerFactory),
            metrics,
            loggerFactory.CreateLogger<CorrelationMiddleware>());
}
