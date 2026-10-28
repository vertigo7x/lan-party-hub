using System.Text.Json;
using System.Text.Json.Nodes;
using LANPartyHub.Client.Application.Ports;

namespace LANPartyHub.Client.Infrastructure.Configuration;

/// <summary>
/// Persistence of the settings of the client in its configuration file (AD-12, AR-23).
///
/// <para>
/// The file is the layer of the lowest priority of the configuration, so it is where the
/// installation root that the player chooses in the settings of the client is written. The
/// adapter rewrites only the key that changes and keeps every other key of the file, so a
/// theme or an address that is already configured survive the change.
/// </para>
/// </summary>
public sealed class ClientSettingsStore : IClientSettingsStore
{
    private readonly string _filePath;
    private readonly string _sectionName;
    private readonly string _key;

    /// <summary>Creates the adapter over the configuration file of the client.</summary>
    /// <param name="filePath">Absolute path of the configuration file.</param>
    /// <param name="sectionName">Section of the file that holds the settings of the client.</param>
    /// <param name="key">Key of the installation root inside the section.</param>
    public ClientSettingsStore(string filePath, string sectionName, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        _filePath = filePath;
        _sectionName = sectionName;
        _key = key;
    }

    /// <inheritdoc />
    public async Task SaveInstallDirectoryAsync(string installDirectory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);

        var document = await ReadAsync(cancellationToken);

        var section = document[_sectionName] as JsonObject ?? new JsonObject();
        section[_key] = installDirectory;
        document[_sectionName] = section;

        var json = document.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

        await File.WriteAllTextAsync(_filePath, json, cancellationToken);
    }

    private async Task<JsonObject> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return new JsonObject();
        }

        var content = await File.ReadAllTextAsync(_filePath, cancellationToken);

        if (string.IsNullOrWhiteSpace(content))
        {
            return new JsonObject();
        }

        try
        {
            return JsonNode.Parse(content) as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            // A file that is not a JSON object is replaced by one that holds the settings.
            return new JsonObject();
        }
    }
}
