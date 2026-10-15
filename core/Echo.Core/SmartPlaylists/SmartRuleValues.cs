using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Echo.Core.SmartPlaylists;

internal static class SmartRuleValues
{
    public static JsonObject Object(JsonElement value) => value.ValueKind == JsonValueKind.Object
        ? JsonNode.Parse(value.GetRawText())!.AsObject() : new();
    public static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    public static string? Clean(JsonNode? node, int maximum = int.MaxValue)
    {
        var text = Text(node)?.Trim();
        return string.IsNullOrEmpty(text) ? null : text[..Math.Min(maximum, text.Length)];
    }
    public static bool? Boolean(JsonNode? node) => node is JsonValue value && value.TryGetValue<bool>(out var boolean) ? boolean : null;
    public static double? Number(JsonNode? node, bool present = true)
    {
        if (!present) return null;
        if (node is null) return 0;
        if (node is not JsonValue value) return null;
        if (value.TryGetValue<double>(out var number)) return double.IsFinite(number) ? number : null;
        if (value.TryGetValue<int>(out var integer)) return integer;
        if (value.TryGetValue<long>(out var wide)) return wide;
        if (value.TryGetValue<bool>(out var boolean)) return boolean ? 1 : 0;
        if (value.TryGetValue<string>(out var text))
        {
            if (text.Trim().Length == 0) return 0;
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number) && double.IsFinite(number) ? number : null;
        }
        return null;
    }
    public static int Integer(JsonNode? node, int fallback, int minimum, int maximum, bool present = true) =>
        Number(node, present) is { } number ? (int)Math.Clamp(Math.Floor(number), minimum, maximum) : fallback;
    public static JsonElement Element(JsonNode node) { using var doc = JsonDocument.Parse(node.ToJsonString()); return doc.RootElement.Clone(); }
    public static string Threshold(double days, DateTimeOffset now) => now.AddDays(-days).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
}
