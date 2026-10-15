using Echo.Core.Database;
using Echo.Core.Protocol;
using Echo.Core.Query.Sql;
using Microsoft.Data.Sqlite;

namespace Echo.Core.Query;

public static class InboxPageQuery
{
    public static CoreInboxPage Execute(ReadOnlyLibraryDatabase database, CoreInboxQuery input)
    {
        using var transaction = database.BeginDeferredRead();
        var batches = InboxBatchQuery.List(database);
        var query = InboxSelectionBuilder.Resolve(database, input, batches);
        var page = new CoreInboxPage
        {
            Batches = batches,
            SelectedBatch = query.SelectedBatch,
            Scope = query.Scope,
            Filter = query.Filter,
            Status = query.Status,
            Page = query.Page,
            PageSize = query.PageSize,
        };
        if (!query.HasTarget) return page;

        var selection = InboxSelectionBuilder.Build(query);
        var countSql = $"SELECT COUNT(*) {selection.FromSql} {selection.WhereSql}";
        page.Total = Convert.ToInt32(CachedQuery.Scalar(database, countSql, selection.Bind) ?? 0);
        var evidence = ReadPage(database, selection, query.PageSize, checked(((long)query.Page - 1) * query.PageSize));
        var ids = evidence.Select(item => item.TrackId).Distinct(StringComparer.Ordinal).ToList();
        var tracks = TrackByIdQuery.Execute(database, ids).ToDictionary(track => track.Id, StringComparer.Ordinal);
        foreach (var item in evidence)
        {
            if (!tracks.TryGetValue(item.TrackId, out var track)) continue;
            page.Items.Add(new CoreInboxItem
            {
                BatchId = item.BatchId,
                AddedAt = item.AddedAt,
                Track = track,
                Reasons = InboxReasons.For(item),
                InboxStatus = item.Status is "processed" or "ignored" ? item.Status : "pending",
            });
        }
        page.HasMore = (long)(query.Page - 1) * query.PageSize + evidence.Count < page.Total;
        page.Story = InboxAggregates.Story(database, query);
        page.Albums = InboxAggregates.Albums(database, query);
        page.Facets = InboxAggregates.Facets(database, query);
        return page;
    }

    private static List<InboxItemEvidence> ReadPage(ReadOnlyLibraryDatabase database, InboxSelection selection, int limit, long offset)
    {
        var sql = $"""
            SELECT library_inbox_items.batch_id, library_inbox_items.created_at,
                   COALESCE(library_inbox_item_states.status, 'pending'), tracks.id,
                   tracks.cover_id, covers.source_type, tracks.metadata_status,
                   tracks.embedded_metadata_status, tracks.embedded_cover_status,
                   tracks.artist, tracks.album, tracks.album_artist, tracks.field_sources_json,
                   tracks.duration, tracks.size_bytes, tracks.codec
            {selection.FromSql}
            LEFT JOIN covers ON covers.id = tracks.cover_id
            {selection.WhereSql}
            ORDER BY library_inbox_batches.finished_at DESC, library_inbox_items.position ASC
            LIMIT ? OFFSET ?
            """;
        var rows = new List<InboxItemEvidence>(limit);
        using var reader = CachedQuery.Reader(database, sql, command =>
        {
            selection.Bind(command);
            command.Add(limit);
            command.Add(offset);
        });
        while (reader.Read())
        {
            rows.Add(new InboxItemEvidence(
                Text(reader, 0) ?? string.Empty, Text(reader, 1) ?? string.Empty,
                Text(reader, 2) ?? "pending", Text(reader, 3) ?? string.Empty,
                Text(reader, 4), Text(reader, 5), Text(reader, 6), Text(reader, 7), Text(reader, 8),
                Text(reader, 9) ?? string.Empty, Text(reader, 10) ?? string.Empty,
                Text(reader, 11) ?? string.Empty, Text(reader, 12),
                reader.IsDBNull(13) ? 0 : reader.GetDouble(13),
                reader.IsDBNull(14) ? 1 : reader.GetInt64(14), Text(reader, 15)));
        }
        return rows;
    }

    private static string? Text(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
}
