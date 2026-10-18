using Echo.Core.Protocol;

namespace Echo.Core.Playlists;

internal static class PlaylistManagementCommands
{
    internal static void RequireEditable(PlaylistSql sql, string id, bool deleting = false)
    {
        var kind = sql.Value("SELECT kind FROM playlists WHERE id=$0 AND source_provider='local'", id) as string;
        if (kind is null || kind == "system" || (!deleting && kind != "manual"))
            throw new InvalidOperationException("Select an editable local playlist.");
    }

    internal static PlaylistCommandResult State(PlaylistSql sql)
    {
        var history = new List<PlaylistRecoveryEntry>();
        using (var command = sql.Command("""
            SELECT r.id,r.playlist_id,r.name,r.action,r.created_at,
              (SELECT COUNT(*) FROM playlist_recovery_items i WHERE i.recovery_id=r.id AND role='source')
            FROM playlist_recovery r ORDER BY created_at DESC,id DESC LIMIT 20
            """))
        using (var reader = command.ExecuteReader())
            while (reader.Read()) history.Add(new(reader.GetString(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),reader.GetInt32(5),reader.GetString(4)));
        var organization = new List<PlaylistOrganization>();
        using (var command = sql.Command("""
            SELECT p.id,COALESCE(o.pinned,0),COALESCE(o.position,10000),g.group_id
            FROM playlists p LEFT JOIN playlist_organization o ON o.playlist_id=p.id
            LEFT JOIN playlist_group_members g ON g.playlist_id=p.id
            WHERE p.source_provider='local' AND p.kind!='system' ORDER BY o.position LIMIT 10000
            """))
        using (var reader = command.ExecuteReader())
            while (reader.Read()) organization.Add(new(reader.GetString(0),reader.GetInt32(1)!=0,reader.GetInt32(2),reader.IsDBNull(3) ? null : reader.GetString(3)));
        return new(null, [], History: history, Organization: organization, Groups: PlaylistGroupCommands.Read(sql));
    }

    internal static PlaylistCommandResult Execute(PlaylistSql sql, PlaylistManagementRequest request, string timestamp, string? backupDirectory)
    {
        var id = request.PlaylistId ?? "";
        if (request.Action == "relink") return PlaylistRelinkCommand.Execute(sql, request, timestamp);
        if (request.Action.StartsWith("group-", StringComparison.Ordinal)) return PlaylistGroupCommands.Execute(sql,request);
        if (request.Action == "restore")
            return new(PlaylistRecovery.Restore(sql, request.HistoryId ?? "", timestamp), []);
        if (request.Action == "reorder")
        {
            var ids = request.PlaylistIds ?? [];
            if (ids.Count > 10000 || ids.Distinct().Count() != ids.Count) throw new ArgumentException("Invalid playlist order.");
            for (var i=0; i<ids.Count; i++)
            {
                RequireEditable(sql, ids[i], true);
                sql.Run("INSERT INTO playlist_organization(playlist_id,position) VALUES ($0,$1) ON CONFLICT(playlist_id) DO UPDATE SET position=excluded.position", ids[i], i);
            }
            return new(null, []);
        }
        if (request.Action == "pin")
        {
            RequireEditable(sql, id, true);
            sql.Run("INSERT INTO playlist_organization(playlist_id,pinned,position) VALUES ($0,$1,10000) ON CONFLICT(playlist_id) DO UPDATE SET pinned=excluded.pinned", id, request.Pinned ? 1 : 0);
            return new(id, []);
        }
        if (request.Action == "clean") PlaylistInspection.ValidateCleanup(sql, request);
        else RequireEditable(sql, id, request.Action == "delete");
        if (request.Action == "delete")
        {
            PlaylistBackupWriter.Write(sql, id, "delete", backupDirectory, timestamp);
            var recovery = PlaylistRecovery.Save(sql, id, "delete", timestamp);
            PlaylistRecovery.Capture(sql, recovery, id, false);
            PlaylistRecovery.Prune(sql, recovery);
            sql.Run("DELETE FROM playlists WHERE id=$0", id);
            return new(id, [], HistoryId: recovery);
        }
        if (request.Action is not ("remove" or "copy" or "move" or "clean")) throw new ArgumentException("Unknown management action.");
        var items = request.ItemIds ?? [];
        if (items.Count is < 1 or > 1000 || items.Distinct().Count() != items.Count) throw new ArgumentException("Select 1 to 1000 songs.");
        sql.Run("CREATE TEMP TABLE playlist_selection(id TEXT PRIMARY KEY)");
        foreach (var item in items) sql.Run("INSERT INTO playlist_selection VALUES ($0)", item);
        if (Convert.ToInt32(sql.Value("SELECT COUNT(*) FROM playlist_items WHERE playlist_id=$0 AND id IN (SELECT id FROM playlist_selection)", id)) != items.Count)
            throw new InvalidOperationException("Selection changed. Refresh the playlist.");
        var target = request.TargetPlaylistId;
        if (request.Action is not ("remove" or "clean"))
        {
            if (string.IsNullOrWhiteSpace(target) || target == id) throw new ArgumentException("Choose another playlist.");
            RequireEditable(sql, target);
        }
        if (request.Action != "copy") PlaylistBackupWriter.Write(sql, id, "remove-item", backupDirectory, timestamp);
        var historyId = request.Action == "copy" ? null : PlaylistRecovery.Save(sql, id, request.Action, timestamp, target);
        if (historyId is not null) PlaylistRecovery.Capture(sql, historyId, id, true);
        var added = 0;
        if (target is not null && request.Action != "remove")
        {
            using var command = sql.Command("SELECT id FROM playlist_items WHERE playlist_id=$0 AND id IN (SELECT id FROM playlist_selection) ORDER BY position,added_at", id);
            using var reader = command.ExecuteReader();
            var position = Convert.ToInt32(sql.Value("SELECT COALESCE(MAX(position),-1)+1 FROM playlist_items WHERE playlist_id=$0", target));
            while (reader.Read())
            {
                var sourceItem = reader.GetString(0);
                var newId = Guid.NewGuid().ToString();
                var inserted = sql.Run("""
                    INSERT INTO playlist_items(id,playlist_id,media_type,media_id,source_provider,source_item_id,title_snapshot,artist_snapshot,album_snapshot,duration_snapshot,cover_id,position,added_at,added_from,unavailable)
                    SELECT $0,$1,media_type,media_id,source_provider,source_item_id,title_snapshot,artist_snapshot,album_snapshot,duration_snapshot,cover_id,$2,$3,'playlist',unavailable
                    FROM playlist_items s WHERE s.id=$4 AND NOT EXISTS (
                      SELECT 1 FROM playlist_items t WHERE t.playlist_id=$1 AND t.media_type=s.media_type AND t.source_provider=s.source_provider
                        AND ((s.media_id IS NOT NULL AND t.media_id=s.media_id) OR (s.media_id IS NULL AND t.source_item_id IS s.source_item_id AND t.title_snapshot IS s.title_snapshot)))
                    """, newId,target,position,timestamp,sourceItem);
                if (inserted == 0) continue;
                position++; added++;
                if (historyId is not null) sql.Run($"INSERT INTO playlist_recovery_items SELECT $0,id,{PlaylistRecovery.JsonRow(PlaylistRecovery.ItemColumns)},'target' FROM playlist_items WHERE id=$1", historyId,newId);
            }
            sql.Refresh(target,timestamp);
        }
        if (historyId is not null)
        {
            PlaylistRecovery.Prune(sql,historyId);
            sql.Run("DELETE FROM playlist_items WHERE playlist_id=$0 AND id IN (SELECT id FROM playlist_selection)", id);
            sql.Resequence(id); sql.Refresh(id,timestamp);
        }
        return new(id, [], HistoryId: historyId, AffectedCount: request.Action == "copy" ? added : items.Count);
    }
}
