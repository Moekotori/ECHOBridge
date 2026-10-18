using Echo.Core.Protocol;

namespace Echo.Core.Playlists;

internal static class PlaylistRelinkCommand
{
    internal const string Capability = "playlists.relink/v1";
    internal static PlaylistCommandResult Execute(PlaylistSql sql, PlaylistManagementRequest request, string timestamp)
    {
        var playlistId = request.PlaylistId ?? "";
        PlaylistManagementCommands.RequireEditable(sql, playlistId);
        var itemId = request.ItemId ?? "";
        var trackId = request.TrackId ?? "";
        if (sql.Value("""
            SELECT 1 FROM playlist_items i LEFT JOIN tracks t ON t.id=i.media_id
            WHERE i.id=$0 AND i.playlist_id=$1 AND i.media_type='track' AND i.source_provider='local'
              AND (i.unavailable=1 OR t.id IS NULL OR t.missing=1)
            """, itemId, playlistId) is null)
            throw new InvalidOperationException("Missing playlist item changed. Refresh the playlist.");
        if (sql.Value("SELECT 1 FROM tracks WHERE id=$0 AND missing=0", trackId) is null)
            throw new InvalidOperationException("Select an available local song.");
        var recovery = PlaylistRecovery.Save(sql, playlistId, "relink", timestamp);
        PlaylistRecovery.Capture(sql, recovery, playlistId, false);
        PlaylistRecovery.Prune(sql, recovery);
        sql.Run("""
            UPDATE playlist_items SET media_id=$0,unavailable=0,
              cover_id=COALESCE(cover_id,(SELECT cover_id FROM tracks WHERE id=$0)),
              added_from='steam-cloud-linked'
            WHERE id=$1 AND playlist_id=$2
            """, trackId, itemId, playlistId);
        sql.Refresh(playlistId, timestamp);
        return new(playlistId, [itemId], HistoryId: recovery, AffectedCount: 1);
    }
}
