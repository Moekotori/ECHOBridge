using Echo.Core.Database;
using Echo.Core.Protocol;
using Echo.Core.Query.Sql;
using Microsoft.Data.Sqlite;

namespace Echo.Core.Query;

internal sealed record ResolvedInboxQuery(
    string? BatchId, string Scope, string Filter, string Status,
    string? FolderId, string? Album, string? Artist,
    int Page, int PageSize, string? FtsMatch, CoreInboxBatch? SelectedBatch, bool HasTarget);

internal sealed record InboxSelection(string FromSql, string WhereSql, IReadOnlyList<object?> Parameters)
{
    public void Bind(SqliteCommand command)
    {
        foreach (var value in Parameters) command.Add(value);
    }
}

internal static class InboxSelectionBuilder
{
    public const string MissingCover = "NOT EXISTS (SELECT 1 FROM covers WHERE covers.id = tracks.cover_id AND covers.source_type != 'default')";
    public const string MetadataIssue = """
        (tracks.metadata_status = 'fallback'
          OR tracks.embedded_metadata_status = 'error'
          OR tracks.embedded_cover_status = 'error'
          OR json_extract(tracks.field_sources_json, '$.title') IN ('unknown', 'filename_fallback')
          OR json_extract(tracks.field_sources_json, '$.artist') IN ('unknown', 'filename_fallback')
          OR json_extract(tracks.field_sources_json, '$.album') IN ('unknown', 'filename_fallback')
          OR json_extract(tracks.field_sources_json, '$.albumArtist') IN ('unknown', 'artist_fallback', 'filename_fallback'))
        """;
    public const string UnknownArtist = "lower(trim(tracks.artist)) IN ('', 'unknown', 'unknown artist')";
    public const string UnknownAlbum = "lower(trim(tracks.album)) IN ('', 'unknown', 'unknown album')";
    public const string Suspicious = "(tracks.duration <= 0 OR tracks.size_bytes <= 0 OR tracks.codec IS NULL OR trim(tracks.codec) = '')";

    public static ResolvedInboxQuery Resolve(ReadOnlyLibraryDatabase database, CoreInboxQuery input, List<CoreInboxBatch> batches)
    {
        var batchId = Text(input.BatchId);
        var scope = input.Scope is "latest" or "batch" or "all" ? input.Scope : batchId is not null ? "batch" : "latest";
        var filter = input.Filter is "missing_cover" or "metadata_issue" or "unknown_artist" or "unknown_album" or "suspicious_file"
            ? input.Filter : "all";
        var status = input.Status is "pending" or "processed" or "ignored" ? input.Status : "all";
        var selected = scope == "all" ? null : scope == "batch" && batchId is not null
            ? batches.Find(batch => batch.Id == batchId) ?? InboxBatchQuery.ById(database, batchId)
            : batches.FirstOrDefault();
        return new ResolvedInboxQuery(batchId, scope, filter, status, Text(input.FolderId), Text(input.Album), Text(input.Artist),
            Math.Max(1, input.Page ?? 1), Math.Clamp(input.PageSize ?? 50, 1, 100), Text(input.FtsMatch), selected,
            scope == "all" ? batches.Count > 0 : selected is not null);
    }

    public static InboxSelection Build(ResolvedInboxQuery query)
    {
        var hasSearch = query.FtsMatch is not null;
        var from = $"""
            FROM library_inbox_items
            INNER JOIN library_inbox_batches ON library_inbox_batches.id = library_inbox_items.batch_id
            INNER JOIN tracks ON tracks.id = library_inbox_items.track_id
            LEFT JOIN library_inbox_item_states
              ON library_inbox_item_states.batch_id = library_inbox_items.batch_id
             AND library_inbox_item_states.track_id = library_inbox_items.track_id
            {(hasSearch ? "INNER JOIN tracks_fts ON tracks_fts.rowid = tracks.rowid" : string.Empty)}
            """;
        var clauses = new List<string> { "tracks.missing = 0 AND tracks.library_hidden = 0", FilterSql(query.Filter) };
        var parameters = new List<object?>();
        if (query.Scope != "all" && query.SelectedBatch is not null)
        {
            clauses.Add("library_inbox_items.batch_id = ?");
            parameters.Add(query.SelectedBatch.Id);
        }
        if (query.FolderId is not null) { clauses.Add("tracks.folder_id = ?"); parameters.Add(query.FolderId); }
        if (query.Album is not null) { clauses.Add("tracks.album = ?"); parameters.Add(query.Album); }
        if (query.Artist is not null) { clauses.Add("tracks.artist = ?"); parameters.Add(query.Artist); }
        if (query.Status != "all")
        {
            clauses.Add("COALESCE(library_inbox_item_states.status, 'pending') = ?");
            parameters.Add(query.Status);
        }
        if (hasSearch) { clauses.Add("tracks_fts MATCH ?"); parameters.Add(query.FtsMatch); }
        return new InboxSelection(from, $"WHERE {string.Join(" AND ", clauses)}", parameters);
    }

    public static string FilterSql(string filter) => filter switch
    {
        "missing_cover" => MissingCover,
        "metadata_issue" => MetadataIssue,
        "unknown_artist" => UnknownArtist,
        "unknown_album" => UnknownAlbum,
        "suspicious_file" => Suspicious,
        _ => "1 = 1",
    };

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
