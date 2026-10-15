using System.Security.Cryptography;
using Echo.Core.Database;

namespace Echo.Core.Albums;

/// <summary>Compact cover evidence from cached derivatives; no decoded image retention.</summary>
internal static class AlbumCoverGrouping
{
    private const int MaxCoverBytes = 4 * 1024 * 1024;
    public static void Build(TransactionSql sql, CancellationToken cancellation)
    {
        sql.Run("""
            CREATE TEMP TABLE IF NOT EXISTS ag_visual_evidence(title TEXT,cover_id TEXT,match_key TEXT,PRIMARY KEY(title,cover_id)) WITHOUT ROWID;
            CREATE TEMP TABLE ag_cover_evidence(group_key TEXT,kind TEXT,match_key TEXT,count INTEGER,position INTEGER,
              PRIMARY KEY(group_key,kind,match_key)) WITHOUT ROWID;
            """);
        using var command = sql.Command("""
            SELECT group_key,cover_path,cover_hash,cover_id,COUNT(*),MIN(position),MIN(title)
            FROM ag_tracks WHERE COALESCE(cover_type,'')<>'default'
            GROUP BY group_key,cover_path,cover_hash,cover_id ORDER BY group_key,MIN(position)
            """);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            cancellation.ThrowIfCancellationRequested();
            var group = reader.GetString(0);
            var path = reader.IsDBNull(1) ? null : reader.GetString(1);
            var source = reader.IsDBNull(2) ? null : reader.GetString(2);
            var id = reader.IsDBNull(3) ? null : reader.GetString(3);
            var exact = ExactHash(path, source, cancellation);
            var visual = id is null ? null : sql.Value("SELECT match_key FROM ag_visual_evidence WHERE title=$0 AND cover_id=$1",
                AlbumIdentity.LooseTitle(reader.GetString(6)), id) as string;
            void Add(string kind, string? key)
            {
                if (string.IsNullOrEmpty(key)) return;
                sql.Run("""
                    INSERT INTO ag_cover_evidence VALUES($0,$1,$2,$3,$4)
                    ON CONFLICT(group_key,kind,match_key) DO UPDATE SET count=count+excluded.count,position=MIN(position,excluded.position)
                    """, group, kind, key, reader.GetInt64(4), reader.GetInt64(5));
            }
            Add("exact", exact);
            Add("visual", visual);
        }
    }
    internal static string? ExactHash(string? path, string? fallback, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(path)) return fallback;
        try
        {
            if (new FileInfo(path).Length > MaxCoverBytes) return fallback;
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
            Span<byte> buffer = stackalloc byte[8192];
            var total = 0;
            for (;;)
            {
                cancellation.ThrowIfCancellationRequested();
                var read = file.Read(buffer);
                if (read == 0) return Convert.ToHexStringLower(hash.GetHashAndReset());
                total += read;
                if (total > MaxCoverBytes) return fallback;
                hash.AppendData(buffer[..read]);
            }
        }
        catch (IOException) { return fallback; }
        catch (UnauthorizedAccessException) { return fallback; }
        catch (ArgumentException) { return fallback; }
    }
}
