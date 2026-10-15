using System.Text.Json;
using Echo.Core.Protocol;
using Echo.Core.Query.Sql;
using Microsoft.Data.Sqlite;

namespace Echo.Core.Query;

public enum PlaybackHistoryFilterKind
{
    Events,
    Stats,
    Insights,
}

public readonly record struct PlaybackHistoryFilter(string ClauseSql, List<object> Parameters)
{
    public string WhereSql => string.IsNullOrWhiteSpace(ClauseSql) ? string.Empty : $"WHERE {ClauseSql}";

    public void Bind(SqliteCommand command)
    {
        foreach (var parameter in Parameters)
        {
            command.Add(parameter);
        }
    }

    public PlaybackHistoryFilter Append(string clause, object? parameter = null)
    {
        var next = new List<object>(Parameters);
        if (parameter is not null)
        {
            next.Add(parameter);
        }

        var sql = string.IsNullOrWhiteSpace(ClauseSql) ? clause : $"{ClauseSql} AND {clause}";
        return new PlaybackHistoryFilter(sql, next);
    }

    public static bool IsUnfiltered(CorePlaybackHistoryQuery? query)
    {
        if (query is null)
        {
            return true;
        }

        return string.IsNullOrWhiteSpace(query.Search)
            && string.IsNullOrWhiteSpace(query.From)
            && string.IsNullOrWhiteSpace(query.To)
            && query.CompletedOnly != true
            && query.MediaType is not ("local" or "streaming")
            && query.ActiveRemoteSourceIds is null;
    }

    public static bool HasTimeRange(CorePlaybackHistoryQuery? query) =>
        !string.IsNullOrWhiteSpace(query?.From) || !string.IsNullOrWhiteSpace(query?.To);

    public static PlaybackHistoryFilter Build(
        CorePlaybackHistoryQuery? query,
        CoreNormalizedEntitySearch? search,
        string table,
        PlaybackHistoryFilterKind kind,
        bool includeRange = true,
        bool includeSearch = true,
        bool includeMedia = true)
    {
        var clauses = new List<string>();
        var parameters = new List<object>();
        var startedAt = kind == PlaybackHistoryFilterKind.Stats ? $"{table}.last_started_at" : $"{table}.started_at";
        var completed = kind == PlaybackHistoryFilterKind.Stats ? $"{table}.completed_count > 0" : $"{table}.completed > 0";

        if (includeSearch)
        {
            var like = SqlFragments.LikeEntityFilter(
            [
                $"{table}.title",
                $"{table}.artist",
                $"COALESCE({table}.album, '')",
                $"COALESCE({table}.title_snapshot, '')",
                $"COALESCE({table}.artist_snapshot, '')",
                $"{table}.track_path",
            ], search);
            if (!string.IsNullOrWhiteSpace(like.Sql))
            {
                clauses.Add(like.Sql);
                parameters.AddRange(like.Parameters);
            }
        }

        if (includeRange && !string.IsNullOrWhiteSpace(query?.From))
        {
            clauses.Add($"{startedAt} >= ?");
            parameters.Add(query!.From!);
        }

        if (includeRange && !string.IsNullOrWhiteSpace(query?.To))
        {
            clauses.Add($"{startedAt} < ?");
            parameters.Add(query!.To!);
        }

        if (query?.CompletedOnly == true)
        {
            clauses.Add(completed);
        }

        if (includeMedia && query?.MediaType is "local" or "streaming")
        {
            if (kind == PlaybackHistoryFilterKind.Insights)
            {
                clauses.Add($"{table}.media_type = ?");
                parameters.Add(query.MediaType);
            }
            else if (query.MediaType == "streaming")
            {
                clauses.Add($"{table}.media_type = ?");
                parameters.Add("streaming");
            }
            else
            {
                clauses.Add($"{table}.media_type != ?");
                parameters.Add("streaming");
            }
        }

        if (query?.ActiveRemoteSourceIds is not null)
        {
            clauses.Add($"""
                ({table}.media_type != 'remote' OR EXISTS (
                  SELECT 1 FROM json_each(?) AS active_source
                  WHERE substr({table}.track_path, 1, length(active_source.value) + 10)
                    = 'remote://' || active_source.value || '/'
                ))
                """);
            parameters.Add(JsonSerializer.Serialize(query.ActiveRemoteSourceIds));
        }

        return new PlaybackHistoryFilter(string.Join(" AND ", clauses), parameters);
    }
}
