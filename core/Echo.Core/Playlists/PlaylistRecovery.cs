using System.Text.Json;

namespace Echo.Core.Playlists;

internal static class PlaylistRecovery
{
    internal const string ItemColumns = "id,playlist_id,media_type,media_id,source_provider,source_item_id,title_snapshot,artist_snapshot,album_snapshot,duration_snapshot,cover_id,position,added_at,added_from,unavailable";
    private const string PlaylistColumns = "id,name,description,kind,source_provider,source_playlist_id,cover_id,cover_url,sort_mode,item_count,created_at,updated_at";
    internal static string JsonRow(string columns) => "json_object(" + string.Join(",", columns.Split(',').Select(column => $"'{column}',{column}")) + ")";

    internal static string Save(PlaylistSql sql, string id, string action, string timestamp, string? target = null)
    {
        var recovery = Guid.NewGuid().ToString();
        sql.Run($"""
            INSERT INTO playlist_recovery(id,playlist_id,name,action,created_at,playlist_json,rules_json,target_playlist_id)
            SELECT $0,id,name,$2,$3,json_set({JsonRow(PlaylistColumns)},
              '$._pinned',COALESCE((SELECT pinned FROM playlist_organization WHERE playlist_id=$1),0),
              '$._position',COALESCE((SELECT position FROM playlist_organization WHERE playlist_id=$1),10000),
              '$._groupId',(SELECT group_id FROM playlist_group_members WHERE playlist_id=$1)),
              (SELECT rules_json FROM smart_playlist_rules WHERE playlist_id=$1),$4
            FROM playlists WHERE id=$1
            """, recovery, id, action, timestamp, target);
        return recovery;
    }

    internal static void Capture(PlaylistSql sql, string recovery, string id, bool selectedOnly)
    {
        sql.Run($"""
            INSERT INTO playlist_recovery_items(recovery_id,item_id,payload)
            SELECT $0,id,{JsonRow(ItemColumns)} FROM playlist_items
            WHERE playlist_id=$1 {(selectedOnly ? "AND id IN (SELECT id FROM playlist_selection)" : "")}
            """, recovery, id);
    }

    internal static void Prune(PlaylistSql sql, string current)
    {
        // A single oversized change is rejected atomically rather than silently losing its undo.
        if (Convert.ToInt64(sql.Value("SELECT COUNT(*) FROM playlist_recovery_items WHERE recovery_id=$0", current)) > 100000 ||
            Convert.ToInt64(sql.Value("SELECT COALESCE(SUM(length(CAST(payload AS BLOB))),0) FROM playlist_recovery_items WHERE recovery_id=$0", current)) > 32 * 1024 * 1024)
            throw new InvalidOperationException("This operation exceeds recovery capacity. Remove smaller batches first.");
        sql.Run("DELETE FROM playlist_recovery WHERE created_at < strftime('%Y-%m-%dT%H:%M:%fZ','now','-30 days') AND id<>$0", current);
        while (Convert.ToInt64(sql.Value("SELECT COUNT(*) FROM playlist_recovery")) > 20 ||
            Convert.ToInt64(sql.Value("SELECT COUNT(*) FROM playlist_recovery_items")) > 100000 ||
            Convert.ToInt64(sql.Value("SELECT COALESCE(SUM(length(CAST(payload AS BLOB))),0) FROM playlist_recovery_items")) > 32 * 1024 * 1024)
            sql.Run("DELETE FROM playlist_recovery WHERE id=(SELECT id FROM playlist_recovery WHERE id<>$0 ORDER BY created_at,id LIMIT 1)", current);
    }

    internal static string Restore(PlaylistSql sql, string recovery, string timestamp)
    {
        string id, action, playlistJson;
        string? rules, target;
        using (var command = sql.Command("SELECT playlist_id,action,playlist_json,rules_json,target_playlist_id FROM playlist_recovery WHERE id=$0", recovery))
        using (var reader = command.ExecuteReader())
        {
            if (!reader.Read()) throw new InvalidOperationException("Recovery record no longer exists.");
            id = reader.GetString(0); action = reader.GetString(1); playlistJson = reader.GetString(2);
            rules = reader.IsDBNull(3) ? null : reader.GetString(3);
            target = reader.IsDBNull(4) ? null : reader.GetString(4);
        }
        if (action == "delete")
        {
            if (sql.Value("SELECT 1 FROM playlists WHERE id=$0", id) is not null)
                throw new InvalidOperationException("Playlist already exists. Refresh before restoring.");
            sql.Run($"INSERT INTO playlists({PlaylistColumns}) SELECT {Extract(PlaylistColumns, "$0")}", playlistJson);
            sql.Run("INSERT INTO playlist_organization(playlist_id,pinned,position) VALUES ($0,COALESCE(json_extract($1,'$._pinned'),0),COALESCE(json_extract($1,'$._position'),10000))", id,playlistJson);
            sql.Run("""
                INSERT INTO playlist_group_members(playlist_id,group_id)
                SELECT $0,json_extract($1,'$._groupId') WHERE EXISTS
                  (SELECT 1 FROM playlist_groups WHERE id=json_extract($1,'$._groupId'))
                """,id,playlistJson);
            if (rules is not null) sql.Run("INSERT INTO smart_playlist_rules(playlist_id,version,rules_json,created_at,updated_at) VALUES ($0,1,$1,$2,$2)", id, rules, timestamp);
        }
        else
        {
            if (action == "clean") PlaylistInspection.RequireSupported(sql,id);
            else PlaylistManagementCommands.RequireEditable(sql, id);
            if (sql.Value("""
                SELECT 1 FROM playlist_recovery_items r JOIN playlist_items p
                ON p.id=r.item_id OR ($2!='clean' AND p.playlist_id=$1 AND p.media_id=json_extract(r.payload,'$.media_id') AND p.media_type=json_extract(r.payload,'$.media_type'))
                WHERE r.recovery_id=$0 AND r.role='source' LIMIT 1
                """, recovery, id, action) is not null)
                throw new InvalidOperationException("Some removed songs were added again. Recovery would create duplicates.");
        }
        // Undo a move only removes the exact copies this operation created, never pre-existing target songs.
        if (target is not null)
        {
            if (sql.Value("""
                SELECT 1 FROM playlist_recovery_items r LEFT JOIN playlist_items p ON p.id=r.item_id
                WHERE r.recovery_id=$0 AND r.role='target' AND
                  (p.id IS NULL OR p.playlist_id<>$1 OR p.media_id IS NOT json_extract(r.payload,'$.media_id')) LIMIT 1
                """, recovery, target) is not null)
                throw new InvalidOperationException("The destination songs changed. Recovery was not applied.");
            sql.Run("DELETE FROM playlist_items WHERE id IN (SELECT item_id FROM playlist_recovery_items WHERE recovery_id=$0 AND role='target')", recovery);
            sql.Resequence(target); sql.Refresh(target, timestamp);
        }
        // Insert each row at its prior position, shifting existing songs instead of overwriting later edits.
        if (action == "delete")
        {
            sql.Run($"INSERT INTO playlist_items({ItemColumns}) SELECT {Extract(ItemColumns, "payload")} FROM playlist_recovery_items WHERE recovery_id=$0 AND role='source' ORDER BY CAST(json_extract(payload,'$.position') AS INTEGER)", recovery);
        }
        else using (var command = sql.Command("SELECT payload FROM playlist_recovery_items WHERE recovery_id=$0 AND role='source' ORDER BY CAST(json_extract(payload,'$.position') AS INTEGER)", recovery))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var payload = reader.GetString(0);
                sql.Run("UPDATE playlist_items SET position=position+1 WHERE playlist_id=$0 AND position>=json_extract($1,'$.position')", id, payload);
                sql.Run($"INSERT INTO playlist_items({ItemColumns}) SELECT {Extract(ItemColumns, "$0")}", payload);
            }
        }
        if (action != "delete") sql.Resequence(id);
        sql.Refresh(id, timestamp);
        sql.Run("DELETE FROM playlist_recovery WHERE id=$0", recovery);
        return id;
    }

    private static string Extract(string columns, string source) => string.Join(",", columns.Split(',').Select(column => $"json_extract({source},'$.{column}')"));
}
