namespace Echo.Core.Query.Sql;

internal static class IndexedTrackTitleSql
{
    // Apply OFFSET to the narrow covering index, not to hydrated track rows.
    // MATERIALIZED bounds the join input to one page and preserves duplicate filtering.
    public static string Order(bool descending) => descending
        ? "sort_keys.title_key COLLATE NOCASE DESC, sort_keys.title_text COLLATE NOCASE DESC, sort_keys.artist_key COLLATE NOCASE ASC, sort_keys.artist_text COLLATE NOCASE ASC, sort_keys.track_id ASC"
        : TrackOrderSql.IndexedTitleOrder;

    public static string Page(bool list, string duplicateJoin, string filterSql, bool descending = false) => $"""
        WITH page_ids AS MATERIALIZED (
            SELECT sort_keys.track_id, sort_keys.title_key, sort_keys.title_text,
                   sort_keys.artist_key, sort_keys.artist_text
            FROM local_track_sort_keys AS sort_keys INDEXED BY idx_local_track_sort_keys_title
            {duplicateJoin}
            {filterSql}
            ORDER BY {Order(descending)}
            LIMIT ? OFFSET ?
        )
        SELECT {TrackSelectSql.ForList(list)}
        FROM page_ids AS sort_keys
        INNER JOIN tracks ON tracks.id = sort_keys.track_id
        ORDER BY {Order(descending)}
        """;
}
