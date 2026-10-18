using System.Text.Json;
using Echo.Core.Protocol;

namespace Echo.Core.Playlists;

/// <summary>One portable playlist is applied atomically by the existing SQLite writer owner.</summary>
internal static class CloudPlaylistCommands
{
    internal const string Capability = "playlists.cloud/v1";
    internal static PlaylistCommandResult Execute(PlaylistSql sql, PlaylistCommand request, string timestamp)
    {
        var input = request.CloudPlaylist;
        var id = request.PlaylistId ?? throw new ArgumentException("Playlist ID required.");
        if (!Guid.TryParse(id, out _)) throw new ArgumentException("Invalid cloud playlist ID.");
        var exists = sql.Value("SELECT kind FROM playlists WHERE id=$0", id) as string;
        if (exists is not null && (exists != "manual" || sql.Value("SELECT source_provider FROM playlists WHERE id=$0", id) as string != "local"))
            throw new InvalidOperationException("Cloud may only change manual local playlists.");
        var expected = input.GetProperty("expectedUpdatedAt");
        if ((sql.Value("SELECT updated_at FROM playlists WHERE id=$0", id) as string) != expected.GetString())
            throw new InvalidOperationException("Playlist changed during cloud reconciliation.");
        if (exists is not null)
        {
            var recovery = PlaylistRecovery.Save(sql, id, input.GetProperty("deleted").GetBoolean() ? "delete" : "clear", timestamp);
            PlaylistRecovery.Capture(sql, recovery, id, false);
            PlaylistRecovery.Prune(sql, recovery);
        }
        if (input.GetProperty("deleted").GetBoolean())
        {
            sql.Run("DELETE FROM playlists WHERE id=$0", id);
            return new(id, []);
        }
        var name = input.GetProperty("name").GetString();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 2048) throw new ArgumentException("Invalid playlist name.");
        var description = input.GetProperty("description").GetString();
        var items = input.GetProperty("items");
        if (items.GetArrayLength() > 20000) throw new ArgumentException("Cloud playlist limit.");
        sql.Run("""
            INSERT INTO playlists(id,name,description,kind,source_provider,sort_mode,item_count,created_at,updated_at)
            VALUES($0,$1,$2,'manual','local',$3,0,$4,$5)
            ON CONFLICT(id) DO UPDATE SET name=excluded.name,description=excluded.description,sort_mode=excluded.sort_mode,updated_at=excluded.updated_at
            """, id, name, description, input.GetProperty("sortMode").GetString(), input.GetProperty("createdAt").GetString(), timestamp);
        sql.Run("CREATE TEMP TABLE cloud_playlist_items(id TEXT PRIMARY KEY)");
        var position = 0;
        foreach (var item in items.EnumerateArray())
        {
            var itemId = item.GetProperty("id").GetString();
            if (!Guid.TryParse(itemId, out _)) throw new ArgumentException("Invalid cloud item ID.");
            if (sql.Value("SELECT 1 FROM playlist_items WHERE id=$0 AND playlist_id<>$1", itemId, id) is not null)
                throw new InvalidOperationException("Cloud item belongs to another playlist.");
            sql.Run("INSERT INTO cloud_playlist_items VALUES($0)", itemId);
            var trackId = item.GetProperty("trackId").GetString();
            if (trackId is not null && sql.Value("SELECT 1 FROM tracks WHERE id=$0 AND missing=0", trackId) is null)
                throw new InvalidOperationException("Cloud track is no longer available.");
            sql.Run("""
                INSERT INTO playlist_items(id,playlist_id,media_type,media_id,source_provider,source_item_id,
                  title_snapshot,artist_snapshot,album_snapshot,duration_snapshot,cover_id,position,added_at,added_from,unavailable)
                VALUES($0,$1,'track',$2,'local',$3,$4,$5,$6,$7,(SELECT cover_id FROM tracks WHERE id=$2),$8,$9,'steam-cloud',$10)
                ON CONFLICT(id) DO UPDATE SET media_id=excluded.media_id,source_item_id=excluded.source_item_id,
                  title_snapshot=excluded.title_snapshot,artist_snapshot=excluded.artist_snapshot,album_snapshot=excluded.album_snapshot,
                  duration_snapshot=excluded.duration_snapshot,cover_id=COALESCE(playlist_items.cover_id,excluded.cover_id),
                  position=excluded.position,unavailable=excluded.unavailable
                """, itemId, id, trackId, "steam-cloud:" + itemId, item.GetProperty("title").GetString(),
                item.GetProperty("artist").GetString(), item.GetProperty("album").GetString(),
                item.GetProperty("duration").ValueKind == JsonValueKind.Null ? null : item.GetProperty("duration").GetDouble(),
                position++, item.GetProperty("addedAt").GetString(), trackId is null ? 1 : 0);
        }
        sql.Run("DELETE FROM playlist_items WHERE playlist_id=$0 AND id NOT IN (SELECT id FROM cloud_playlist_items)", id);
        sql.Run("DROP TABLE cloud_playlist_items");
        sql.Refresh(id, timestamp);
        return new(id, []);
    }
}
