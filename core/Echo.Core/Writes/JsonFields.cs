using System.Text.Json;

namespace Echo.Core.Writes;

internal static class JsonFields
{
    public static JsonElement Get(JsonElement item, string key) => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(key, out var value) ? value : default;
    public static string? Text(JsonElement item, string key) => Get(item, key) is var value && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    public static double? Number(JsonElement item, string key) => Get(item, key) is var value && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;
    public static object? Value(JsonElement item, string key) => Get(item, key).ValueKind switch
    {
        JsonValueKind.String => Text(item, key), JsonValueKind.Number => Number(item, key),
        JsonValueKind.True => 1, JsonValueKind.False => 0, _ => null,
    };
}
