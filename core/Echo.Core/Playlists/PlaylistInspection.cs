using Echo.Core.Protocol;

namespace Echo.Core.Playlists;

/// <summary>Bounded identity checks. Filesystem verification belongs to Electron, outside SQLite transactions.</summary>
internal static class PlaylistInspection
{
    private const string DuplicateSql = """
        EXISTS (SELECT 1 FROM playlist_items earlier
          WHERE earlier.playlist_id=i.playlist_id AND earlier.rowid<i.rowid
            AND earlier.media_type=i.media_type AND earlier.source_provider=i.source_provider
            AND ((i.media_id IS NOT NULL AND earlier.media_id=i.media_id)
              OR (i.media_id IS NULL AND i.source_item_id IS NOT NULL AND earlier.source_item_id=i.source_item_id)))
        """;

    internal static void RequireSupported(PlaylistSql sql, string? id)
    {
        if (sql.Value("""
            SELECT 1 FROM playlists WHERE id=$0 AND source_provider='local'
              AND (kind='manual' OR (kind='system' AND source_playlist_id='liked-tracks'))
            """,id) is null) throw new InvalidOperationException("Choose a local manual playlist or Favorite Songs.");
    }

    internal static PlaylistCommandResult Read(PlaylistSql sql, PlaylistManagementRequest request)
    {
        RequireSupported(sql,request.PlaylistId);
        if (request.Cursor < 0 || request.ItemIds is { Count: > 100 }) throw new ArgumentException("Invalid inspection page.");
        var selection = request.ItemIds;
        var parameters = new List<object?> { request.PlaylistId,request.Cursor };
        var where = "i.playlist_id=$0 AND i.rowid>$1";
        if (selection is not null)
        {
            if (selection.Count == 0) return new(request.PlaylistId,[],Inspection:new([],request.Cursor,false,0));
            where += " AND i.id IN (" + string.Join(",",selection.Select((_,index) => "$"+(index+2))) + ")";
            parameters.AddRange(selection);
        }
        var items = new List<PlaylistInspectionItem>();
        var cursor = request.Cursor;
        var hasMore = false;
        using var command = sql.Command($"""
            SELECT i.rowid,i.id,COALESCE(t.title,i.title_snapshot,'Unknown track'),
              COALESCE(t.artist,i.artist_snapshot,''),{DuplicateSql},i.source_provider,
              t.path,t.id,i.unavailable,i.media_type
            FROM playlist_items i LEFT JOIN tracks t ON t.id=i.media_id AND i.source_provider='local'
            WHERE {where} ORDER BY i.rowid LIMIT 101
            """,parameters.ToArray());
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (items.Count == 100) { hasMore = true; break; }
            cursor = reader.GetInt64(0);
            var local = reader.GetString(5) == "local" && reader.GetString(9) == "track";
            var availability = local && reader.IsDBNull(7) ? "missing" : reader.GetInt32(8)!=0 ? "unavailable" : local ? "available" : "unchecked";
            items.Add(new(reader.GetString(1),reader.GetString(2),reader.GetString(3),reader.GetInt32(4)!=0,availability,
                local && !reader.IsDBNull(6) ? reader.GetString(6) : null));
        }
        return new(request.PlaylistId,[],Inspection:new(items,cursor,hasMore,items.Count));
    }

    internal static void ValidateCleanup(PlaylistSql sql, PlaylistManagementRequest request)
    {
        RequireSupported(sql,request.PlaylistId);
        var items = request.ItemIds ?? [];
        if (items.Count is < 1 or > 500) throw new ArgumentException("Select 1 to 500 inspection results.");
        var missing = new HashSet<string>(request.MissingItemIds ?? []);
        foreach (var item in items)
        {
            // Recheck duplicates at commit time so a removed keeper cannot cause the last copy to be deleted.
            if (sql.Value($"""
                SELECT 1 FROM playlist_items i LEFT JOIN tracks t ON t.id=i.media_id AND i.source_provider='local'
                WHERE i.playlist_id=$0 AND i.id=$1 AND ({DuplicateSql} OR i.unavailable=1
                  OR (i.source_provider='local' AND i.media_type='track' AND t.id IS NULL))
                """,request.PlaylistId,item) is null && !missing.Contains(item))
                throw new InvalidOperationException("Inspection results changed. Check the playlist again before removing songs.");
        }
    }
}
