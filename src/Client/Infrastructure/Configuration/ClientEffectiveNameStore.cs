using System.Text.Json;
using System.Text.Json.Nodes;
using LANPartyHub.Client.Application.Ports;

namespace LANPartyHub.Client.Infrastructure.Configuration;

/// <summary>
/// Persistence of the EFFECTIVE name of the team in the configuration file of the
/// client (AD-12, AD-30, AD-31).
///
/// <para>
/// El nombre efectivo es el único bajo el que la fila del cliente queda registrada, así
/// que el cliente lo PERSISTE y lo propone en sus reconexiones: proponer el nombre que
/// colisionó haría que el servidor asignara un nombre derivado distinto en cada
/// reconexión (AD-30, AD-31).
/// </para>
/// <para>
/// El nombre vive en el ARCHIVO DE CONFIGURACIÓN y no en la base de datos del cliente,
/// porque el esquema de esa base de datos es el del ERD de la espina y no admite una
/// columna más en la fila de la identidad (AD-11, AD-22).
/// </para>
/// <para>
/// El adaptador reescribe solo la clave que cambia y conserva las demás del archivo,
/// así que un tema o una dirección ya configurados sobreviven al cambio (AD-12).
/// </para>
/// </summary>
public sealed class ClientEffectiveNameStore : IClientEffectiveNameStore
{
    private readonly string _filePath;
    private readonly string _sectionName;
    private readonly string _key;

    /// <summary>Creates the adapter over the configuration file of the client.</summary>
    /// <param name="filePath">Absolute path of the configuration file.</param>
    /// <param name="sectionName">Section of the file that holds the settings of the client.</param>
    /// <param name="key">Key of the effective name inside the section.</param>
    public ClientEffectiveNameStore(string filePath, string sectionName, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        _filePath = filePath;
        _sectionName = sectionName;
        _key = key;
    }

    /// <inheritdoc />
    public async Task<string?> LoadEffectiveMachineNameAsync(CancellationToken cancellationToken)
    {
        var document = await ReadAsync(cancellationToken).ConfigureAwait(false);

        if (document[_sectionName] is not JsonObject section)
        {
            return null;
        }

        if (section[_key] is not JsonValue value)
        {
            return null;
        }

        var name = value.GetValue<string>();

        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    /// <inheritdoc />
    public async Task SaveEffectiveMachineNameAsync(
        string machineName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(machineName);

        var document = await ReadAsync(cancellationToken).ConfigureAwait(false);

        var section = document[_sectionName] as JsonObject ?? new JsonObject();
        section[_key] = machineName;
        document[_sectionName] = section;

        var json = document.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

        await File.WriteAllTextAsync(_filePath, json, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the configuration file. A file that does not exist, that está vacío o que no
    /// es un objeto JSON se lee como un documento vacío (AD-12).
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The configuration document.</returns>
    private async Task<JsonObject> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return new JsonObject();
        }

        var content = await File.ReadAllTextAsync(_filePath, cancellationToken).ConfigureAwait(false);

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
