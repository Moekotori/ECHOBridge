using Echo.Core.Database;

namespace Echo.Core.Albums;

/// <summary>Retains item IDs/timestamps and duplicates only when an old album splits.</summary>
internal static class AlbumPlaylistMigration
{
    public static void Apply(TransactionSql sql, string now, CancellationToken cancellation)
    {
        sql.Run("""
            CREATE TEMP TABLE ag_saved_items AS SELECT * FROM playlist_items
              WHERE media_type='album' AND source_provider='local';
            CREATE INDEX ag_saved_items_order ON ag_saved_items(playlist_id,position,added_at);
            CREATE TEMP TABLE ag_playlist_claims(playlist_id TEXT,album_id TEXT,PRIMARY KEY(playlist_id,album_id)) WITHOUT ROWID;
            CREATE TEMP TABLE ag_touched_playlists(id TEXT PRIMARY KEY) WITHOUT ROWID;
            """);
        using var read = sql.Command("SELECT id,playlist_id,media_id FROM ag_saved_items ORDER BY playlist_id,position,added_at");
        using var reader = read.ExecuteReader();
        while (reader.Read())
        {
            cancellation.ThrowIfCancellationRequested();
            var itemId = reader.GetString(0);
            var playlistId = reader.GetString(1);
            var mediaId = reader.GetString(2);
            var first = true;
            using var targets = sql.Command("""
                SELECT id FROM ag_targets WHERE old_id=$0 AND NOT EXISTS(
                  SELECT 1 FROM ag_playlist_claims WHERE playlist_id=$1 AND album_id=ag_targets.id)
                ORDER BY count DESC,id
                """, mediaId, playlistId);
            using var targetReader = targets.ExecuteReader();
            while (targetReader.Read())
            {
                var albumId = targetReader.GetString(0);
                var targetItemId = first ? itemId : Guid.NewGuid().ToString();
                if (!first)
                    sql.Run("""
                        INSERT INTO playlist_items(id,playlist_id,media_type,media_id,source_provider,source_item_id,
                          title_snapshot,artist_snapshot,album_snapshot,duration_snapshot,cover_id,position,added_at,added_from,unavailable)
                        SELECT $0,playlist_id,'album',$1,'local',source_item_id,title_snapshot,artist_snapshot,album_snapshot,
                          duration_snapshot,cover_id,(SELECT COALESCE(MAX(position),-1)+1 FROM playlist_items WHERE playlist_id=$2),added_at,added_from,0
                        FROM ag_saved_items WHERE id=$3
                        """, targetItemId, albumId, playlistId, itemId);
                sql.Run("""
                    UPDATE playlist_items SET media_id=$0,title_snapshot=(SELECT title FROM albums WHERE id=$0),
                      artist_snapshot=(SELECT album_artist FROM albums WHERE id=$0),album_snapshot=(SELECT title FROM albums WHERE id=$0),
                      duration_snapshot=(SELECT duration FROM albums WHERE id=$0),cover_id=(SELECT cover_id FROM albums WHERE id=$0),unavailable=0
                    WHERE id=$1;
                    INSERT INTO ag_playlist_claims VALUES($2,$0);
                    INSERT OR IGNORE INTO ag_touched_playlists VALUES($2);
                    """, albumId, targetItemId, playlistId);
                first = false;
            }
            if (first && Convert.ToInt64(sql.Value("SELECT COUNT(*) FROM ag_targets WHERE old_id=$0", mediaId)) > 0)
                sql.Run("DELETE FROM playlist_items WHERE id=$0; INSERT OR IGNORE INTO ag_touched_playlists VALUES($1)", itemId, playlistId);
        }
        sql.Run("""
            UPDATE playlists SET item_count=(SELECT COUNT(*) FROM playlist_items WHERE playlist_id=playlists.id),updated_at=$0
              WHERE id IN(SELECT id FROM ag_touched_playlists)
            """, now);
    }
}
