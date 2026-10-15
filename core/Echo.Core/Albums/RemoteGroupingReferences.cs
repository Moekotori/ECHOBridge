using System.Text;
using Echo.Core.Database;

namespace Echo.Core.Albums;

internal static class RemoteGroupingReferences
{
    public static void Migrate(TransactionSql sql,string strategy,string now,CancellationToken cancellation)
    {
        sql.Run("CREATE TEMP TABLE ag_remote_saved AS SELECT id,playlist_id,media_id,position FROM playlist_items WHERE media_type='album' AND media_id LIKE 'remote-album:%' ORDER BY playlist_id,position,id");
        var legacy=new[]{RemoteGroupingIdentity.Sql("conservative",true),RemoteGroupingIdentity.Sql("conservative",previous:true),RemoteGroupingIdentity.Sql("standard",true),RemoteGroupingIdentity.Sql("standard",previous:true)};
        long cursor=0;
        for (;;)
        {
        var page=new List<(long Row,string Id,string Playlist,string Media)>(100);
        using(var read=sql.Command("SELECT rowid,id,playlist_id,media_id FROM ag_remote_saved WHERE rowid>$0 ORDER BY rowid LIMIT 100",cursor))
        using(var reader=read.ExecuteReader())
            while(reader.Read()) page.Add((reader.GetInt64(0),reader.GetString(1),reader.GetString(2),reader.GetString(3)));
        if(page.Count==0) break;
        cursor=page[^1].Row;
        foreach(var item in page)
        {
            cancellation.ThrowIfCancellationRequested();
            var id=item.Id; var playlist=item.Playlist; var old=item.Media;
            string identity;
            try { identity=Encoding.UTF8.GetString(Convert.FromHexString(old[13..])); } catch(FormatException) { continue; }
            var parts=identity.Split('\u001f'); if(parts.Length<2 || parts[0].Length==0 || parts[1].Length==0) continue;
            sql.Run("CREATE TEMP TABLE ag_remote_targets(id TEXT PRIMARY KEY) WITHOUT ROWID");
            sql.Run($"""
                WITH {RemoteGroupingIdentity.Evidence}
                INSERT OR IGNORE INTO ag_remote_targets
                SELECT 'remote-album:'||lower(hex({RemoteGroupingIdentity.Sql(strategy)}))
                FROM remote_tracks JOIN remote_credit_base credit ON credit.id=remote_tracks.id
                JOIN remote_credit_owners owners ON owners.credit_scope=credit.credit_scope
                LEFT JOIN remote_credit_keys matched ON matched.credit_scope=credit.credit_scope
                  AND matched.owner_key=ag_artist(COALESCE(NULLIF(TRIM(remote_tracks.album_artist),''),remote_tracks.artist))
                WHERE remote_tracks.source_id=$0 AND remote_tracks.provider=$1 AND ({string.Join(" OR ",legacy.Select(key=>key+"=$2"))})
                """,parts[0],parts[1],identity);
            if(Convert.ToInt64(sql.Value("SELECT COUNT(*) FROM ag_remote_targets"))>0 && sql.Value("SELECT id FROM ag_remote_targets WHERE id=$0",old) is null)
            {
                sql.Run("DELETE FROM ag_remote_targets WHERE id IN(SELECT media_id FROM playlist_items WHERE playlist_id=$0 AND media_type='album' AND id!=$1)",playlist,id);
                var first=sql.Value("SELECT id FROM ag_remote_targets ORDER BY id LIMIT 1") as string;
                using(var targets=sql.Command("SELECT id FROM ag_remote_targets WHERE id!=$0 ORDER BY id",first))
                using(var rows=targets.ExecuteReader())
                    while(rows.Read())
                    {
                        cancellation.ThrowIfCancellationRequested();
                        sql.Run("""
                            INSERT INTO playlist_items(id,playlist_id,media_type,media_id,source_provider,source_item_id,title_snapshot,artist_snapshot,
                              album_snapshot,duration_snapshot,cover_id,position,added_at,added_from,unavailable)
                            SELECT $0,playlist_id,media_type,$1,source_provider,source_item_id,title_snapshot,artist_snapshot,album_snapshot,
                              duration_snapshot,cover_id,(SELECT COALESCE(MAX(position),-1)+1 FROM playlist_items WHERE playlist_id=old.playlist_id),
                              added_at,added_from,unavailable FROM playlist_items old WHERE id=$2
                            """,Guid.NewGuid().ToString(),rows.GetString(0),id);
                    }
                if(first is null) sql.Run("DELETE FROM playlist_items WHERE id=$0",id);
                else sql.Run("UPDATE playlist_items SET media_id=$0,unavailable=0 WHERE id=$1",first,id);
                sql.Run("UPDATE playlists SET item_count=(SELECT COUNT(*) FROM playlist_items WHERE playlist_id=$0),updated_at=$1 WHERE id=$0",playlist,now);
            }
            sql.Run("DROP TABLE ag_remote_targets");
        }
        }
        sql.Run("DROP TABLE ag_remote_saved");
    }
}
