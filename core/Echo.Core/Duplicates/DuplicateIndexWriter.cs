using System.Globalization;
using System.Text;
using Echo.Core.Maintenance;
using Microsoft.Data.Sqlite;

namespace Echo.Core.Duplicates;

/// <summary>Builds the strict duplicate index with disk-backed temporary rows instead of a library-sized heap snapshot.</summary>
public static class DuplicateIndexWriter
{
    public const string Capability = "library.duplicateIndex/v1";
    private const string GroupReason = "[\"strict_title_artist_duration_cluster\"]";
    private const string KeepReason = "[\"representative_highest_quality\"]";
    private const string RemoveReason = "[\"hidden_lower_quality_duplicate\"]";
    private static readonly HashSet<string> Lossless = ["FLAC", "ALAC", "WAV", "AIFF", "AIF", "APE", "WV", "DSF", "DFF", "TTA", "TAK"];
    private static readonly HashSet<string> Lossy = ["OPUS", "AAC", "M4A", "OGG", "MP3", "WMA"];

    public static DuplicateIndexSummary Rebuild(SqliteConnection source, string databasePath, string? mode)
    {
        // The current UI exposes three mode names, but only strict matching has an implementation.
        _ = mode;
        const string normalizedMode = "strict";
        using var writer = SortKeyMaintenanceSql.OpenWriter(databasePath);
        writer.CreateCollation("echo_duplicate_path", (left, right) =>
            StringComparer.CurrentCulture.Compare(left, right));
        SortKeyMaintenanceSql.Execute(writer, null, "PRAGMA temp_store=FILE");
        SortKeyMaintenanceSql.Execute(writer, null, """
            CREATE TEMP TABLE duplicate_candidates (
              id TEXT PRIMARY KEY, bucket TEXT NOT NULL, version TEXT NOT NULL,
              duration REAL NOT NULL, quality INTEGER NOT NULL, size_bytes INTEGER NOT NULL, path TEXT NOT NULL
            );
            CREATE TEMP TABLE duplicate_cluster_members (
              cluster_id INTEGER NOT NULL, id TEXT NOT NULL, duration REAL NOT NULL,
              quality INTEGER NOT NULL, size_bytes INTEGER NOT NULL, path TEXT NOT NULL
            );
            CREATE TEMP TABLE duplicate_clusters (id INTEGER PRIMARY KEY, bucket TEXT NOT NULL, count INTEGER NOT NULL);
            """);

        var scanned = Stage(source, writer);
        SortKeyMaintenanceSql.Execute(writer, null,
            "CREATE INDEX temp.duplicate_candidate_order ON duplicate_candidates(bucket, duration, id)");
        Cluster(writer);
        SortKeyMaintenanceSql.Execute(writer, null,
            "CREATE INDEX temp.duplicate_cluster_order ON duplicate_cluster_members(cluster_id, duration)");
        // The timestamp also identifies a paged read's index revision. Millisecond precision can
        // repeat across two quick rebuilds, so retain the clock's full tick precision.
        var updatedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture);
        return Persist(writer, normalizedMode, updatedAt, scanned);
    }

    private static int Stage(SqliteConnection source, SqliteConnection writer)
    {
        using var select = source.CreateCommand();
        select.CommandText = """
            SELECT id, path, title, artist, album_artist, duration, codec, bit_depth, sample_rate,
                   bitrate, cover_id, metadata_status, size_bytes
            FROM tracks WHERE missing = 0 AND library_hidden = 0
            """;
        using var insert = writer.CreateCommand();
        insert.CommandText = "INSERT INTO duplicate_candidates VALUES ($id,$bucket,$version,$duration,$quality,$size,$path)";
        foreach (var name in new[] { "$id", "$bucket", "$version", "$duration", "$quality", "$size", "$path" })
            insert.Parameters.Add(new SqliteParameter(name, DBNull.Value));
        using var rows = select.ExecuteReader();
        var scanned = 0;
        while (rows.Read())
        {
            scanned++;
            var duration = Number(rows, 5);
            if (!double.IsFinite(duration) || duration <= 0) continue;
            var title = Text(rows, 2);
            var normalizedTitle = DuplicateTextNormalization.Title(title);
            var artist = Text(rows, 3);
            if (string.IsNullOrWhiteSpace(artist)) artist = Text(rows, 4);
            var normalizedArtist = DuplicateTextNormalization.Normalize(artist);
            if (normalizedTitle.Length == 0 || normalizedArtist.Length == 0) continue;
            insert.Parameters["$id"].Value = Text(rows, 0);
            var version = DuplicateTextNormalization.VersionSignature(title, Text(rows, 1));
            insert.Parameters["$bucket"].Value = normalizedTitle + "\0" + normalizedArtist
                + (version.Length > 0 ? "\0" + version : "");
            insert.Parameters["$version"].Value = version;
            insert.Parameters["$duration"].Value = duration;
            insert.Parameters["$quality"].Value = Quality(rows);
            insert.Parameters["$size"].Value = Long(rows, 12);
            insert.Parameters["$path"].Value = Text(rows, 1);
            insert.ExecuteNonQuery();
        }
        return scanned;
    }

    private static void Cluster(SqliteConnection writer)
    {
        using var select = writer.CreateCommand();
        select.CommandText = "SELECT id,bucket,version,duration,quality,size_bytes,path FROM duplicate_candidates ORDER BY bucket,duration,id";
        using var insert = writer.CreateCommand();
        insert.CommandText = "INSERT INTO duplicate_cluster_members VALUES ($cluster,$id,$duration,$quality,$size,$path)";
        foreach (var name in new[] { "$cluster", "$id", "$duration", "$quality", "$size", "$path" })
            insert.Parameters.Add(new SqliteParameter(name, DBNull.Value));
        using var saveCluster = writer.CreateCommand();
        saveCluster.CommandText = "INSERT INTO duplicate_clusters VALUES ($id,$bucket,$count)";
        foreach (var name in new[] { "$id", "$bucket", "$count" }) saveCluster.Parameters.Add(new SqliteParameter(name, DBNull.Value));

        using var rows = select.ExecuteReader();
        string? bucket = null, version = null;
        var firstDuration = 0.0;
        var count = 0;
        var clusterId = 0;
        (string Id, double Duration, long Quality, long Size, string Path)? first = null;
        void InsertMember((string Id, double Duration, long Quality, long Size, string Path) member)
        {
            insert.Parameters["$cluster"].Value = clusterId;
            insert.Parameters["$id"].Value = member.Id;
            insert.Parameters["$duration"].Value = member.Duration;
            insert.Parameters["$quality"].Value = member.Quality;
            insert.Parameters["$size"].Value = member.Size;
            insert.Parameters["$path"].Value = member.Path;
            insert.ExecuteNonQuery();
        }
        void Flush()
        {
            if (count < 2 || bucket is null) return;
            saveCluster.Parameters["$id"].Value = clusterId;
            saveCluster.Parameters["$bucket"].Value = bucket;
            saveCluster.Parameters["$count"].Value = count;
            saveCluster.ExecuteNonQuery();
        }
        while (rows.Read())
        {
            var nextBucket = rows.GetString(1);
            var nextVersion = rows.GetString(2);
            var duration = rows.GetDouble(3);
            if (count == 0 || bucket != nextBucket || version != nextVersion || duration - firstDuration > 2)
            {
                Flush();
                clusterId++;
                bucket = nextBucket;
                version = nextVersion;
                firstDuration = duration;
                count = 0;
                first = null;
            }
            var member = (rows.GetString(0), duration, rows.GetInt64(4), rows.GetInt64(5), rows.GetString(6));
            if (count == 0) first = member;
            else
            {
                if (count == 1) InsertMember(first!.Value);
                InsertMember(member);
            }
            count++;
        }
        Flush();
    }

    private static DuplicateIndexSummary Persist(SqliteConnection writer, string mode, string updatedAt, int scanned)
    {
        // All published rows change in one transaction; a failed build leaves the previous index intact.
        using var transaction = writer.BeginTransaction();
        SortKeyMaintenanceSql.Execute(writer, transaction,
            "DELETE FROM duplicate_track_members WHERE group_id IN (SELECT id FROM duplicate_track_groups WHERE mode=$p0)", mode);
        SortKeyMaintenanceSql.Execute(writer, transaction, "DELETE FROM duplicate_track_groups WHERE mode=$p0", mode);
        var groups = 0;
        var members = 0;
        using var clusters = SortKeyMaintenanceSql.Command(writer, transaction, "SELECT id,bucket,count FROM duplicate_clusters ORDER BY id");
        using var clusterRows = clusters.ExecuteReader();
        while (clusterRows.Read())
        {
            var clusterId = clusterRows.GetInt64(0);
            var bucket = clusterRows.GetString(1);
            var count = clusterRows.GetInt32(2);
            var left = Convert.ToDouble(SortKeyMaintenanceSql.Scalar(writer, transaction,
                "SELECT duration FROM duplicate_cluster_members WHERE cluster_id=$p0 ORDER BY duration LIMIT 1 OFFSET $p1",
                clusterId, (count - 1) / 2), CultureInfo.InvariantCulture);
            var right = count % 2 == 0
                ? Convert.ToDouble(SortKeyMaintenanceSql.Scalar(writer, transaction,
                    "SELECT duration FROM duplicate_cluster_members WHERE cluster_id=$p0 ORDER BY duration LIMIT 1 OFFSET $p1",
                    clusterId, count / 2), CultureInfo.InvariantCulture)
                : left;
            var median = (left + right) / 2;
            var key = mode + "\0" + bucket + "\0" + Math.Floor(median + 0.5).ToString(CultureInfo.InvariantCulture);
            var groupId = Guid.NewGuid().ToString("D");
            using var ranked = SortKeyMaintenanceSql.Command(writer, transaction,
                "SELECT id,quality FROM duplicate_cluster_members WHERE cluster_id=$p0 " +
                "ORDER BY quality DESC, ABS(duration-$p1) ASC, size_bytes DESC, path COLLATE echo_duplicate_path ASC, id ASC",
                clusterId, median);
            using var rankedRows = ranked.ExecuteReader();
            var rank = 0;
            while (rankedRows.Read())
            {
                rank++;
                var trackId = rankedRows.GetString(0);
                if (rank == 1)
                    SortKeyMaintenanceSql.Execute(writer, transaction,
                        "INSERT INTO duplicate_track_groups VALUES ($p0,$p1,$p2,$p3,$p4,$p5,$p6,$p7,$p8,$p9)",
                        groupId, mode, key, trackId, count, count - 1, 1, GroupReason, updatedAt, updatedAt);
                SortKeyMaintenanceSql.Execute(writer, transaction,
                    "INSERT INTO duplicate_track_members VALUES ($p0,$p1,$p2,$p3,$p4,$p5,$p6,$p7)",
                    groupId, trackId, rankedRows.GetInt64(1), rank, rank > 1 ? 1 : 0,
                    rank == 1 ? KeepReason : RemoveReason, updatedAt, updatedAt);
                members++;
            }
            groups++;
        }
        transaction.Commit();
        return new DuplicateIndexSummary(mode, scanned, groups, members, members - groups, updatedAt);
    }

    private static string Text(SqliteDataReader row, int index) => row.IsDBNull(index) ? "" : row.GetString(index);
    private static double Number(SqliteDataReader row, int index) => row.IsDBNull(index) ? 0 : Convert.ToDouble(row.GetValue(index), CultureInfo.InvariantCulture);
    private static long Long(SqliteDataReader row, int index) => row.IsDBNull(index) ? 0 : Convert.ToInt64(row.GetValue(index), CultureInfo.InvariantCulture);
    private static double Positive(SqliteDataReader row, int index, double cap)
    {
        var value = Number(row, index);
        return double.IsFinite(value) && value > 0 ? Math.Min(value, cap) : 0;
    }
    private static long Quality(SqliteDataReader row)
    {
        var codec = Text(row, 6).Normalize(NormalizationForm.FormKC).Trim().ToUpperInvariant();
        var score = Lossless.Contains(codec) ? 10_000 : Lossy.Contains(codec) ? 2_000 : 250;
        var value = score + Positive(row, 7, 32) * 100 + Positive(row, 8, 192_000) / 100
            + Positive(row, 9, 10_000_000) / 10_000;
        if (Text(row, 10).Length > 0) value += 25;
        if (Text(row, 11).Equals("ok", StringComparison.OrdinalIgnoreCase)) value += 25;
        return (long)Math.Floor(value + 0.5);
    }
}

public sealed record DuplicateIndexSummary(
    string Mode, int TotalTracksScanned, int DuplicateGroups, int DuplicateMembers, int HiddenTracks, string UpdatedAt);
