using Echo.Core.Protocol;

namespace Echo.Core.Query;

public static class PlaybackHistoryOrder
{
    // Keep the Core reader and the TypeScript fallback on the same fixed expressions.
    public static string Build(CorePlaybackHistoryQuery? query, string kind)
    {
        var direction = query?.SortDirection == "asc" ? "ASC" : "DESC";
        var recent = kind == "records" ? "started_at" : "last_started_at";
        var count = kind == "stats" ? "play_count" : "history_play_count";
        var duration = kind == "stats" ? "total_played_seconds" : kind == "window" ? "history_played_seconds_total" : "played_seconds";
        var tie = kind == "records" ? "created_at DESC, id DESC" : "history_key ASC";
        return query?.Sort switch
        {
            "title" => $"COALESCE(title_snapshot, title, '') COLLATE NOCASE {direction}, {recent} DESC, {tie}",
            "artist" => $"COALESCE(artist_snapshot, artist, '') COLLATE NOCASE {direction}, COALESCE(title_snapshot, title, '') COLLATE NOCASE ASC, {recent} DESC, {tie}",
            "duration" => $"{duration} {direction}, {recent} DESC, {tie}",
            "recent" => $"{recent} {direction}, {(kind == "records" ? "" : $"{count} DESC, ")}{tie}",
            _ => kind == "records" ? $"{recent} {direction}, {tie}" : $"{count} {direction}, {recent} DESC, {tie}",
        };
    }
}
