using Echo.Core.Database;
using Microsoft.Data.Sqlite;

namespace Echo.Core.Duplicates;

public static class DuplicateCleanupCandidatesQuery
{
    private const int PageSize = 100;

    public static DuplicateCleanupCandidatesPage Page(
        ReadOnlyLibraryDatabase database, string? revision, long cursor)
    {
        if (string.IsNullOrEmpty(revision)) throw new ArgumentException("Duplicate cleanup revision is required.");
        var summary = DuplicateIndexQueries.Summary(database, "strict");
        if (summary.UpdatedAt != revision) throw new InvalidOperationException("Duplicate cleanup preview expired; scan again.");

        var connection = database.RequireConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT m.rowid, g.id, g.track_count, m.quality_score,
                   t.id, t.title, t.artist, t.path, t.codec, t.sample_rate, t.bit_depth,
                   t.bitrate, t.size_bytes,
                   representative.quality_score,
                   kept.id, kept.title, kept.artist, kept.path, kept.codec, kept.sample_rate,
                   kept.bit_depth, kept.bitrate, kept.size_bytes
            FROM duplicate_track_members m
            JOIN duplicate_track_groups g ON g.id=m.group_id
            JOIN tracks t ON t.id=m.track_id AND t.missing=0 AND t.library_hidden=0
            JOIN duplicate_track_members representative ON representative.group_id=g.id AND representative.rank=1
            JOIN tracks kept ON kept.id=representative.track_id AND kept.missing=0 AND kept.library_hidden=0
            WHERE g.mode='strict' AND g.updated_at=$revision AND m.hidden=1 AND m.rowid>$cursor
            ORDER BY m.rowid LIMIT $limit
            """;
        command.Parameters.AddWithValue("$revision", revision);
        command.Parameters.AddWithValue("$cursor", Math.Max(0, cursor));
        command.Parameters.AddWithValue("$limit", PageSize + 1);
        var items = new List<DuplicateCleanupCandidate>(PageSize);
        var nextCursor = cursor;
        var hasMore = false;
        using (var rows = command.ExecuteReader())
        {
            while (rows.Read())
            {
                if (items.Count == PageSize) { hasMore = true; break; }
                nextCursor = rows.GetInt64(0);
                items.Add(new DuplicateCleanupCandidate(
                    rows.GetString(1), rows.GetInt32(2),
                    Member(rows, 4, rows.GetDouble(3)),
                    Member(rows, 14, rows.GetDouble(13))));
            }
        }
        (long Count, long Bytes)? totals = cursor == 0 ? Totals(connection, revision) : null;
        return new DuplicateCleanupCandidatesPage(items, nextCursor, hasMore,
            totals?.Bytes, totals?.Count);
    }

    private static DuplicateCleanupDisplayMember Member(SqliteDataReader row, int first, double quality) => new(
        row.GetString(first), row.GetString(first + 1), row.GetString(first + 2), row.GetString(first + 3),
        NullableText(row, first + 4), NullableInt(row, first + 5), NullableInt(row, first + 6),
        NullableInt(row, first + 7), NullableLong(row, first + 8), quality);

    private static string? NullableText(SqliteDataReader row, int index) => row.IsDBNull(index) ? null : row.GetString(index);
    private static int? NullableInt(SqliteDataReader row, int index) => row.IsDBNull(index) ? null : row.GetInt32(index);
    private static long? NullableLong(SqliteDataReader row, int index) => row.IsDBNull(index) ? null : row.GetInt64(index);

    private static (long Count, long Bytes) Totals(SqliteConnection connection, string revision)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*), COALESCE(SUM(COALESCE(t.size_bytes,0)),0)
            FROM duplicate_track_members m
            JOIN duplicate_track_groups g ON g.id=m.group_id
            JOIN tracks t ON t.id=m.track_id AND t.missing=0 AND t.library_hidden=0
            JOIN duplicate_track_members representative ON representative.group_id=g.id AND representative.rank=1
            JOIN tracks kept ON kept.id=representative.track_id AND kept.missing=0 AND kept.library_hidden=0
            WHERE g.mode='strict' AND g.updated_at=$revision AND m.hidden=1
            """;
        command.Parameters.AddWithValue("$revision", revision);
        using var rows = command.ExecuteReader();
        rows.Read();
        return (rows.GetInt64(0), rows.GetInt64(1));
    }
}

public sealed record DuplicateCleanupCandidatesPage(
    List<DuplicateCleanupCandidate> Items, long Cursor, bool HasMore, long? TotalBytesToRemove,
    long? AvailableCount);

public sealed record DuplicateCleanupCandidate(
    string GroupId, int TrackCount, DuplicateCleanupDisplayMember Remove, DuplicateCleanupDisplayMember Keep);

public sealed record DuplicateCleanupDisplayMember(
    string TrackId, string Title, string Artist, string Path, string? Codec, int? SampleRate,
    int? BitDepth, int? Bitrate, long? SizeBytes, double QualityScore);
