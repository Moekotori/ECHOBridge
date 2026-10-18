using System.Text.Json;

namespace Echo.Bridge.Library;

public static class BridgeLyrics
{
    public static object? Read(BridgeLibraryReader reader, string id, CancellationToken cancellation)
    {
        var track = reader.Track(id, cancellation);
        var cached = BridgeLibraryMetadata.Read(reader.Profile, cancellation, database =>
        {
            using var command = database.RequireConnection().CreateCommand();
            command.CommandText = """
                SELECT kind,
                  CASE WHEN length(plain_lyrics)<=100000 THEN plain_lyrics END,
                  CASE WHEN length(synced_lyrics)<=200000 THEN synced_lyrics END,
                  CASE WHEN length(lines_json)<=524288 THEN lines_json END,
                  offset_ms,provider_lyrics_id,
                  (COALESCE(length(lines_json),0)>524288 OR COALESCE(length(plain_lyrics),0)>100000 OR COALESCE(length(synced_lyrics),0)>200000),
                  cache_key,provider,acceptance_origin,title,artist
                FROM lyrics_cache WHERE track_id=$id
                ORDER BY CASE WHEN provider='manual' OR acceptance_origin='manual' THEN 0 WHEN provider='local' THEN 1 WHEN provider='lrclib' THEN 2 ELSE 3 END,updated_at DESC LIMIT 32
                """;
            command.Parameters.AddWithValue("$id", id);
            using var row = command.ExecuteReader();
            while (row.Read())
            {
                cancellation.ThrowIfCancellationRequested();
                var removed = !row.IsDBNull(5) && row.GetString(5).StartsWith("removed:", StringComparison.Ordinal);
                if (removed) return new Dictionary<string, object?> { ["removed"] = true };
                if (!BridgeLyricCacheIdentity.Matches(track, row.GetString(7), row.GetString(8), row.GetString(9),
                    row.GetString(10), row.GetString(11))) continue;
                if (row.GetBoolean(6)) throw BridgeLyricProjection.TooLarge();
                using var lines = JsonDocument.Parse(row.GetString(3));
                return new Dictionary<string, object?>
                {
                    ["kind"] = row.GetString(0), ["plainText"] = row.IsDBNull(1) ? null : row.GetString(1),
                    ["syncedText"] = row.IsDBNull(2) ? null : row.GetString(2), ["offsetMs"] = row.GetInt64(4),
                    ["lines"] = new BridgeLyricProjection().Project(lines.RootElement), ["removed"] = false,
                };
            }
            return null;
        });
        if (cached?.GetValueOrDefault("removed") is true) return null;
        if (cached is null)
        {
            var path = Path.ChangeExtension(track.Path, ".lrc");
            if (!File.Exists(path)) path = Path.ChangeExtension(track.Path, ".txt");
            if (!File.Exists(path)) return null;
            if (new FileInfo(path).Length > 400_000) throw BridgeLyricProjection.TooLarge();
            var text = File.ReadAllText(path);
            cached = BridgeLocalLyrics.Parse(text);
        }
        cached.Remove("removed");
        cached["title"] = BridgeTrackProjection.Text(track.Title); cached["artist"] = BridgeTrackProjection.Text(track.Artist);
        cached["album"] = BridgeTrackProjection.Text(track.Album); cached["durationSeconds"] = track.Duration;
        return cached;
    }

}
