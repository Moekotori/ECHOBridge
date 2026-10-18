namespace Echo.Core.Playlists;

internal static class PlaylistTrackCommands
{
    public static List<string> Add(PlaylistSql sql, string id, List<string> trackIds, string timestamp, string? afterItemId = null)
    {
        var source = sql.Value("SELECT source_provider FROM playlists WHERE id=$0", id) as string;
        if (source is null) throw new InvalidOperationException("Unknown playlist");
        if (source != "local") throw new InvalidOperationException("Local tracks cannot be added to streaming playlists. Keep local and streaming playlists separate.");
        if (sql.Value("SELECT kind FROM playlists WHERE id=$0", id) as string == "smart")
            throw new InvalidOperationException("Dynamic smart playlists are rule-based. Freeze the playlist before adding tracks manually.");
        int? insertPosition = null;
        if (afterItemId is not null)
        {
            if (sql.Value("SELECT kind FROM playlists WHERE id=$0", id) as string != "manual"
                || sql.Value("SELECT sort_mode FROM playlists WHERE id=$0", id) as string != "manual")
                throw new InvalidOperationException("Insert-after requires a manually ordered playlist.");
            if (sql.Value("SELECT 1 FROM playlist_items WHERE playlist_id=$0 AND id=$1", id, afterItemId) is null)
                throw new InvalidOperationException("Unknown insertion anchor.");
            sql.Resequence(id);
            insertPosition = Convert.ToInt32(sql.Value("SELECT position FROM playlist_items WHERE id=$0", afterItemId)) + 1;
        }
        var unique = trackIds.Distinct(StringComparer.Ordinal).ToArray();
        if (insertPosition is not null)
            sql.Run("UPDATE playlist_items SET position=position+$0 WHERE playlist_id=$1 AND position>=$2", unique.Length, id, insertPosition);
        var added = new List<string>();
        // The command transaction owns insertion and resequencing; failed tracks roll back the batch.
        foreach (var trackId in unique)
        {
            if (sql.Value("SELECT 1 FROM tracks WHERE id=$0", trackId) is null) throw new InvalidOperationException("Unknown track");
            if (sql.Value("SELECT 1 FROM playlist_items WHERE playlist_id=$0 AND media_type='track' AND media_id=$1 LIMIT 1", id, trackId) is not null) continue;
            var itemId = Guid.NewGuid().ToString();
            sql.Run("""
                INSERT INTO playlist_items (id,playlist_id,media_type,media_id,source_provider,source_item_id,
                  title_snapshot,artist_snapshot,album_snapshot,duration_snapshot,cover_id,position,added_at,added_from,unavailable)
                SELECT $0,$1,'track',id,'local',path,title,artist,album,duration,cover_id,$2,$3,'library',0 FROM tracks WHERE id=$4
                """, itemId, id, (insertPosition ?? -unique.Length) + added.Count, timestamp, trackId);
            added.Add(itemId);
        }
        if (added.Count > 0 || insertPosition is not null) sql.Resequence(id);
        if (added.Count > 0) sql.Refresh(id, timestamp);
        return added;
    }
    public static void Move(PlaylistSql sql, string id, string itemId, int target, string timestamp)
    {
        if (sql.Value("SELECT kind FROM playlists WHERE id=$0", id) as string == "smart")
        {
            Echo.Core.SmartPlaylists.SmartPlaylistOrder.Move(sql, id, itemId, target, timestamp);
            return;
        }
        var count = Convert.ToInt32(sql.Value("SELECT COUNT(*) FROM playlist_items WHERE playlist_id=$0", id));
        if (sql.Value("SELECT 1 FROM playlist_items WHERE playlist_id=$0 AND id=$1", id, itemId) is null)
            throw new InvalidOperationException("Unknown playlist item");
        sql.Resequence(id);
        var from = Convert.ToInt32(sql.Value("SELECT position FROM playlist_items WHERE id=$0", itemId));
        target = Math.Clamp(target, 0, Math.Max(0, count - 1));
        if (from < target) sql.Run("UPDATE playlist_items SET position=position-1 WHERE playlist_id=$0 AND position>$1 AND position<=$2", id, from, target);
        else if (from > target) sql.Run("UPDATE playlist_items SET position=position+1 WHERE playlist_id=$0 AND position>=$1 AND position<$2", id, target, from);
        sql.Run("UPDATE playlist_items SET position=$0 WHERE id=$1", target, itemId);
        sql.Run("UPDATE playlists SET updated_at=$0 WHERE id=$1", timestamp, id);
    }
}
