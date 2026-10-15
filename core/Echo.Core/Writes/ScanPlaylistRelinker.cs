using System.Text.Json;
using Echo.Core.Database;
using static Echo.Core.Writes.JsonFields;
namespace Echo.Core.Writes;
internal static class ScanPlaylistRelinker
{
    public static void Relink(TransactionSql sql,string id,string path,JsonElement t,string? cover,string timestamp)
    {
        var changed=sql.Run("""
UPDATE playlist_items SET
        media_id = $0,
        source_item_id = $1,
        cover_id = COALESCE(cover_id, $2),
        unavailable = 0
       WHERE media_type = 'track'
         AND source_provider = 'local'
         AND NOT EXISTS (
           SELECT 1
           FROM tracks AS linked_tracks
           WHERE linked_tracks.id = playlist_items.media_id
             AND linked_tracks.missing = 0
         )
         AND (
           source_item_id = $3
           OR (
             source_item_id IS NULL
             AND title_snapshot = $4
             AND artist_snapshot = $5
             AND COALESCE(album_snapshot, '') = COALESCE($6, '')
             AND (
               duration_snapshot IS NULL
               OR $7 IS NULL
               OR ABS(duration_snapshot - $8) < 1
             )
           )
         )
""", id,path,cover,path,Text(t,"title"),Text(t,"artist"),Text(t,"album"),Number(t,"duration"),Number(t,"duration"));
        if(changed>0) sql.Run("""
UPDATE playlists SET updated_at = $0
         WHERE id IN (
           SELECT DISTINCT playlist_id
           FROM playlist_items
           WHERE media_type = 'track'
             AND source_provider = 'local'
             AND media_id = $1
         )
""",timestamp,id);
    }
}
