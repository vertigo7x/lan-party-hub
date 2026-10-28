using System.Text.Json;
using LANPartyHub.Client.Infrastructure.Logging;
using Microsoft.Extensions.Logging;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// File of the log of the client (AD-15). The client is a desktop application and it has no
/// console when the player launches it from the desktop, so the log is also written in a
/// JSON file next to the deployment, and the file cannot grow without bound.
/// </summary>
public sealed class JsonFileLoggerProviderTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("lan-party-hub-client-log-").FullName;

    [Fact]
    public void The_entries_are_written_in_the_file_as_json()
    {
        var path = Path.Combine(_folder, JsonFileLoggerProvider.FileName);

        using (var factory = CreateLoggerFactory(path))
        {
            factory.CreateLogger("LANPartyHub.Client.Test").LogInformation("Entrada de prueba {Value}", 42);
        }

        var lines = File.ReadAllLines(path);

        Assert.NotEmpty(lines);

        using var document = JsonDocument.Parse(lines[0]);
        var entry = document.RootElement;

        Assert.Equal("Entrada de prueba 42", entry.GetProperty("Message").GetString());
        Assert.Equal("Information", entry.GetProperty("LogLevel").GetString());
        Assert.Equal("LANPartyHub.Client.Test", entry.GetProperty("Category").GetString());
        Assert.Equal(42, entry.GetProperty("State").GetProperty("Value").GetInt32());
        Assert.True(entry.TryGetProperty("Timestamp", out _));
    }

    [Fact]
    public void The_file_is_rotated_when_it_reaches_its_maximum_size()
    {
        var path = Path.Combine(_folder, JsonFileLoggerProvider.FileName);

        using (var factory = CreateLoggerFactory(path, maximumSizeInBytes: 256))
        {
            for (var index = 0; index < 20; index++)
            {
                factory.CreateLogger("LANPartyHub.Client.Test").LogInformation("Entrada de prueba {Value}", index);
            }
        }

        var info = new FileInfo(path);

        Assert.True(info.Length <= 256, $"The file of the log grew to {info.Length} bytes.");
        Assert.True(File.Exists(path + ".1"));
    }

    [Fact]
    public void A_file_that_cannot_be_written_never_stops_the_client()
    {
        // A folder where the file should be: the file cannot be written.
        var path = Path.Combine(_folder, "bloqueado", JsonFileLoggerProvider.FileName);
        Directory.CreateDirectory(Path.Combine(_folder, "bloqueado"));

        using var factory = CreateLoggerFactory(path);

        factory.CreateLogger("LANPartyHub.Client.Test").LogInformation("Entrada de prueba.");
    }

    private static ILoggerFactory CreateLoggerFactory(string path, long maximumSizeInBytes = JsonFileLoggerProvider.DefaultMaximumSizeInBytes) =>
        LoggerFactory.Create(logging =>
        {
            logging.ClearProviders();
            logging.AddProvider(new JsonFileLoggerProvider(path, maximumSizeInBytes));
            logging.SetMinimumLevel(LogLevel.Information);
        });

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A file that the operating system still holds does not fail the test.
        }
    }
}
