using System.Text.Json;
using LANPartyHub.Client.Host;
using LANPartyHub.Server.Host;
using LANPartyHub.Server.Infrastructure.Http;
using LANPartyHub.Shared.Contracts;
using Xunit;
using ClientProgram = LANPartyHub.Client.Program;

namespace LANPartyHub.IntegrationTests;

/// <summary>
/// Content of the log of the two binaries (AD-15, AD-25).
///
/// <para>
/// The test reads the log that the real server and the real client write and it checks
/// four things: no entry carries a credential, every code that appears is a code of the
/// catalog, the code is never the text that the operator reads first, and the fields of
/// the entries of the product are the known ones.
/// </para>
/// </summary>
[Collection(LogCaptureCollection.Name)]
public sealed class LogContentTests : IDisposable
{
    private const string ClientId = "0193f2a1b2c34d4e5f60718293a4b5c6";

    /// <summary>
    /// Names of the fields that would carry a credential. The comparison is on the whole
    /// name, so a field like "SessionId" of the framework is not a false positive.
    /// </summary>
    private static readonly string[] CredentialFieldNames =
    [
        "password",
        "passwd",
        "pwd",
        "secret",
        "secretkey",
        "privatekey",
        "credential",
        "credentials",
        "apikey",
        "api_key",
        "authorization",
        "bearer",
        "cookie",
        "setcookie",
        "token",
        "accesstoken",
        "access_token",
        "refreshtoken",
        "sessiontoken",
    ];

    /// <summary>
    /// Fields that the entries of the product are allowed to carry. Every field of the
    /// state of an entry of the product has to be one of them.
    /// </summary>
    private static readonly string[] KnownStateFields =
    [
        "{OriginalFormat}",
        "Message",
        "DataDirectory",
        "HttpPort",
        "StatusCode",
        "code",
        "ServiceType",
        "Port",
        "InstanceName",
        "DataFolder",
        "InstallDirectory",
        "Theme",
        "ClientId",
        "OperationId",
        "TransferEngine",
        "Reason",
        "PersistedOffset",
        "EffectiveOffset",
    ];

    /// <summary>
    /// Properties that the entry of the log publishes at its first level. They are the ones
    /// of the JSON formatter, and nothing else.
    /// </summary>
    private static readonly string[] KnownEntryProperties =
    [
        "Timestamp",
        "EventId",
        "LogLevel",
        "Category",
        "Message",
        "State",
        "Scopes",
        "Exception",
    ];

    private readonly List<string> _temporaryFolders = [];

    [Fact]
    public async Task No_entry_of_the_log_carries_a_credential()
    {
        var entries = await ReadTheLogOfBothBinariesAsync();

        Assert.NotEmpty(entries);

        foreach (var entry in entries)
        {
            foreach (var field in LogEntries.StateFields(entry).Concat(LogEntries.ScopeFields(entry)))
            {
                Assert.False(
                    CredentialFieldNames.Contains(field.Name, StringComparer.OrdinalIgnoreCase),
                    $"The entry carries a field that looks like a credential: {field.Name}.");
            }

            foreach (var value in Values(entry))
            {
                Assert.False(
                    value.Contains("Bearer ", StringComparison.OrdinalIgnoreCase),
                    $"The entry carries a value that looks like a credential: {value}.");
            }
        }
    }

    [Fact]
    public async Task Every_code_that_appears_in_the_log_is_a_code_of_the_catalog()
    {
        var entries = await ReadTheLogOfBothBinariesAsync();

        var codes = new List<string>();

        foreach (var entry in entries.Where(LogEntries.IsProduct))
        {
            foreach (var field in LogEntries.StateFields(entry))
            {
                if (field.Name == "code")
                {
                    codes.Add(field.Value.GetString() ?? string.Empty);
                }
            }
        }

        Assert.Contains(ErrorCatalog.ClientIdMissingCode, codes);

        foreach (var code in codes)
        {
            Assert.Contains(code, ErrorCatalog.Codes);
        }
    }

    [Fact]
    public async Task The_code_of_an_error_is_never_the_text_of_the_entry()
    {
        var entries = await ReadTheLogOfBothBinariesAsync();

        foreach (var entry in entries.Where(LogEntries.IsProduct))
        {
            var message = entry.GetProperty("Message").GetString() ?? string.Empty;

            foreach (var field in LogEntries.StateFields(entry))
            {
                if (field.Name != "code")
                {
                    continue;
                }

                // The code travels in the structured field and not in the text that the
                // operator reads first (AD-15, AD-25).
                var code = field.Value.GetString() ?? string.Empty;

                Assert.DoesNotContain(code, message);

                if (field.Value.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                foreach (var other in LogEntries.StateFields(entry))
                {
                    if (other.Name == "code" || other.Value.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    Assert.DoesNotContain(code, other.Value.GetString() ?? string.Empty);
                }
            }
        }
    }

    [Fact]
    public async Task The_fields_of_the_entries_of_the_product_are_the_known_ones()
    {
        var entries = await ReadTheLogOfBothBinariesAsync();

        var productEntries = entries.Where(LogEntries.IsProduct).ToArray();

        Assert.NotEmpty(productEntries);

        foreach (var entry in productEntries)
        {
            foreach (var field in LogEntries.StateFields(entry))
            {
                Assert.Contains(field.Name, KnownStateFields);
            }
        }
    }

    [Fact]
    public async Task The_properties_of_every_entry_are_the_ones_of_the_json_formatter()
    {
        var entries = await ReadTheLogOfBothBinariesAsync();

        Assert.NotEmpty(entries);

        foreach (var entry in entries)
        {
            foreach (var property in entry.EnumerateObject())
            {
                Assert.Contains(property.Name, KnownEntryProperties);
            }
        }
    }

    [Fact]
    public async Task The_scope_of_the_correlation_only_carries_the_two_identifiers()
    {
        var entries = await ReadTheLogOfBothBinariesAsync();

        var scopes = entries
            .SelectMany(LogEntries.Scopes)
            .Where(scope => scope.TryGetProperty("ClientId", out _))
            .ToArray();

        Assert.NotEmpty(scopes);

        foreach (var scope in scopes)
        {
            foreach (var field in scope.EnumerateObject())
            {
                Assert.Contains(field.Name, new[] { "Message", "ClientId", "OperationId" });
            }
        }
    }

    /// <summary>
    /// Reads the log of the two binaries: the server, with a request of the client and a
    /// request without the identifier of the client, and the client, with its startup.
    /// </summary>
    private async Task<IReadOnlyList<JsonElement>> ReadTheLogOfBothBinariesAsync()
    {
        await using var capture = await ConsoleLogCapture.StartAsync();

        await using (var host = await TestServer.StartAsync(TestContext.Current.CancellationToken))
        {
            using var client = host.CreateClient();

            using var correlated = new HttpRequestMessage(HttpMethod.Get, "/api/v1/catalog");
            correlated.Headers.Add(CorrelationMiddleware.ClientIdHeaderName, ClientId);

            using var correlatedResponse = await client.SendAsync(correlated, TestContext.Current.CancellationToken);

            using var rejected = await client.GetAsync("/api/v1/catalog", TestContext.Current.CancellationToken);

            using var metrics = await client.GetAsync(MetricsEndpoint.Route, TestContext.Current.CancellationToken);
        }

        var dataFolder = CreateTemporaryDataFolder("lan-party-hub-log-content-");
        var deploymentFolder = CreateTemporaryDataFolder("lan-party-hub-log-content-deployment-");

        using (var factory = ClientProgram.CreateLoggerFactory(deploymentFolder))
        using (var correlation = new ClientCorrelationFactory(factory))
        {
            await ClientProgram.StartClientAsync(
                [],
                correlation,
                factory,
                dataFolder: dataFolder,
                cancellationToken: TestContext.Current.CancellationToken);
        }

        return capture.Entries;
    }

    /// <summary>
    /// Every value of text that the entry carries: the fields of its state, the fields of
    /// its scopes and its own text.
    /// </summary>
    private static IEnumerable<string> Values(JsonElement entry)
    {
        foreach (var field in LogEntries.StateFields(entry).Concat(LogEntries.ScopeFields(entry)))
        {
            if (field.Value.ValueKind == JsonValueKind.String)
            {
                yield return field.Value.GetString() ?? string.Empty;
            }
        }

        foreach (var name in new[] { "Message", "Exception" })
        {
            if (entry.TryGetProperty(name, out var property)
                && property.ValueKind == JsonValueKind.String)
            {
                yield return property.GetString() ?? string.Empty;
            }
        }
    }

    private string CreateTemporaryDataFolder(string prefix)
    {
        var folder = Directory.CreateTempSubdirectory(prefix);

        _temporaryFolders.Add(folder.FullName);

        return folder.FullName;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var folder in _temporaryFolders)
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
                // A file that the operating system still holds does not fail the test.
            }
        }
    }
}
