using System.Text.Json;
using Echo.Core.Protocol;

namespace Echo.Core.Media;

internal static class NativeMetadataProtocol
{
    public static void RequireEvent(string line, string type)
    {
        using var parsed = JsonDocument.Parse(line);
        if (!IsObject(parsed.RootElement) || Text(parsed.RootElement, "type") != type) Fail();
    }

    public static NativeMetadataResponse? Response(string line, string requestId)
    {
        using var parsed = JsonDocument.Parse(line);
        var root = parsed.RootElement;
        if (!IsObject(root)) { Fail(); return null; }
        if (Text(root, "requestId") != requestId) return null;
        var type = Text(root, "type");
        if (type == "unsupported") return new("unsupported");
        if (type == "metadata" && root.TryGetProperty("result", out var result) && IsObject(result))
            return new("ok", result.Clone());
        if (type == "error") throw new NativeMetadataReadException("native_metadata_failed");
        Fail();
        return null;
    }

    private static bool IsObject(JsonElement value) => value.ValueKind == JsonValueKind.Object;
    private static string? Text(JsonElement value, string name) => value.TryGetProperty(name, out var field)
        && field.ValueKind == JsonValueKind.String ? field.GetString() : null;
    private static void Fail() => throw new NativeMetadataReadException("native_metadata_protocol");
}
