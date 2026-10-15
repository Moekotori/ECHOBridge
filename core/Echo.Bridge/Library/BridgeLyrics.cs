using System.Text.Json;
using System.Text.RegularExpressions;

namespace Echo.Bridge.Library;

public static class BridgeLyrics
{
    private static readonly HashSet<string> Fields = ["timeMs", "startMs", "endMs", "text", "translation", "romanization", "kana",
        "id", "agentId", "agentName", "songPart", "wordTimingOrigin", "words", "alternateTexts", "backgroundVocals", "role", "language"];

    public static object? Read(BridgeLibraryReader reader, string id, CancellationToken cancellation)
    {
        var track = reader.Track(id, cancellation);
        var cached = BridgeLibraryMetadata.Read(reader.Profile, cancellation, database =>
        {
            using var command = database.RequireConnection().CreateCommand();
            command.CommandText = """
                SELECT kind,plain_lyrics,synced_lyrics,lines_json,offset_ms,provider_lyrics_id
                FROM lyrics_cache WHERE track_id=$id
                  AND length(lines_json)<=262144 AND COALESCE(length(plain_lyrics),0)<=200000 AND COALESCE(length(synced_lyrics),0)<=200000
                ORDER BY CASE WHEN provider='manual' OR acceptance_origin='manual' THEN 0 WHEN provider='local' THEN 1 WHEN provider='lrclib' THEN 2 ELSE 3 END,updated_at DESC LIMIT 1
                """;
            command.Parameters.AddWithValue("$id", id);
            using var row = command.ExecuteReader();
            if (!row.Read()) return null;
            var kind = row.GetString(0);
            var removed = !row.IsDBNull(5) && row.GetString(5).StartsWith("removed:", StringComparison.Ordinal);
            using var lines = JsonDocument.Parse(row.GetString(3));
            return new Dictionary<string, object?>
            {
                ["kind"] = removed ? "empty" : kind, ["plainText"] = row.IsDBNull(1) ? null : row.GetString(1),
                ["syncedText"] = row.IsDBNull(2) ? null : row.GetString(2), ["offsetMs"] = row.GetInt64(4),
                ["lines"] = Project(lines.RootElement, 0), ["removed"] = removed,
            };
        });
        if (cached?.GetValueOrDefault("removed") is true) return null;
        if (cached is null)
        {
            var path = Path.ChangeExtension(track.Path, ".lrc");
            if (!File.Exists(path)) path = Path.ChangeExtension(track.Path, ".txt");
            if (!File.Exists(path) || new FileInfo(path).Length > 200_000) return null;
            var text = File.ReadAllText(path);
            var lines = new List<object>();
            foreach (var line in text.Split('\n').Take(2000))
            {
                var match = Regex.Match(line, @"^\[(\d{1,3}):(\d{2})(?:\.(\d{1,3}))?\](.*)$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(20));
                if (!match.Success) continue;
                var ms = (int.Parse(match.Groups[1].Value) * 60 + int.Parse(match.Groups[2].Value)) * 1000;
                if (match.Groups[3].Success) ms += int.Parse(match.Groups[3].Value.PadRight(3, '0'));
                lines.Add(new { timeMs = ms, endMs = (int?)null, text = BridgeTrackProjection.Text(match.Groups[4].Value.TrimEnd()) });
            }
            cached = new() { ["kind"] = lines.Count > 0 ? "synced" : "plain", ["lines"] = lines,
                ["plainText"] = lines.Count == 0 ? text : null, ["syncedText"] = lines.Count > 0 ? text : null, ["offsetMs"] = 0 };
        }
        cached.Remove("removed");
        cached["title"] = BridgeTrackProjection.Text(track.Title); cached["artist"] = BridgeTrackProjection.Text(track.Artist);
        cached["album"] = BridgeTrackProjection.Text(track.Album); cached["durationSeconds"] = track.Duration;
        return cached;
    }

    private static object? Project(JsonElement value, int depth)
    {
        if (depth > 6) throw new BridgeException(413, "peer_lyrics_too_large");
        return value.ValueKind switch
        {
            JsonValueKind.Array when value.GetArrayLength() <= (depth == 0 ? 2000 : 160) => value.EnumerateArray().Select(item => Project(item, depth + 1)).ToArray(),
            JsonValueKind.Object => value.EnumerateObject().Where(item => Fields.Contains(item.Name)).ToDictionary(item => item.Name, item => Project(item.Value, depth + 1)),
            JsonValueKind.String when value.GetString()!.Length <= 4096 => value.GetString(),
            JsonValueKind.Number => value.GetDouble(), JsonValueKind.Null => null,
            _ => throw new BridgeException(413, "peer_lyrics_too_large"),
        };
    }
}
