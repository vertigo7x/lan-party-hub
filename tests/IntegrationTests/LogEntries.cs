using System.Text.Json;

namespace LANPartyHub.IntegrationTests;

/// <summary>
/// Reading helpers over the JSON entries of the log (AD-15).
/// </summary>
internal static class LogEntries
{
    /// <summary>
    /// Tells whether the entry belongs to the product and not to the hosting layer of the
    /// framework.
    /// </summary>
    /// <param name="entry">Entry of the log.</param>
    public static bool IsProduct(JsonElement entry) =>
        entry.TryGetProperty("Category", out var category)
        && category.GetString() is { } name
        && name.StartsWith("LANPartyHub", StringComparison.Ordinal);

    /// <summary>
    /// Tells whether the entry was written by the given category of the product. It is the
    /// way to observe the entry of a component and not the entry of another one that
    /// happens to be in the same scope of correlation (AD-15).
    /// </summary>
    /// <param name="entry">Entry of the log.</param>
    /// <param name="category">Category of the component, as the log writes it.</param>
    /// <returns><c>true</c> when the entry belongs to that category.</returns>
    public static bool HasCategory(JsonElement entry, string category) =>
        entry.TryGetProperty("Category", out var value)
        && value.GetString() == category;

    /// <summary>
    /// The scopes of the entry. A scope that is not an object is not a scope of the
    /// product and it is ignored.
    /// </summary>
    /// <param name="entry">Entry of the log.</param>
    public static IEnumerable<JsonElement> Scopes(JsonElement entry)
    {
        if (!entry.TryGetProperty("Scopes", out var scopes) || scopes.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var scope in scopes.EnumerateArray())
        {
            if (scope.ValueKind == JsonValueKind.Object)
            {
                yield return scope;
            }
        }
    }

    /// <summary>
    /// The fields of the state of the entry, which are the structured data of the message.
    /// </summary>
    /// <param name="entry">Entry of the log.</param>
    public static IEnumerable<JsonProperty> StateFields(JsonElement entry)
    {
        if (!entry.TryGetProperty("State", out var state) || state.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        foreach (var field in state.EnumerateObject())
        {
            yield return field;
        }
    }

    /// <summary>
    /// The fields of every scope of the entry.
    /// </summary>
    /// <param name="entry">Entry of the log.</param>
    public static IEnumerable<JsonProperty> ScopeFields(JsonElement entry)
    {
        foreach (var scope in Scopes(entry))
        {
            foreach (var field in scope.EnumerateObject())
            {
                yield return field;
            }
        }
    }

    /// <summary>
    /// The scope of the correlation of the entry, which is the one that carries the
    /// identifier of the client, or <c>null</c> when the entry does not carry one.
    /// </summary>
    /// <param name="entry">Entry of the log.</param>
    /// <param name="clientId">Identifier of the client.</param>
    public static JsonElement? CorrelationScope(JsonElement entry, string clientId)
    {
        foreach (var scope in Scopes(entry))
        {
            if (scope.TryGetProperty("ClientId", out var scopeClientId)
                && scopeClientId.GetString() == clientId)
            {
                return scope;
            }
        }

        return null;
    }

    /// <summary>
    /// Tells whether the entry carries a correlation scope with the given identifiers. An
    /// operation that is <c>null</c> is not checked.
    /// </summary>
    /// <param name="entry">Entry of the log.</param>
    /// <param name="operationId">Identifier of the unit of work, or <c>null</c>.</param>
    /// <param name="clientId">Identifier of the client.</param>
    public static bool HasScope(JsonElement entry, string? operationId, string clientId)
    {
        foreach (var scope in Scopes(entry))
        {
            if (!scope.TryGetProperty("ClientId", out var scopeClientId)
                || scopeClientId.GetString() != clientId)
            {
                continue;
            }

            if (operationId is null)
            {
                return scope.TryGetProperty("OperationId", out _);
            }

            if (scope.TryGetProperty("OperationId", out var scopeOperationId)
                && scopeOperationId.GetString() == operationId)
            {
                return true;
            }
        }

        return false;
    }
}
