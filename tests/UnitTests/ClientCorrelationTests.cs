using System.Text.Json;
using LANPartyHub.Client.Host;
using Microsoft.Extensions.Logging;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// Correlation scope of the client (AD-15, AD-23). The scope carries the technical identity
/// of the team and the identifier of the unit of work, and the JSON entry that the log
/// writes carries both.
/// </summary>
[Collection(LogCaptureCollection.Name)]
public sealed class ClientCorrelationTests
{
    private const string ClientId = "0193f2a1b2c34d4e5f60718293a4b5c6";

    private const string OperationId = "9f2c1d3e4f5a6b7c8d9e0f1a2b3c4d5e";

    private static readonly string[] KnownScopeFields = ["Message", "ClientId", "OperationId"];

    [Fact]
    public void The_scope_carries_the_client_and_the_operation()
    {
        var correlation = ClientCorrelation.Create(ClientId, OperationId);

        Assert.Equal(ClientId, correlation.ClientId);
        Assert.Equal(OperationId, correlation.OperationId);
        Assert.Equal(ClientId, Field(correlation, ClientCorrelation.ClientIdFieldName));
        Assert.Equal(OperationId, Field(correlation, ClientCorrelation.OperationIdFieldName));
    }

    [Fact]
    public void The_scope_of_a_client_without_an_operation_carries_only_the_client()
    {
        var correlation = ClientCorrelation.Create(ClientId);

        Assert.Equal(ClientId, correlation.ClientId);
        Assert.Null(correlation.OperationId);
        Assert.Equal(ClientId, Field(correlation, ClientCorrelation.ClientIdFieldName));
        Assert.Null(Field(correlation, ClientCorrelation.OperationIdFieldName));
    }

    [Fact]
    public void An_empty_client_identifier_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => ClientCorrelation.Create(string.Empty));
        Assert.Throws<ArgumentException>(() => ClientCorrelation.Create("   "));
    }

    [Fact]
    public void An_empty_operation_identifier_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => ClientCorrelation.Create(ClientId, "  "));
    }

    [Fact]
    public void The_scope_exposes_the_identifiers_of_the_unit_of_work_in_progress()
    {
        using var factory = LoggerFactory.Create(logging => logging.SetMinimumLevel(LogLevel.Information));
        var correlation = new ClientCorrelationFactory(factory);

        Assert.Null(correlation.ClientId);
        Assert.Null(correlation.OperationId);

        correlation.Open(ClientId, OperationId);

        Assert.Equal(ClientId, correlation.ClientId);
        Assert.Equal(OperationId, correlation.OperationId);
    }

    [Fact]
    public async Task The_json_entry_carries_the_client_and_the_operation()
    {
        await using var capture = await ConsoleLogCapture.StartAsync();

        using (var factory = CreateLoggerFactory())
        {
            var correlation = new ClientCorrelationFactory(factory);

            correlation.Open(ClientId, OperationId);

            factory.CreateLogger("LANPartyHub.Client.Test").LogInformation("Entrada de prueba.");
        }

        var entries = capture.Entries;

        Assert.NotEmpty(entries);

        var scopes = entries.SelectMany(CorrelationScopes).ToArray();

        Assert.NotEmpty(scopes);

        foreach (var scope in scopes)
        {
            Assert.Equal(ClientId, scope.GetProperty("ClientId").GetString());
            Assert.Equal(OperationId, scope.GetProperty("OperationId").GetString());

            // The scope carries the two identifiers and nothing else: no credential can
            // travel in it.
            foreach (var field in scope.EnumerateObject())
            {
                Assert.Contains(field.Name, KnownScopeFields);
            }
        }
    }

    [Fact]
    public async Task The_scope_of_the_client_is_closed_when_the_scope_is_disposed()
    {
        await using var capture = await ConsoleLogCapture.StartAsync();

        using (var factory = CreateLoggerFactory())
        {
            var correlation = new ClientCorrelationFactory(factory);

            correlation.Open(ClientId, OperationId);

            factory.CreateLogger("LANPartyHub.Client.Test").LogInformation("Dentro del ámbito.");

            correlation.Dispose();

            factory.CreateLogger("LANPartyHub.Client.Test").LogInformation("Fuera del ámbito.");
        }

        var entries = capture.Entries;

        Assert.NotEmpty(entries);

        var scoped = entries.Where(entry => CorrelationScopes(entry).Any()).ToArray();
        var unscoped = entries.Where(entry => !CorrelationScopes(entry).Any()).ToArray();
        // The entries written while the scope was open carry it, and the ones written
        // after it was closed do not.
        Assert.NotEmpty(scoped);
        Assert.NotEmpty(unscoped);

        foreach (var entry in scoped)
        {
            Assert.Single(entry.GetProperty("Scopes").EnumerateArray());
        }
    }

    private static ILoggerFactory CreateLoggerFactory() =>
        LoggerFactory.Create(logging =>
        {
            logging.ClearProviders();
            logging.AddJsonConsole(options =>
            {
                options.IncludeScopes = true;
                options.TimestampFormat = "O";
                options.UseUtcTimestamp = true;
            });
            logging.SetMinimumLevel(LogLevel.Information);
        });

    /// <summary>
    /// The scopes of an entry that carry the correlation of the client.
    /// </summary>
    private static IEnumerable<JsonElement> CorrelationScopes(JsonElement entry)
    {
        if (!entry.TryGetProperty("Scopes", out var scopes) || scopes.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var scope in scopes.EnumerateArray())
        {
            if (scope.ValueKind == JsonValueKind.Object && scope.TryGetProperty("ClientId", out _))
            {
                yield return scope;
            }
        }
    }

    private static object? Field(IReadOnlyList<KeyValuePair<string, object?>> scope, string name)
    {
        foreach (var field in scope)
        {
            if (field.Key == name)
            {
                return field.Value;
            }
        }

        return null;
    }
}
