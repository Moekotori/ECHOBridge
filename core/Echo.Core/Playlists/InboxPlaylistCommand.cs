using Echo.Core.Protocol;
using Echo.Core.Search;
using Echo.Core.SmartPlaylists;

namespace Echo.Core.Playlists;

internal static class InboxPlaylistCommand
{
  private static readonly Dictionary<string, (string Label, string Sql)> Filters = new()
  {
    ["all"] = ("全部", "1=1"),
    ["missing_cover"] = ("缺封面", "NOT EXISTS (SELECT 1 FROM covers WHERE covers.id=tracks.cover_id AND covers.source_type!='default')"),
    ["unknown_artist"] = ("未知艺人", "lower(trim(tracks.artist)) IN ('','unknown','unknown artist')"),
    ["unknown_album"] = ("未知专辑", "lower(trim(tracks.album)) IN ('','unknown','unknown album')"),
    ["suspicious_file"] = ("疑似异常", "(tracks.duration<=0 OR tracks.size_bytes<=0 OR tracks.codec IS NULL OR trim(tracks.codec)='')"),
    ["metadata_issue"] = ("资料异常", """
            (tracks.metadata_status='fallback' OR tracks.embedded_metadata_status='error' OR tracks.embedded_cover_status='error'
             OR json_extract(tracks.field_sources_json,'$.title') IN ('unknown','filename_fallback')
             OR json_extract(tracks.field_sources_json,'$.artist') IN ('unknown','filename_fallback')
             OR json_extract(tracks.field_sources_json,'$.album') IN ('unknown','filename_fallback')
             OR json_extract(tracks.field_sources_json,'$.albumArtist') IN ('unknown','artist_fallback','filename_fallback'))
            """),
  };
  public static PlaylistCommandResult Execute(PlaylistSql sql, PlaylistCommand request, string timestamp)
  {
    var input = SmartRuleValues.Object(request.InboxInput);
    var batchId = SmartRuleValues.Clean(input["batchId"]);
    var scope = SmartRuleValues.Text(input["scope"]);
    if (scope is not ("all" or "latest" or "batch")) scope = batchId is null ? "latest" : "batch";
    var selected = scope == "batch" && batchId is not null ? batchId
        : sql.Value("SELECT id FROM library_inbox_batches ORDER BY finished_at DESC,created_at DESC LIMIT 1") as string;
    if (selected is null || sql.Value("SELECT 1 FROM library_inbox_batches WHERE id=$0", selected) is null)
      throw new ArgumentException("No new-songs inbox batch is available.");
    var filter = Filters.GetValueOrDefault(SmartRuleValues.Text(input["filter"]) ?? "all", Filters["all"]);
    var parameters = new List<object?>();
    string Bind(object? value) { var key = "$" + parameters.Count; parameters.Add(value); return key; }
    var conditions = new List<string> { "tracks.missing=0 AND tracks.library_hidden=0", filter.Sql };
    if (scope != "all") conditions.Add("library_inbox_items.batch_id=" + Bind(selected));
    foreach (var (key, column) in new[] { ("folderId", "tracks.folder_id"), ("album", "tracks.album"), ("artist", "tracks.artist") })
      if (SmartRuleValues.Clean(input[key]) is { } value) conditions.Add(column + "=" + Bind(value));
    var status = SmartRuleValues.Text(input["status"]);
    if (status is "pending" or "processed" or "ignored") conditions.Add("COALESCE(library_inbox_item_states.status,'pending')=" + Bind(status));
    var fts = LibrarySearchNormalizer.Track(SmartRuleValues.Text(input["search"]), request.CrossScriptSearch).FtsMatch;
    if (fts is not null) conditions.Add("tracks_fts MATCH " + Bind(fts));
    var selection = $"""
            FROM library_inbox_items INNER JOIN library_inbox_batches ON library_inbox_batches.id=library_inbox_items.batch_id
            INNER JOIN tracks ON tracks.id=library_inbox_items.track_id
            LEFT JOIN library_inbox_item_states ON library_inbox_item_states.batch_id=library_inbox_items.batch_id
              AND library_inbox_item_states.track_id=library_inbox_items.track_id
            {(fts is null ? "" : "INNER JOIN tracks_fts ON tracks_fts.rowid=tracks.rowid")}
            WHERE {string.Join(" AND ", conditions)}
            """;
    var matched = Convert.ToInt64(sql.Value("SELECT COUNT(*) " + selection, parameters.ToArray()));
    var ids = new List<string>();
    using (var command = sql.Command("SELECT tracks.id " + selection + " ORDER BY library_inbox_batches.finished_at DESC,library_inbox_items.position ASC LIMIT 1000", parameters.ToArray()))
    using (var reader = command.ExecuteReader()) { while (reader.Read()) ids.Add(reader.GetString(0)); }
    if (ids.Count == 0) throw new ArgumentException("No inbox tracks match the current filter.");
    var scopeLabel = scope == "all" ? "最近新增" : sql.Value("SELECT folder_name FROM library_inbox_batches WHERE id=$0", selected) as string ?? "本次新增";
    var name = SmartRuleValues.Clean(input["name"]) ?? $"新歌收件箱 {scopeLabel} {filter.Label} {timestamp[..10]}";
    var id = Guid.NewGuid().ToString();
    sql.Run("""
            INSERT INTO playlists(id,name,description,kind,source_provider,sort_mode,item_count,created_at,updated_at)
            VALUES($0,$1,$2,'manual','local','manual',0,$3,$3)
            """, id, name, $"Created from ECHO new-songs inbox on {timestamp}.", timestamp);
    return new(id, PlaylistTrackCommands.Add(sql, id, ids, timestamp), MatchedCount: matched);
  }
}
