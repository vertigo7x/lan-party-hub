using System.Text.Json;
using LANPartyHub.Client.Application;
using LANPartyHub.Client.Host;
using LANPartyHub.Client.Infrastructure.Logging;
using Xunit;
using ClientProgram = LANPartyHub.Client.Program;

namespace LANPartyHub.IntegrationTests;

/// <summary>
/// Log of the real client (AD-15, AD-23).
///
/// <para>
/// The test starts the real client over a temporary data folder, with the real factory of
/// the log, because the shape of the entry and the correlation of the client are only
/// observable in the log that the product really writes.
/// </para>
/// </summary>
[Collection(LogCaptureCollection.Name)]
public sealed class ClientLogTests : IDisposable
{
    private static readonly string[] KnownScopeFields = ["Message", "ClientId", "OperationId"];

    private readonly List<string> _temporaryFolders = [];

    [Fact]
    public async Task The_log_of_the_client_is_json_and_carries_its_client_id()
    {
        await using var capture = await ConsoleLogCapture.StartAsync();

        var dataFolder = CreateTemporaryDataFolder("lan-party-hub-client-log-");
        var deploymentFolder = CreateTemporaryDataFolder("lan-party-hub-client-log-deployment-");

        ClientStartupResult startup;

        using (var factory = ClientProgram.CreateLoggerFactory(deploymentFolder))
        using (var correlation = new ClientCorrelationFactory(factory))
        {
            startup = await ClientProgram.StartClientAsync(
                [],
                correlation,
                factory,
                dataFolder: dataFolder,
                cancellationToken: TestContext.Current.CancellationToken);
        }

        // The factory is closed, so the log has been written completely.
        var entries = capture.Entries;

        Assert.NotEmpty(entries);

        // Every entry is a JSON entry of the shape that the formatter publishes.
        foreach (var entry in entries)
        {
            Assert.Equal(JsonValueKind.Object, entry.ValueKind);
            Assert.True(entry.TryGetProperty("Timestamp", out _));
            Assert.True(entry.TryGetProperty("LogLevel", out _));
            Assert.True(entry.TryGetProperty("Category", out _));
            Assert.True(entry.TryGetProperty("Message", out _));
        }

        // The scope of the correlation carries the identifier of the team and the one of the
        // unit of work, and nothing else.
        var scopes = entries
            .SelectMany(LogEntries.Scopes)
            .Where(scope => scope.TryGetProperty("ClientId", out _))
            .ToArray();

        Assert.NotEmpty(scopes);

        foreach (var scope in scopes)
        {
            Assert.Equal(startup.Identity.CanonicalId, scope.GetProperty("ClientId").GetString());
            Assert.False(string.IsNullOrWhiteSpace(scope.GetProperty("OperationId").GetString()));

            foreach (var field in scope.EnumerateObject())
            {
                Assert.Contains(field.Name, KnownScopeFields);
            }
        }

        // The client opens a single scope: the one of the correlation.
        foreach (var entry in entries)
        {
            var entryScopes = entry.GetProperty("Scopes").EnumerateArray().ToArray();

            Assert.True(entryScopes.Length <= 1);
        }
    }

    [Fact]
    public async Task The_log_of_the_client_is_also_written_in_a_file_next_to_the_deployment()
    {
        var dataFolder = CreateTemporaryDataFolder("lan-party-hub-client-log-");
        var deploymentFolder = CreateTemporaryDataFolder("lan-party-hub-client-log-deployment-");

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

        var path = Path.Combine(deploymentFolder, JsonFileLoggerProvider.FileName);

        Assert.True(File.Exists(path), "The client did not write its log in the file of the deployment.");

        var lines = File.ReadAllLines(path);

        Assert.NotEmpty(lines);

        // The file carries the same JSON entries that the console publishes.
        using var document = JsonDocument.Parse(lines[0]);

        Assert.True(document.RootElement.TryGetProperty("Message", out _));
        Assert.True(document.RootElement.TryGetProperty("Timestamp", out _));
        Assert.True(document.RootElement.TryGetProperty("Scopes", out _));
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
