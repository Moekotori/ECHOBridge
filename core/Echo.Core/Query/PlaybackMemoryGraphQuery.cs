using System.Globalization;
using Echo.Core.Database;
using Echo.Core.Mapping;
using Echo.Core.Protocol;
using Echo.Core.Query.Sql;

namespace Echo.Core.Query;

public static class PlaybackMemoryGraphQuery
{
    private const int EventLimit = 800;
    private const int ComebackDays = 45;
    private const int ComebackGapDays = 14;
    private const int ForgottenDays = 45;
    private const int TransitionMaxGapSeconds = 45 * 60;
    private const string LikedPlaylistId = "liked-tracks";
    private static readonly string[] BucketIds = ["lateNight", "morning", "day", "evening"];

    public static PlaybackMemoryGraphDto Execute(
        ReadOnlyLibraryDatabase database,
        CorePlaybackHistoryQuery? query,
        CoreNormalizedEntitySearch? search)
    {
        if (!QueryCapabilities.SupportsPlaybackMemoryGraph(query))
        {
            throw new UnsupportedQueryException("getPlaybackMemoryGraph combination is not migrated.");
        }

        using var transaction = database.BeginDeferredRead();
        var filter = PlaybackHistoryFilter.Build(query, search, "history", PlaybackHistoryFilterKind.Events)
            .Append("history.ended_at IS NOT NULL");
        var events = new List<MemoryEvent>();
        using (var reader = CachedQuery.Reader(database, $"""
            SELECT
              history.id,
              history.track_id,
              history.track_path,
              history.stable_key,
              history.title_snapshot,
              history.artist_snapshot,
              history.album_snapshot,
              history.title,
              history.artist,
              history.album,
              history.cover_id,
              history.cover_snapshot,
              history.started_at,
              history.ended_at,
              history.played_seconds,
              history.duration_seconds,
              history.duration_snapshot,
              history.completed,
              COALESCE(history.stable_key, history.track_id, history.track_path) AS history_key
            FROM playback_history AS history
            {filter.WhereSql}
            ORDER BY history.started_at DESC, history.created_at DESC, history.id DESC
            LIMIT ?
            """, command =>
        {
            filter.Bind(command);
            command.Add(EventLimit);
        }))
        {
            while (reader.Read())
            {
                events.Add(MapEvent(reader));
            }
        }

        var liked = LoadLiked(database, events.Select(static item => item.TrackId).OfType<string>().Distinct().ToArray());
        foreach (var item in events)
        {
            item.IsLiked = item.TrackId is not null && liked.Contains(item.TrackId);
        }

        var aggregates = new Dictionary<string, MemoryAggregate>(StringComparer.Ordinal);
        var eventsByKey = new Dictionary<string, List<MemoryEvent>>(StringComparer.Ordinal);
        var buckets = BucketIds.ToDictionary(
            static id => id,
            static id => new MemoryBucket { Id = id },
            StringComparer.Ordinal);
        var skippedCount = 0;
        var completedCount = 0;
        var playedSeconds = 0d;
        foreach (var item in events)
        {
            var skipped = IsSkipped(item);
            if (skipped)
            {
                skippedCount += 1;
            }

            if (item.Completed)
            {
                completedCount += 1;
            }

            playedSeconds += item.PlayedSeconds;
            UpdateAggregate(aggregates, item, skipped);
            if (!eventsByKey.TryGetValue(item.Key, out var list))
            {
                list = [];
                eventsByKey[item.Key] = list;
            }

            list.Add(item);
            if (buckets.TryGetValue(BucketFor(item.StartedAt), out var bucket))
            {
                bucket.PlayCount += 1;
                bucket.CompletedCount += item.Completed ? 1 : 0;
                bucket.SkippedCount += skipped ? 1 : 0;
                bucket.PlayedSeconds += item.PlayedSeconds;
                UpdateAggregate(bucket.Tracks, item, skipped);
            }
        }

        var insights = aggregates.Values.Select(ToInsight).ToList();
        var hasRange = PlaybackHistoryFilter.HasTimeRange(query);
        var completedOnly = query?.CompletedOnly == true;
        var likedResult = hasRange ? null : ReadLikedTrack(database, search, completedOnly);
        var eventsAscending = events.OrderBy(static item => DateTimeOffset.Parse(item.StartedAt)).ToList();

        return new PlaybackMemoryGraphDto
        {
            GeneratedAt = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
            Totals = new PlaybackMemoryTotalsDto
            {
                PlayCount = events.Count,
                CompletedCount = completedCount,
                SkippedCount = skippedCount,
                PlayedSeconds = playedSeconds,
                UniqueTracks = aggregates.Count,
                TransitionCount = Math.Max(0, eventsAscending.Count - 1),
            },
            TimeBuckets = BucketIds.Select(id =>
            {
                var bucket = buckets[id];
                return new PlaybackMemoryTimeBucketDto
                {
                    Id = id,
                    PlayCount = bucket.PlayCount,
                    CompletedCount = bucket.CompletedCount,
                    SkippedCount = bucket.SkippedCount,
                    PlayedSeconds = bucket.PlayedSeconds,
                    TopTrack = TopTrack(bucket.Tracks.Values.Select(ToInsight).ToList(), "plays"),
                };
            }).ToList(),
            LateNightTrack = TopTrack(buckets["lateNight"].Tracks.Values.Select(ToInsight).ToList(), "plays"),
            ComebackTrack = ComebackTrack(eventsByKey, aggregates),
            ForgottenTrack = hasRange
                ? TopTrack(insights.Where(track => DaysSince(track.LastPlayedAt) >= ForgottenDays && track.PlayCount >= 2).ToList(), "forgotten")
                : ReadForgottenTrack(database, search, completedOnly),
            LikedTrack = likedResult?.Track ?? TopTrack(insights.Where(static track => track.IsLiked).ToList(), "plays"),
            SkippedTrack = TopTrack(insights.Where(static track => track.SkippedCount > 0).ToList(), "skips"),
            Transition = Transition(eventsAscending, aggregates),
            RecentFlow = RecentFlow(events, aggregates),
            Coverage = new PlaybackMemoryCoverageDto
            {
                RawEventCount = events.Count,
                LikedTrackMatches = likedResult?.MatchCount ?? insights.Count(static track => track.IsLiked),
                OutputDeviceHistory = false,
            },
        };
    }

    private static HashSet<string> LoadLiked(ReadOnlyLibraryDatabase database, IReadOnlyList<string> trackIds)
    {
        var liked = new HashSet<string>(StringComparer.Ordinal);
        if (trackIds.Count == 0)
        {
            return liked;
        }

        var placeholders = SqlFragments.Placeholders(trackIds.Count);
        using var reader = CachedQuery.Reader(database, $"""
            SELECT media_id
            FROM playlist_items
            WHERE playlist_id = ?
              AND media_type IN ('track', 'stream_track')
              AND media_id IN ({placeholders})
            """, command =>
        {
            command.Add(LikedPlaylistId);
            foreach (var trackId in trackIds)
            {
                command.Add(trackId);
            }
        });
        while (reader.Read())
        {
            var id = reader.IsDBNull(0) ? null : Convert.ToString(reader.GetValue(0));
            if (!string.IsNullOrWhiteSpace(id))
            {
                liked.Add(id);
            }
        }

        return liked;
    }

    private static MemoryEvent MapEvent(Microsoft.Data.Sqlite.SqliteDataReader reader)
    {
        var trackId = Text(reader, 1);
        var coverId = Text(reader, 10);
        return new MemoryEvent
        {
            Id = Text(reader, 0) ?? string.Empty,
            TrackId = trackId,
            TrackPath = Text(reader, 2) ?? string.Empty,
            Key = Text(reader, 18) ?? Text(reader, 3) ?? trackId ?? Text(reader, 2) ?? string.Empty,
            Title = Text(reader, 4) ?? Text(reader, 7) ?? string.Empty,
            Artist = Text(reader, 5) ?? Text(reader, 8) ?? string.Empty,
            Album = Text(reader, 6) ?? Text(reader, 9) ?? string.Empty,
            CoverId = coverId,
            CoverSnapshot = Text(reader, 11),
            StartedAt = Text(reader, 12) ?? string.Empty,
            EndedAt = Text(reader, 13),
            PlayedSeconds = Real(reader, 14),
            DurationSeconds = reader.IsDBNull(15) ? Real(reader, 16) : Real(reader, 15),
            Completed = Int(reader, 17) > 0,
        };
    }

    private static bool IsSkipped(MemoryEvent item)
    {
        if (item.Completed || item.EndedAt is null)
        {
            return false;
        }

        return item.DurationSeconds > 0
            ? item.PlayedSeconds / item.DurationSeconds < 0.45
            : item.PlayedSeconds < 45;
    }

    private static string BucketFor(string iso)
    {
        var hour = DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture).ToLocalTime().Hour;
        if (hour >= 22 || hour < 5)
        {
            return "lateNight";
        }

        if (hour < 11)
        {
            return "morning";
        }

        return hour < 17 ? "day" : "evening";
    }

    private static void UpdateAggregate(Dictionary<string, MemoryAggregate> aggregates, MemoryEvent item, bool skipped)
    {
        if (!aggregates.TryGetValue(item.Key, out var aggregate))
        {
            aggregate = new MemoryAggregate
            {
                Id = item.Key,
                TrackId = item.TrackId,
                Title = item.Title,
                Artist = item.Artist,
                Album = item.Album,
                CoverThumb = CoverThumb(item.CoverId, item.CoverSnapshot),
                DurationSeconds = item.DurationSeconds,
                FirstPlayedAt = item.StartedAt,
                LastPlayedAt = item.StartedAt,
                IsLiked = item.IsLiked,
                CoverId = item.CoverId,
                CoverSnapshot = item.CoverSnapshot,
            };
            aggregates[item.Key] = aggregate;
        }

        var latestStartedAt = aggregate.LastPlayedAt is null ? double.NegativeInfinity : DateTimeOffset.Parse(aggregate.LastPlayedAt).ToUnixTimeMilliseconds();
        var eventStartedAt = DateTimeOffset.Parse(item.StartedAt).ToUnixTimeMilliseconds();
        aggregate.PlayCount += 1;
        aggregate.CompletedCount += item.Completed ? 1 : 0;
        aggregate.SkippedCount += skipped ? 1 : 0;
        aggregate.PlayedSeconds += item.PlayedSeconds;
        aggregate.IsLiked = aggregate.IsLiked || item.IsLiked;
        if (item.DurationSeconds > 0)
        {
            aggregate.DurationSeconds = item.DurationSeconds;
        }

        if (aggregate.FirstPlayedAt is null || eventStartedAt < DateTimeOffset.Parse(aggregate.FirstPlayedAt).ToUnixTimeMilliseconds())
        {
            aggregate.FirstPlayedAt = item.StartedAt;
        }

        if (aggregate.LastPlayedAt is null || eventStartedAt >= latestStartedAt)
        {
            aggregate.TrackId = item.TrackId;
            aggregate.Title = item.Title;
            aggregate.Artist = item.Artist;
            aggregate.Album = item.Album;
            aggregate.CoverId = item.CoverId;
            aggregate.CoverSnapshot = item.CoverSnapshot;
            aggregate.CoverThumb = CoverThumb(item.CoverId, item.CoverSnapshot);
            aggregate.LastPlayedAt = item.StartedAt;
        }
    }

    private static string? CoverThumb(string? coverId, string? coverSnapshot) =>
        string.IsNullOrEmpty(coverId) ? coverSnapshot : LibraryText.CoverUrl(coverId, "thumb");

    private static PlaybackMemoryTrackInsightDto ToInsight(MemoryAggregate aggregate) =>
        new()
        {
            Id = aggregate.Id,
            TrackId = aggregate.TrackId,
            Title = aggregate.Title,
            Artist = aggregate.Artist,
            Album = aggregate.Album,
            CoverThumb = aggregate.CoverThumb,
            PlayCount = aggregate.PlayCount,
            CompletedCount = aggregate.CompletedCount,
            SkippedCount = aggregate.SkippedCount,
            PlayedSeconds = aggregate.PlayedSeconds,
            DurationSeconds = aggregate.DurationSeconds,
            FirstPlayedAt = aggregate.FirstPlayedAt,
            LastPlayedAt = aggregate.LastPlayedAt,
            IsLiked = aggregate.IsLiked,
        };

    private static PlaybackMemoryTrackInsightDto? TopTrack(IReadOnlyList<PlaybackMemoryTrackInsightDto> tracks, string mode)
    {
        PlaybackMemoryTrackInsightDto? best = null;
        foreach (var track in tracks)
        {
            if (best is null)
            {
                best = track;
                continue;
            }

            var better = mode switch
            {
                "skips" => track.SkippedCount > best.SkippedCount
                    || (track.SkippedCount == best.SkippedCount && track.PlayCount > best.PlayCount),
                "forgotten" => track.PlayCount > best.PlayCount
                    || (track.PlayCount == best.PlayCount && track.PlayedSeconds > best.PlayedSeconds)
                    || (track.PlayCount == best.PlayCount
                        && track.PlayedSeconds == best.PlayedSeconds
                        && DaysSince(track.LastPlayedAt) > DaysSince(best.LastPlayedAt)),
                _ => track.PlayCount > best.PlayCount
                    || (track.PlayCount == best.PlayCount && track.PlayedSeconds > best.PlayedSeconds)
                    || (track.PlayCount == best.PlayCount
                        && track.PlayedSeconds == best.PlayedSeconds
                        && ParseMs(track.LastPlayedAt) > ParseMs(best.LastPlayedAt)),
            };
            if (better)
            {
                best = track;
            }
        }

        return best;
    }

    private static List<PlaybackMemoryTrackInsightDto> RecentFlow(
        IReadOnlyList<MemoryEvent> eventsDescending,
        Dictionary<string, MemoryAggregate> aggregates)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var flow = new List<PlaybackMemoryTrackInsightDto>();
        foreach (var item in eventsDescending)
        {
            if (!seen.Add(item.Key) || !aggregates.TryGetValue(item.Key, out var aggregate))
            {
                continue;
            }

            flow.Add(ToInsight(aggregate));
            if (flow.Count >= 6)
            {
                break;
            }
        }

        flow.Reverse();
        return flow;
    }

    private static PlaybackMemoryTrackInsightDto? ComebackTrack(
        Dictionary<string, List<MemoryEvent>> eventsByKey,
        Dictionary<string, MemoryAggregate> aggregates)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        PlaybackMemoryTrackInsightDto? best = null;
        var bestGap = double.NegativeInfinity;
        foreach (var (key, items) in eventsByKey)
        {
            if (items.Count < 2 || !aggregates.TryGetValue(key, out var aggregate))
            {
                continue;
            }

            var ordered = items.OrderBy(static item => DateTimeOffset.Parse(item.StartedAt)).ToList();
            var latest = ordered[^1];
            var previous = ordered[^2];
            var latestAt = DateTimeOffset.Parse(latest.StartedAt).ToUnixTimeMilliseconds();
            var previousAt = DateTimeOffset.Parse(previous.StartedAt).ToUnixTimeMilliseconds();
            var recentDays = (now - latestAt) / 86_400_000d;
            var gapDays = (latestAt - previousAt) / 86_400_000d;
            if (recentDays > ComebackDays || gapDays < ComebackGapDays)
            {
                continue;
            }

            var track = ToInsight(aggregate);
            if (best is null || gapDays > bestGap || (gapDays == bestGap && track.PlayCount > best.PlayCount))
            {
                best = track;
                bestGap = gapDays;
            }
        }

        return best;
    }

    private static PlaybackMemoryTransitionDto? Transition(
        IReadOnlyList<MemoryEvent> eventsAscending,
        Dictionary<string, MemoryAggregate> aggregates)
    {
        var transitions = new Dictionary<string, MemoryTransition>(StringComparer.Ordinal);
        for (var index = 0; index < eventsAscending.Count - 1; index += 1)
        {
            var from = eventsAscending[index];
            var to = eventsAscending[index + 1];
            if (from.Key == to.Key)
            {
                continue;
            }

            var gapSeconds = (DateTimeOffset.Parse(to.StartedAt) - DateTimeOffset.Parse(from.StartedAt)).TotalSeconds;
            if (!double.IsFinite(gapSeconds) || gapSeconds < 0 || gapSeconds > TransitionMaxGapSeconds)
            {
                continue;
            }

            var id = $"{from.Key}\u001f{to.Key}";
            if (!transitions.TryGetValue(id, out var transition))
            {
                transition = new MemoryTransition { FromKey = from.Key, ToKey = to.Key };
                transitions[id] = transition;
            }

            transition.Count += 1;
            transition.TotalGapSeconds += gapSeconds;
            if (transition.LastPlayedAt is null || DateTimeOffset.Parse(to.StartedAt) > DateTimeOffset.Parse(transition.LastPlayedAt))
            {
                transition.LastPlayedAt = to.StartedAt;
            }
        }

        MemoryTransition? best = null;
        string? bestId = null;
        foreach (var (id, transition) in transitions)
        {
            if (best is null
                || transition.Count > best.Count
                || (transition.Count == best.Count && ParseMs(transition.LastPlayedAt) > ParseMs(best.LastPlayedAt)))
            {
                best = transition;
                bestId = id;
            }
        }

        if (best is null || bestId is null
            || !aggregates.TryGetValue(best.FromKey, out var fromAggregate)
            || !aggregates.TryGetValue(best.ToKey, out var toAggregate))
        {
            return null;
        }

        return new PlaybackMemoryTransitionDto
        {
            Id = bestId,
            From = ToInsight(fromAggregate),
            To = ToInsight(toAggregate),
            Count = best.Count,
            AverageGapSeconds = best.Count > 0 ? best.TotalGapSeconds / best.Count : 0,
            LastPlayedAt = best.LastPlayedAt,
        };
    }

    private static PlaybackMemoryTrackInsightDto? ReadForgottenTrack(
        ReadOnlyLibraryDatabase database,
        CoreNormalizedEntitySearch? search,
        bool completedOnly)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-ForgottenDays).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        var filter = PlaybackHistoryFilter.Build(
            new CorePlaybackHistoryQuery { CompletedOnly = completedOnly },
            search,
            "history",
            PlaybackHistoryFilterKind.Stats,
            includeRange: false)
            .Append("history.play_count >= 2")
            .Append("history.last_started_at < ?", cutoff);
        using var reader = CachedQuery.Reader(database, $"""
            SELECT
              history.history_key,
              history.track_id,
              history.title_snapshot,
              history.artist_snapshot,
              history.album_snapshot,
              history.title,
              history.artist,
              history.album,
              history.cover_id,
              history.cover_snapshot,
              history.duration_seconds,
              history.play_count,
              history.completed_count,
              history.total_played_seconds,
              history.last_started_at
            FROM playback_history_stats AS history
            {filter.WhereSql}
            ORDER BY history.play_count DESC, history.total_played_seconds DESC, history.last_started_at ASC
            LIMIT 1
            """, filter.Bind);
        return reader.Read() ? MapStatsInsight(reader, false) : null;
    }

    private static (int MatchCount, PlaybackMemoryTrackInsightDto? Track)? ReadLikedTrack(
        ReadOnlyLibraryDatabase database,
        CoreNormalizedEntitySearch? search,
        bool completedOnly)
    {
        var filter = PlaybackHistoryFilter.Build(
            new CorePlaybackHistoryQuery { CompletedOnly = completedOnly },
            search,
            "history",
            PlaybackHistoryFilterKind.Stats,
            includeRange: false)
            .Append("playlist_items.playlist_id = ?", LikedPlaylistId)
            .Append("playlist_items.media_type IN ('track', 'stream_track')")
            .Append("playlist_items.media_id = history.track_id");
        var matchCount = Convert.ToInt32(CachedQuery.Scalar(database, $"""
            SELECT COUNT(DISTINCT history.history_key)
            FROM playback_history_stats AS history
            INNER JOIN playlist_items ON playlist_items.media_id = history.track_id
            {filter.WhereSql}
            """, filter.Bind) ?? 0);
        using var reader = CachedQuery.Reader(database, $"""
            SELECT
              history.history_key,
              history.track_id,
              history.title_snapshot,
              history.artist_snapshot,
              history.album_snapshot,
              history.title,
              history.artist,
              history.album,
              history.cover_id,
              history.cover_snapshot,
              history.duration_seconds,
              history.play_count,
              history.completed_count,
              history.total_played_seconds,
              history.last_started_at
            FROM playback_history_stats AS history
            INNER JOIN playlist_items ON playlist_items.media_id = history.track_id
            {filter.WhereSql}
            ORDER BY history.play_count DESC, history.total_played_seconds DESC, history.last_started_at DESC
            LIMIT 1
            """, filter.Bind);
        return (matchCount, reader.Read() ? MapStatsInsight(reader, true) : null);
    }

    private static PlaybackMemoryTrackInsightDto MapStatsInsight(Microsoft.Data.Sqlite.SqliteDataReader reader, bool isLiked)
    {
        var playCount = Int(reader, 11);
        var completedCount = Int(reader, 12);
        var coverId = Text(reader, 8);
        return new PlaybackMemoryTrackInsightDto
        {
            Id = Text(reader, 0) ?? string.Empty,
            TrackId = Text(reader, 1),
            Title = Text(reader, 2) ?? Text(reader, 5) ?? string.Empty,
            Artist = Text(reader, 3) ?? Text(reader, 6) ?? string.Empty,
            Album = Text(reader, 4) ?? Text(reader, 7) ?? string.Empty,
            CoverThumb = CoverThumb(coverId, Text(reader, 9)),
            PlayCount = playCount,
            CompletedCount = completedCount,
            SkippedCount = Math.Max(0, playCount - completedCount),
            PlayedSeconds = Real(reader, 13),
            DurationSeconds = Real(reader, 10),
            FirstPlayedAt = null,
            LastPlayedAt = Text(reader, 14),
            IsLiked = isLiked,
        };
    }

    private static double DaysSince(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso) || !DateTimeOffset.TryParse(iso, out var parsed))
        {
            return double.PositiveInfinity;
        }

        return Math.Max(0, (DateTimeOffset.UtcNow - parsed).TotalDays);
    }

    private static double ParseMs(string? iso) =>
        DateTimeOffset.TryParse(iso, out var parsed) ? parsed.ToUnixTimeMilliseconds() : double.NegativeInfinity;

    private static string? Text(Microsoft.Data.Sqlite.SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : Convert.ToString(reader.GetValue(ordinal));

    private static int Int(Microsoft.Data.Sqlite.SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetValue(ordinal));

    private static double Real(Microsoft.Data.Sqlite.SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? 0 : Convert.ToDouble(reader.GetValue(ordinal));

    private sealed class MemoryEvent
    {
        public string Id { get; set; } = string.Empty;
        public string? TrackId { get; set; }
        public string TrackPath { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string Album { get; set; } = string.Empty;
        public string? CoverId { get; set; }
        public string? CoverSnapshot { get; set; }
        public string StartedAt { get; set; } = string.Empty;
        public string? EndedAt { get; set; }
        public double PlayedSeconds { get; set; }
        public double DurationSeconds { get; set; }
        public bool Completed { get; set; }
        public bool IsLiked { get; set; }
    }

    private sealed class MemoryAggregate
    {
        public string Id { get; set; } = string.Empty;
        public string? TrackId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string Album { get; set; } = string.Empty;
        public string? CoverThumb { get; set; }
        public int PlayCount { get; set; }
        public int CompletedCount { get; set; }
        public int SkippedCount { get; set; }
        public double PlayedSeconds { get; set; }
        public double DurationSeconds { get; set; }
        public string? FirstPlayedAt { get; set; }
        public string? LastPlayedAt { get; set; }
        public bool IsLiked { get; set; }
        public string? CoverId { get; set; }
        public string? CoverSnapshot { get; set; }
    }

    private sealed class MemoryBucket
    {
        public string Id { get; set; } = string.Empty;
        public int PlayCount { get; set; }
        public int CompletedCount { get; set; }
        public int SkippedCount { get; set; }
        public double PlayedSeconds { get; set; }
        public Dictionary<string, MemoryAggregate> Tracks { get; } = new(StringComparer.Ordinal);
    }

    private sealed class MemoryTransition
    {
        public string FromKey { get; set; } = string.Empty;
        public string ToKey { get; set; } = string.Empty;
        public int Count { get; set; }
        public double TotalGapSeconds { get; set; }
        public string? LastPlayedAt { get; set; }
    }
}
