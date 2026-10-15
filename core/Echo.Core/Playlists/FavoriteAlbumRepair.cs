using System.Text;

namespace Echo.Core.Playlists;

/// <summary>Relink missing album identities without materializing all matches in Electron.</summary>
internal static class FavoriteAlbumRepair
{
    public static void Execute(PlaylistSql sql, string playlistId, string timestamp)
    {
        string cursor = "";
        var deleted = false;
        for (;;)
        {
            var batch = new List<(string Id, string? Artist, string? Title)>(128);
            using (var command = sql.Command("""
                SELECT items.id, items.artist_snapshot,
                  COALESCE(NULLIF(items.title_snapshot,''),items.album_snapshot)
                FROM playlist_items items LEFT JOIN albums ON albums.id=items.media_id
                WHERE items.playlist_id=$0 AND items.media_type='album'
                  AND items.source_provider='local' AND albums.id IS NULL AND items.id>$1
                ORDER BY items.id LIMIT 128
                """, playlistId, cursor))
            using (var reader = command.ExecuteReader())
                while (reader.Read()) batch.Add((reader.GetString(0), Text(reader, 1), Text(reader, 2)));
            if (batch.Count == 0) break;
            cursor = batch[^1].Id;
            foreach (var item in batch)
            {
                // Keep only two candidates: a unique title or a unique artist match.
                // Ambiguous names must never merge favorites.
                string? first = null, artistMatch = null;
                int count = 0, artistCount = 0;
                using (var command = sql.Command("SELECT id,album_artist FROM albums WHERE TRIM(title)=TRIM($0) COLLATE NOCASE", item.Title))
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        first ??= reader.GetString(0);
                        count = Math.Min(2, count + 1);
                        if (!string.IsNullOrEmpty(item.Artist) && Key(reader.GetString(1)) == Key(item.Artist))
                        {
                            artistMatch ??= reader.GetString(0);
                            artistCount = Math.Min(2, artistCount + 1);
                        }
                    }
                }
                var albumId = artistCount == 1 ? artistMatch : artistCount == 0 && count == 1 ? first : null;
                if (albumId is null) continue;
                if (sql.Value("SELECT id FROM playlist_items WHERE playlist_id=$0 AND media_type='album' AND media_id=$1 AND id!=$2 LIMIT 1", playlistId, albumId, item.Id) is not null)
                {
                    sql.Run("DELETE FROM playlist_items WHERE id=$0", item.Id);
                    deleted = true;
                    continue;
                }
                sql.Run("""
                    UPDATE playlist_items SET media_id=$0,
                      title_snapshot=(SELECT title FROM albums WHERE id=$0),
                      artist_snapshot=(SELECT album_artist FROM albums WHERE id=$0),
                      album_snapshot=(SELECT title FROM albums WHERE id=$0),
                      duration_snapshot=(SELECT duration FROM albums WHERE id=$0),
                      cover_id=(SELECT cover_id FROM albums WHERE id=$0),unavailable=0 WHERE id=$1
                    """, albumId, item.Id);
            }
        }
        if (deleted) { sql.Resequence(playlistId); sql.Refresh(playlistId, timestamp); }
    }

    private static string Key(string value) => value.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
    private static string? Text(Microsoft.Data.Sqlite.SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetString(index);
}
