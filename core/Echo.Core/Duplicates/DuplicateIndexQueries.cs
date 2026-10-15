using System.Text.Json;
using Echo.Core.Database;
using Echo.Core.Mapping;
using Echo.Core.Protocol;
using Microsoft.Data.Sqlite;

namespace Echo.Core.Duplicates;

public static class DuplicateIndexQueries
{
    private const int PageSize = 100;
    private const string MemberSelect = """
        SELECT m.rowid AS duplicate_cursor, g.id AS group_id, g.duplicate_key, g.track_count,
               g.confidence, m.quality_score, m.rank, m.hidden,
               m.reasons_json AS member_reasons_json, t.size_bytes AS member_size_bytes, t.*
        FROM duplicate_track_members m
        JOIN duplicate_track_groups g ON g.id=m.group_id
        JOIN tracks t ON t.id=m.track_id AND t.missing=0 AND t.library_hidden=0
        """;

    public static DuplicateIndexSummary Summary(ReadOnlyLibraryDatabase database, string? mode)
    {
        _ = mode;
        var scanned = CountVisibleTracks(database.RequireConnection());
        using var command = database.RequireConnection().CreateCommand();
        command.CommandText = """
            SELECT COUNT(*), COALESCE(SUM(track_count),0), COALESCE(SUM(hidden_count),0), MAX(updated_at)
            FROM duplicate_track_groups WHERE mode='strict'
            """;
        using var rows = command.ExecuteReader();
        rows.Read();
        return new DuplicateIndexSummary("strict", scanned, rows.GetInt32(0), rows.GetInt32(1),
            rows.GetInt32(2), rows.IsDBNull(3) ? "" : rows.GetString(3));
    }

    private static int CountVisibleTracks(SqliteConnection connection)
    {
        // Present-track maintenance counts every file still on disk, including
        // tracks the user hid from the library. Hidden rows are the small side.
        return Math.Max(0, CountPresentTracks(connection) - CountHiddenPresentTracks(connection));
    }

    private static int CountHiddenPresentTracks(SqliteConnection connection)
    {
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM tracks WHERE library_hidden = 1 AND missing = 0";
            return Convert.ToInt32(command.ExecuteScalar());
        }
        catch (SqliteException)
        {
            // Fixtures created before library_hidden have nothing to subtract.
            return 0;
        }
    }

    private static int CountPresentTracks(SqliteConnection connection)
    {
        // One stored integer when sort keys are synced. Otherwise count the
        // narrow partial index. Neither path keeps a second connection or a
        // copy of the track list.
        try
        {
            using var flag = connection.CreateCommand();
            flag.CommandText = "SELECT value FROM library_maintenance_flags WHERE key=$key";
            flag.Parameters.AddWithValue("$key", ProtocolConstants.TrackPresentCountFlagKey);
            if (flag.ExecuteScalar() is string text && int.TryParse(text, out var parsed) && parsed >= 0)
            {
                return parsed;
            }
        }
        catch (SqliteException)
        {
            // Fixtures without the maintenance table still count tracks directly.
        }

        try
        {
            using var indexed = connection.CreateCommand();
            indexed.CommandText = "SELECT COUNT(*) FROM tracks INDEXED BY idx_tracks_sample_rate WHERE missing = 0";
            return Convert.ToInt32(indexed.ExecuteScalar());
        }
        catch (SqliteException)
        {
            using var count = connection.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM tracks WHERE missing = 0";
            return Convert.ToInt32(count.ExecuteScalar());
        }
    }

    public static Dictionary<string, int> HiddenCounts(ReadOnlyLibraryDatabase database, List<string>? ids, string? mode)
    {
        _ = mode;
        if (ids is null || ids.Count > ProtocolConstants.MaxTrackIds)
            throw new ProtocolLimitException("Duplicate hidden counts require at most 500 track IDs.");
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var id in ids)
            if (!string.IsNullOrEmpty(id)) result[id] = 0;
        if (result.Count == 0) return result;
        using var command = database.RequireConnection().CreateCommand();
        var names = new List<string>();
        foreach (var id in result.Keys)
        {
            var name = "$id" + names.Count;
            names.Add(name);
            command.Parameters.AddWithValue(name, id);
        }
        command.CommandText = $"""
            SELECT m.track_id, MAX(g.hidden_count)
            FROM duplicate_track_members m JOIN duplicate_track_groups g ON g.id=m.group_id
            WHERE g.mode='strict' AND m.track_id IN ({string.Join(",", names)}) GROUP BY m.track_id
            """;
        using var rows = command.ExecuteReader();
        while (rows.Read()) result[rows.GetString(0)] = rows.GetInt32(1);
        return result;
    }

    public static DuplicateMemberPage Versions(ReadOnlyLibraryDatabase database, string? trackId, long cursor)
    {
        if (string.IsNullOrEmpty(trackId)) throw new ArgumentException("trackId is required.");
        using var command = database.RequireConnection().CreateCommand();
        command.CommandText = MemberSelect + "\n" + """
            WHERE g.id=(SELECT g2.id FROM duplicate_track_members m2
              JOIN duplicate_track_groups g2 ON g2.id=m2.group_id
              WHERE m2.track_id=$trackId ORDER BY g2.updated_at DESC LIMIT 1)
              AND m.rowid>$cursor
            ORDER BY m.rowid LIMIT $limit
            """;
        command.Parameters.AddWithValue("$trackId", trackId);
        command.Parameters.AddWithValue("$cursor", Math.Max(0, cursor));
        command.Parameters.AddWithValue("$limit", PageSize + 1);
        return ReadPage(command, cursor);
    }

    public static DuplicateMemberPage Preview(ReadOnlyLibraryDatabase database, string? mode, string? revision, long cursor)
    {
        _ = mode;
        if (revision is null) throw new ArgumentException("Duplicate preview revision is required.");
        using var command = database.RequireConnection().CreateCommand();
        command.CommandText = MemberSelect + "\n" + """
            WHERE g.mode='strict' AND g.updated_at=$revision AND m.rowid>$cursor
            ORDER BY m.rowid LIMIT $limit
            """;
        command.Parameters.AddWithValue("$revision", revision);
        command.Parameters.AddWithValue("$cursor", Math.Max(0, cursor));
        command.Parameters.AddWithValue("$limit", PageSize + 1);
        return ReadPage(command, cursor);
    }

    private static DuplicateMemberPage ReadPage(SqliteCommand command, long cursor)
    {
        using var rows = command.ExecuteReader();
        var mapTrack = TrackRowMapper.Bind(rows);
        var items = new List<DuplicateMemberRow>(PageSize);
        var nextCursor = cursor;
        while (rows.Read())
        {
            if (items.Count == PageSize) return new DuplicateMemberPage(items, nextCursor, true);
            nextCursor = rows.GetInt64(0);
            items.Add(new DuplicateMemberRow
            {
                GroupId = rows.GetString(1),
                DuplicateKey = rows.GetString(2),
                TrackCount = rows.GetInt32(3),
                Confidence = rows.GetDouble(4),
                QualityScore = rows.GetDouble(5),
                Rank = rows.GetInt32(6),
                Hidden = rows.GetInt32(7) == 1,
                Reasons = ParseReasons(rows.IsDBNull(8) ? null : rows.GetString(8)),
                SizeBytes = rows.IsDBNull(9) ? null : rows.GetInt64(9),
                Track = mapTrack.Map(),
            });
        }
        return new DuplicateMemberPage(items, nextCursor, false);
    }

    private static List<string> ParseReasons(string? json)
    {
        if (string.IsNullOrEmpty(json)) return [];
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString()!).ToList()
                : [];
        }
        catch (JsonException) { return []; }
    }
}

public sealed record DuplicateMemberPage(List<DuplicateMemberRow> Items, long Cursor, bool HasMore);

public sealed class DuplicateMemberRow
{
    public string GroupId { get; set; } = string.Empty;
    public string DuplicateKey { get; set; } = string.Empty;
    public int TrackCount { get; set; }
    public double Confidence { get; set; }
    public double QualityScore { get; set; }
    public int Rank { get; set; }
    public bool Hidden { get; set; }
    public List<string> Reasons { get; set; } = [];
    public long? SizeBytes { get; set; }
    public LibraryTrackDto Track { get; set; } = new();
}
