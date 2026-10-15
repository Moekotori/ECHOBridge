using Echo.Core.Database;
using Echo.Core.Protocol;
using Echo.Core.Query.Sql;

namespace Echo.Core.Query;

public sealed record PlaylistResultPage(LibraryPlaylistDto? Playlist, List<LibraryPlaylistItemDto> Items);

public static class PlaylistCommandResultQuery
{
  public static PlaylistResultPage Execute(ReadOnlyLibraryDatabase database, string? id, List<string>? itemIds)
  {
    if (itemIds is { Count: > 256 }) throw new ArgumentException("At most 256 item IDs per result page.");
    using var transaction = database.BeginDeferredRead();
    LibraryPlaylistDto? playlist = null;
    if (id is not null)
    {
      using var reader = CachedQuery.Reader(database, """
                SELECT id,name,description,kind,source_provider,source_playlist_id,cover_id,cover_url,sort_mode,item_count,created_at,updated_at
                FROM playlists WHERE id=?
                """, command => command.Add(id));
      if (reader.Read()) playlist = PlaylistListQuery.Map(reader);
    }
    if (playlist is not null)
    {
      Echo.Core.SmartPlaylists.SmartPlaylistQueries.Enrich(database, playlist);
      PlaylistCoverQuery.Enrich(database, playlist);
    }
    if (itemIds is not { Count: > 0 }) return new(playlist, []);
    var rows = new Dictionary<string, PlaylistItemsQuery.ItemRow>(StringComparer.Ordinal);
    var placeholders = string.Join(',', itemIds.Select(_ => "?"));
    using (var reader = CachedQuery.Reader(database, PlaylistItemsQuery.ItemSelectSql + $" WHERE playlist_items.id IN ({placeholders})",
        command => { foreach (var item in itemIds) command.Add(item); }))
    {
      while (reader.Read()) { var row = PlaylistItemsQuery.ReadRow(reader); rows[row.Id] = row; }
    }
    var tracks = TrackPageQuery.HydrateTracks(database, rows.Values.Where(row => row.HasLocalTrack)
        .Select(row => row.TrackId!).Distinct(StringComparer.Ordinal).ToList(), list: false)
        .Cast<LibraryTrackDto>().ToDictionary(track => track.Id, StringComparer.Ordinal);
    return new(playlist, itemIds.Where(rows.ContainsKey).Select(item => PlaylistItemsQuery.MapItem(rows[item], tracks)).ToList());
  }
}
