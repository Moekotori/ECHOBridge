using Echo.Core.Protocol;
using Echo.Core.Maintenance;

namespace Echo.Core.Playlists;

public static class FavoriteCommands
{
  // Idempotent preparation has no user command receipt and never fills the
  // command recovery journal just because a page is opened repeatedly.
  public static PlaylistCommandResult Prepare(string databasePath, string? mediaType)
  {
    if (mediaType is not ("track" or "album")) throw new ArgumentException("Invalid favorite media type.");
    using var connection = SortKeyMaintenanceSql.OpenWriter(databasePath);
    using var transaction = connection.BeginTransaction();
    var sql = new PlaylistSql(connection, transaction);
    var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
    var id = EnsurePlaylist(sql, mediaType == "album", timestamp);
    if (mediaType == "album") FavoriteAlbumRepair.Execute(sql, id, timestamp);
    transaction.Commit();
    return new(id, []);
  }

  internal static PlaylistCommandResult Execute(PlaylistSql sql, PlaylistCommand request, string timestamp)
  {
    var album = request.MediaType == "album";
    if (!album && request.MediaType != "track") throw new ArgumentException("Favorite media type must be track or album.");
    var id = EnsurePlaylist(sql, album, timestamp);
    if (album) FavoriteAlbumRepair.Execute(sql, id, timestamp);
    if (request.FavoriteAction == "clear")
    {
      if (string.IsNullOrEmpty(request.SourceProvider)) sql.Run("DELETE FROM playlist_items WHERE playlist_id=$0", id);
      else sql.Run("DELETE FROM playlist_items WHERE playlist_id=$0 AND source_provider=$1", id, request.SourceProvider);
      sql.Resequence(id); sql.Refresh(id, timestamp);
      return new(id, [], Liked: false);
    }
    if (string.IsNullOrWhiteSpace(request.MediaId)) throw new ArgumentException("Favorite media ID is required.");
    if (request.FavoriteAction is not ("set" or "unset" or "toggle")) throw new ArgumentException("Unknown favorite action.");
    var mediaFilter = album ? "media_type='album'" : "media_type IN ('track','stream_track')";
    var existing = sql.Value($"SELECT id FROM playlist_items WHERE playlist_id=$0 AND media_id=$1 AND {mediaFilter} LIMIT 1", id, request.MediaId) as string;
    if (request.FavoriteAction == "unset" || request.FavoriteAction == "toggle" && existing is not null)
    {
      var removed = sql.Run($"DELETE FROM playlist_items WHERE playlist_id=$0 AND media_id=$1 AND {mediaFilter}", id, request.MediaId);
      if (removed > 0) { sql.Resequence(id); sql.Refresh(id, timestamp); }
      return new(id, [], Liked: false);
    }
    if (existing is not null) return new(id, [existing], Liked: true);
    var itemId = Guid.NewGuid().ToString();
    var position = Convert.ToInt64(sql.Value("SELECT COALESCE(MAX(position),-1)+1 FROM playlist_items WHERE playlist_id=$0", id));
    var table = album ? "albums" : "tracks";
    var artist = album ? "album_artist" : "artist";
    var albumTitle = album ? "title" : "album";
    var inserted = sql.Run($"""
            INSERT INTO playlist_items (id,playlist_id,media_type,media_id,source_provider,source_item_id,
              title_snapshot,artist_snapshot,album_snapshot,duration_snapshot,cover_id,position,added_at,added_from,unavailable)
            SELECT $0,$1,$2,id,'local',NULL,title,{artist},{albumTitle},duration,cover_id,$3,$4,'liked',0 FROM {table} WHERE id=$5
            """, itemId, id, request.MediaType, position, timestamp, request.MediaId);
    if (inserted == 0)
    {
      if (album || request.RemoteTrack is not { } remote) throw new InvalidOperationException("Unknown favorite media.");
      sql.Run("""
                INSERT INTO playlist_items (id,playlist_id,media_type,media_id,source_provider,source_item_id,
                  title_snapshot,artist_snapshot,album_snapshot,duration_snapshot,cover_id,position,added_at,added_from,unavailable)
                VALUES ($0,$1,'track',$2,'remote',$3,$4,$5,$6,$7,$8,$9,$10,'liked',$11)
                """, itemId, id, request.MediaId, remote.SourceItemId ?? request.MediaId, remote.Title,
          remote.Artist, remote.Album, remote.Duration, remote.CoverId, position, timestamp, remote.Unavailable ? 1 : 0);
    }
    sql.Refresh(id, timestamp);
    return new(id, [itemId], Liked: true);
  }

  private static string EnsurePlaylist(PlaylistSql sql, bool album, string timestamp)
  {
    var sourceId = album ? "liked-albums" : "liked-tracks";
    if (sql.Value("SELECT id FROM playlists WHERE kind='system' AND source_provider='local' AND source_playlist_id=$0", sourceId) is string id) return id;
    id = Guid.NewGuid().ToString();
    sql.Run("""
            INSERT INTO playlists (id,name,description,kind,source_provider,source_playlist_id,sort_mode,item_count,created_at,updated_at)
            VALUES ($0,$1,$2,'system','local',$3,'manual',0,$4,$4)
            """, id, album ? "Liked Albums" : "Liked Songs", album ? "Albums you liked in ECHO." : "Tracks you liked in ECHO.", sourceId, timestamp);
    return id;
  }
}
