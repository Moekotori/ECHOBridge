using System.Text.Json;

namespace Echo.Bridge.Library;

internal sealed class BridgeLyricProjection
{
    internal const int MaxLines = 4000;
    internal const int MaxCharacters = 100_000;
    private int characters, words;
    private static readonly HashSet<string> Fields = ["timeMs", "startMs", "endMs", "text", "translation", "romanization", "kana",
        "id", "agentId", "agentName", "songPart", "wordTimingOrigin", "words", "alternateTexts", "backgroundVocals", "role", "language"];

    public object? Project(JsonElement value, int depth = 0, string? field = null)
    {
        if (depth > 6) throw TooLarge();
        if (value.ValueKind == JsonValueKind.Array)
        {
            var limit = depth == 0 ? MaxLines : field switch { "words" => 1000, "alternateTexts" or "backgroundVocals" => 8, _ => 0 };
            if (value.GetArrayLength() > limit) throw TooLarge();
            if (field == "words") { words += value.GetArrayLength(); if (words > 16_000) throw TooLarge(); }
            return value.EnumerateArray().Select(item => Project(item, depth + 1)).ToArray();
        }
        if (value.ValueKind == JsonValueKind.Object)
            return value.EnumerateObject().Where(item => Fields.Contains(item.Name))
                .ToDictionary(item => item.Name, item => Project(item.Value, depth + 1, item.Name));
        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString()!;
            if (text.Length > 4096) throw TooLarge();
            if (field is "text" or "translation" or "romanization" or "kana") characters += text.Length;
            if (characters > MaxCharacters) throw TooLarge();
            return text;
        }
        return value.ValueKind switch { JsonValueKind.Number => value.GetDouble(), JsonValueKind.Null => null, _ => throw TooLarge() };
    }
    internal static BridgeException TooLarge() => new(413, "peer_lyrics_too_large");
}
