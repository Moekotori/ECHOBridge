using Echo.Core.Database;

namespace Echo.Core.Albums;

/// <summary>Cover-backed merging uses persisted clusters instead of a library-sized graph.</summary>
internal static class AlbumLooseGrouping
{
    public static void Apply(TransactionSql sql, CancellationToken cancellation)
    {
        AlbumCoverGrouping.Build(sql, cancellation);
        sql.Run("""
            CREATE TEMP TABLE ag_cover_candidates AS SELECT group_key,title,
              (SELECT match_key FROM ag_cover_evidence evidence WHERE evidence.group_key=groups.group_key AND kind='exact'
                ORDER BY count DESC,position LIMIT 1) AS exact_key,
              (SELECT match_key FROM ag_cover_evidence evidence WHERE evidence.group_key=groups.group_key AND kind='visual'
                ORDER BY count DESC,position LIMIT 1) AS visual_key,
              EXISTS(SELECT 1 FROM ag_credits WHERE ag_credits.group_key=groups.group_key AND reliable_count>0) AS reliable,
              (SELECT COUNT(*) FROM ag_credits WHERE ag_credits.group_key=groups.group_key AND reliable_count>0
                AND credit_key NOT IN('','unknownartist','variousartists'))=1
                AND EXISTS(SELECT 1 FROM ag_credits WHERE ag_credits.group_key=groups.group_key AND reliable_count>0
                  AND credit_key NOT IN('','unknownartist','variousartists') AND ag_single_credit(name)) AS single_owner,
              position FROM ag_groups groups;
            CREATE INDEX ag_cover_candidate_order ON ag_cover_candidates(reliable DESC,single_owner DESC,position);
            CREATE TEMP TABLE ag_cover_clusters(id INTEGER PRIMARY KEY,album_key TEXT,title TEXT);
            CREATE TEMP TABLE ag_cover_members(group_key TEXT PRIMARY KEY,cluster INTEGER) WITHOUT ROWID;
            CREATE INDEX ag_cover_members_cluster ON ag_cover_members(cluster);
            CREATE TEMP TABLE ag_cover_cluster_keys(kind TEXT,match_key TEXT,cluster INTEGER,
              PRIMARY KEY(kind,match_key,cluster)) WITHOUT ROWID;
            """);
        using var read = sql.Command("SELECT group_key,title,exact_key,visual_key FROM ag_cover_candidates ORDER BY reliable DESC,single_owner DESC,position");
        using var reader = read.ExecuteReader();
        var nextCluster = 0L;
        while (reader.Read())
        {
            cancellation.ThrowIfCancellationRequested();
            var key = reader.GetString(0);
            var title = reader.GetString(1);
            var exact = reader.IsDBNull(2) ? null : reader.GetString(2);
            var visual = reader.IsDBNull(3) ? null : reader.GetString(3);
            var normalized = AlbumIdentity.LooseTitle(title);
            var known = normalized.Length > 0 && normalized != "unknown album";
            var visualKey = known && visual is not null ? normalized + "\0" + visual : null;
            long? match = null;
            if (known && exact is not null)
            {
                using var candidates = sql.Command("""
                    SELECT clusters.id,title FROM ag_cover_clusters clusters JOIN ag_cover_cluster_keys keys ON keys.cluster=clusters.id
                    WHERE kind='exact' AND match_key=$0 ORDER BY clusters.id
                    """, exact);
                using var candidate = candidates.ExecuteReader();
                var matches = 0;
                while (candidate.Read())
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (AlbumIdentity.LooseTitle(candidate.GetString(1)) != normalized) continue;
                    var cluster = candidate.GetInt64(0);
                    if (!ClusterCompatible(sql, key, cluster, cancellation)) continue;
                    match = cluster;
                    if (++matches > 1) break;
                }
                if (matches != 1) match = null;
            }
            if (match is null && visualKey is not null)
            {
                using var candidates = sql.Command("SELECT cluster FROM ag_cover_cluster_keys WHERE kind='visual' AND match_key=$0 ORDER BY cluster", visualKey);
                using var candidate = candidates.ExecuteReader();
                var matches = 0;
                while (candidate.Read())
                {
                    cancellation.ThrowIfCancellationRequested();
                    var cluster = candidate.GetInt64(0);
                    var compatible = true;
                    using var members = sql.Command("SELECT group_key FROM ag_cover_members WHERE cluster=$0", cluster);
                    using var member = members.ExecuteReader();
                    while (member.Read())
                        if (!Compatible(sql, key, member.GetString(0), cancellation)) { compatible = false; break; }
                    if (compatible) { match = cluster; if (++matches > 1) break; }
                }
                if (matches != 1) match = null;
            }
            if (match is null)
            {
                match = nextCluster++;
                var albumKey = known && exact is not null ? AlbumIdentity.Hash("loose-cover\0" + normalized + "\0" + exact + "\0" + OwnerScope(sql, key)) : key;
                sql.Run("INSERT INTO ag_cover_clusters VALUES($0,$1,$2)", match, albumKey, title);
            }
            sql.Run("INSERT INTO ag_cover_members VALUES($0,$1)", key, match);
            if (known && exact is not null)
                sql.Run("INSERT OR IGNORE INTO ag_cover_cluster_keys VALUES('exact',$0,$1)", exact, match);
            if (visualKey is not null)
                sql.Run("INSERT OR IGNORE INTO ag_cover_cluster_keys VALUES('visual',$0,$1)", visualKey, match);
        }
        sql.Run("""
            UPDATE ag_tracks SET group_key=(SELECT album_key FROM ag_cover_members members
              JOIN ag_cover_clusters clusters ON clusters.id=members.cluster WHERE members.group_key=ag_tracks.standard_key);
            DROP TABLE ag_groups;
            DROP TABLE ag_credits;
            DROP TABLE ag_components;
            """);
    }

    internal static double Similarity(string left, string right)
    {
        var a = AlbumIdentity.LooseTitle(left); var b = AlbumIdentity.LooseTitle(right);
        if (a.Length == 0 || b.Length == 0 || a == "unknown album" || b == "unknown album") return 0;
        if (a == b) return 1;
        if (a.Length < 2 || b.Length < 2) return 0;
        var grams = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < a.Length - 1; i++) { var gram = a.Substring(i, 2); grams[gram] = grams.GetValueOrDefault(gram) + 1; }
        var matches = 0;
        for (var i = 0; i < b.Length - 1; i++)
        {
            var gram = b.Substring(i, 2);
            if (grams.GetValueOrDefault(gram) > 0) { matches++; grams[gram]--; }
        }
        return 2.0 * matches / (a.Length + b.Length - 2);
    }

    private static string OwnerScope(TransactionSql sql, string group)
    {
        return sql.Value("""
            SELECT group_concat(credit_key,char(31)) FROM (
              SELECT credit_key FROM ag_credits WHERE group_key=$0 AND credit_key NOT IN('','unknownartist','variousartists')
                AND (reliable_count>0 OR NOT EXISTS(SELECT 1 FROM ag_credits reliable
                  WHERE reliable.group_key=$0 AND reliable_count>0 AND credit_key NOT IN('','unknownartist','variousartists')))
              ORDER BY credit_key COLLATE ag_utf16)
            """, group) as string ?? group;
    }
    private static bool ClusterCompatible(TransactionSql sql, string group, long cluster, CancellationToken cancellation)
    {
        using var members = sql.Command("SELECT group_key FROM ag_cover_members WHERE cluster=$0", cluster);
        using var reader = members.ExecuteReader();
        while (reader.Read())
            if (!Compatible(sql, group, reader.GetString(0), cancellation)) return false;
        return true;
    }

    private static (long Count, string? Key) Reliable(TransactionSql sql, string group)
    {
        using var command = sql.Command("""
            SELECT credit_key FROM ag_credits WHERE group_key=$0 AND reliable_count>0
              AND credit_key NOT IN('','unknownartist','variousartists') LIMIT 2
            """, group);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return (0, null);
        var key = reader.GetString(0);
        return (reader.Read() ? 2 : 1, key);
    }
    private static bool HasParticipant(TransactionSql sql, string group, string key, CancellationToken cancellation, bool reliableOnly = false)
    {
        using var command = sql.Command("SELECT name FROM ag_credits WHERE group_key=$0" + (reliableOnly ? " AND reliable_count>0" : ""), group);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            cancellation.ThrowIfCancellationRequested();
            if (AlbumCreditParts.Split(reader.GetString(0)).Any(name => AlbumIdentity.ArtistKey(name) == key)) return true;
        }
        return false;
    }
    private static bool Compatible(TransactionSql sql, string left, string right, CancellationToken cancellation)
    {
        var a = Reliable(sql, left); var b = Reliable(sql, right);
        if (a.Count > 0 && b.Count > 0)
        {
            if (a.Count != 1 || b.Count != 1) return false;
            if (a.Key == b.Key) return true;
            return SingleOwner(sql, left, a.Key!) && HasParticipant(sql, right, a.Key!, cancellation, reliableOnly: true)
                || SingleOwner(sql, right, b.Key!) && HasParticipant(sql, left, b.Key!, cancellation, reliableOnly: true);
        }
        if (a.Count > 0) return a.Count == 1 && (SingleOwner(sql, left, a.Key!) ? HasParticipant(sql, right, a.Key!, cancellation)
            : SharedReliableParticipant(sql, left, right, cancellation));
        if (b.Count > 0) return b.Count == 1 && (SingleOwner(sql, right, b.Key!) ? HasParticipant(sql, left, b.Key!, cancellation)
            : SharedReliableParticipant(sql, right, left, cancellation));
        using var command = sql.Command("SELECT name FROM ag_credits WHERE group_key=$0", left);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            foreach (var name in AlbumCreditParts.Split(reader.GetString(0)))
            {
                var key = AlbumIdentity.ArtistKey(name);
                if (key is not ("" or "unknownartist" or "variousartists") && HasParticipant(sql, right, key, cancellation)) return true;
            }
        return false;
    }
    private static bool SingleOwner(TransactionSql sql, string group, string key) => sql.Value(
        "SELECT ag_single_credit(name) FROM ag_credits WHERE group_key=$0 AND credit_key=$1 AND reliable_count>0", group, key) is long value && value == 1;
    private static bool SharedReliableParticipant(TransactionSql sql, string reliable, string fallback, CancellationToken cancellation)
    {
        using var read = sql.Command("SELECT name FROM ag_credits WHERE group_key=$0 AND reliable_count>0", reliable);
        using var reader = read.ExecuteReader();
        while (reader.Read()) foreach (var name in AlbumCreditParts.Split(reader.GetString(0)))
        {
            var key = AlbumIdentity.ArtistKey(name);
            if (key is not ("" or "unknownartist" or "variousartists") && HasParticipant(sql, fallback, key, cancellation)) return true;
        }
        return false;
    }
}
