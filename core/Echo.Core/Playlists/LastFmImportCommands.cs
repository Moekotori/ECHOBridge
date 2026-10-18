using Echo.Core.Maintenance;
using Echo.Core.Protocol;
using static Echo.Core.Playlists.LastFmImportIdentity;

namespace Echo.Core.Playlists;

/// <summary>A bounded page. Likes include every artist/title match; counts require one match. Versions remain significant.</summary>
public static class LastFmImportCommands
{
    public const string Capability = "lastfm.import/v1";

    public static LastFmImportResult Execute(string databasePath, List<LastFmImportTrack> tracks, CancellationToken cancellation = default)
    {
        if (tracks.Count is < 1 or > 100) throw new ArgumentException("Import pages must contain 1–100 tracks.");
        foreach (var track in tracks)
            if (string.IsNullOrWhiteSpace(track.Title) || string.IsNullOrWhiteSpace(track.Artist)
                || track.Title.Length > 2048 || track.Artist.Length > 2048
                || track.Title.Contains('\0') || track.Artist.Contains('\0') || track.PlayCount is < 0)
                throw new ArgumentException("Invalid Last.fm track.");

        using var connection = SortKeyMaintenanceSql.OpenWriter(databasePath);
        using var transaction = connection.BeginTransaction();
        using var sql = new PlaylistSql(connection, transaction);
        // Keep only this page's keys and one candidate per key. Apply likes while streaming matches.
        var wanted = new Dictionary<string, (string? Id, int Count)>(StringComparer.OrdinalIgnoreCase);
        var loved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var titles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var track in tracks)
        {
            var key = Key(track.Artist, track.Title);
            wanted.TryAdd(key, (null, 0));
            titles.Add(NormalizeText(track.Title));
            if (track.Loved) loved.Add(key);
        }
        var matched = 0; var unmatched = 0; var ambiguous = 0; var lovedAdded = 0; var countsUpdated = 0;
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
        using (var command = sql.Command("SELECT id,title,artist FROM tracks WHERE missing=0"))
        using (var reader = command.ExecuteReader())
            while (reader.Read())
            {
                cancellation.ThrowIfCancellationRequested();
                if (!titles.Contains(NormalizeText(reader.GetString(1)))) continue;
                var key = Key(reader.GetString(2), reader.GetString(1));
                if (!wanted.TryGetValue(key, out var match)) continue;
                var id = reader.GetString(0);
                wanted[key] = (id, Math.Min(2, match.Count + 1));
                if (!loved.Contains(key)) continue;
                var alreadyLiked = sql.Value("""
                    SELECT 1 FROM playlist_items i JOIN playlists p ON p.id=i.playlist_id
                    WHERE p.kind='system' AND p.source_provider='local' AND p.source_playlist_id='liked-tracks'
                      AND i.media_id=$0 AND i.media_type IN ('track','stream_track') LIMIT 1
                    """, id) is not null;
                FavoriteCommands.Execute(sql, new PlaylistCommand { MediaType = "track", MediaId = id, FavoriteAction = "set" }, timestamp);
                if (!alreadyLiked) lovedAdded++;
            }

        foreach (var track in tracks)
        {
            cancellation.ThrowIfCancellationRequested();
            var match = wanted[Key(track.Artist, track.Title)];
            if (match.Count == 0) { unmatched++; continue; }
            if (match.Count > 1 && track.PlayCount is not null) { ambiguous++; continue; }
            matched++;
            if (track.PlayCount is { } count)
                countsUpdated += sql.Run("""
                    UPDATE tracks SET play_count=MAX(COALESCE(play_count,0),$0,
                      COALESCE((SELECT SUM(completed_count) FROM playback_history_stats WHERE media_type='local' AND track_id=tracks.id),0))
                    WHERE id=$1 AND COALESCE(play_count,0)<MAX($0,
                      COALESCE((SELECT SUM(completed_count) FROM playback_history_stats WHERE media_type='local' AND track_id=tracks.id),0))
                    """, count, match.Id);
        }
        transaction.Commit();
        return new(matched, unmatched, ambiguous, lovedAdded, countsUpdated);
    }
}
