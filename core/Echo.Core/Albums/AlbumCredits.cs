using System.Text.RegularExpressions;
using Echo.Core.Database;

namespace Echo.Core.Albums;

/// <summary>Album display ownership follows reliable credits and dominant participants.</summary>
internal static class AlbumCredits
{
    private static readonly Regex SortPrefix = new("^[0-9]{1,3}" + AlbumIdentity.SpacePattern + "*[.)．。]" + AlbumIdentity.SpacePattern
        + "*|^[0-9]{1,3}" + AlbumIdentity.SpacePattern + "*[-_]" + AlbumIdentity.SpacePattern
        + "*(?=[^0-9" + AlbumIdentity.SpaceCharacters + @"(\[（])", RegexOptions.Compiled);
    private static readonly Regex TrackNumber = new(@"^[0-9]{1,2}$|^0+[0-9]*$", RegexOptions.Compiled);
    public static bool Generic(string key) => key is "variousartists" or "unknownartist" or "unknownalbum" or "unknown";

    public static IEnumerable<string> Components(string value)
    {
        // Splitting follows the original displayed credit, as in the TS
        // participant policy. NFKC may turn a literal fullwidth slash into a
        // separator; normalization belongs to the identity key after splitting.
        var normalized = AlbumIdentity.Display(value);
        var parts = AlbumCreditParts.Split(normalized);
        // One credit contributes at most once per participant, matching the
        // default policy used by the existing album display aggregator.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var part in parts)
        {
            var original = AlbumIdentity.Trim(part.Replace('\u0001', '/'));
            var stripped = AlbumIdentity.Trim(SortPrefix.Replace(original, ""));
            var name = AlbumIdentity.Display(stripped.Length > 0 ? stripped : original);
            if (name.Length > 0 && !TrackNumber.IsMatch(AlbumUnicodeNormalization.Normalize(name, System.Text.NormalizationForm.FormKC))
                && seen.Add(AlbumIdentity.ArtistKey(name))) yield return name;
        }
    }

    public static void Build(TransactionSql sql, CancellationToken cancellation)
    {
        sql.Run("""
            CREATE TEMP TABLE ag_credits(group_key TEXT,credit_key TEXT,name TEXT,count INTEGER,
              reliable_count INTEGER,position INTEGER,PRIMARY KEY(group_key,credit_key)) WITHOUT ROWID;
            CREATE TEMP TABLE ag_components(group_key TEXT,credit_key TEXT,name TEXT,count INTEGER,
              position INTEGER,group_position INTEGER,PRIMARY KEY(group_key,credit_key)) WITHOUT ROWID;
            INSERT INTO ag_credits
            SELECT group_key,credit_key,
              (SELECT credit FROM ag_tracks chosen WHERE chosen.hidden=0 AND chosen.group_key=t.group_key
                AND chosen.credit_key=t.credit_key ORDER BY chosen.reliable DESC,chosen.group_position,chosen.position LIMIT 1),
              COUNT(*),SUM(reliable),MIN(position)
            FROM ag_tracks t WHERE hidden=0 GROUP BY group_key,credit_key;
            """);
        using var read = sql.Command("""
            SELECT group_key,credit_key,credit,artist,COUNT(*),MIN(position),MIN(group_position)
            FROM ag_tracks WHERE hidden=0 GROUP BY group_key,credit_key,credit,artist ORDER BY MIN(group_position),MIN(position)
            """);
        using var reader = read.ExecuteReader();
        while (reader.Read())
        {
            cancellation.ThrowIfCancellationRequested();
            var source = Generic(reader.GetString(1)) ? reader.GetString(3) : reader.GetString(2);
            foreach (var name in Components(source))
                sql.Run("""
                    INSERT INTO ag_components VALUES($0,$1,$2,$3,$4,$5)
                    ON CONFLICT(group_key,credit_key) DO UPDATE SET count=count+excluded.count
                    """, reader.GetString(0), AlbumIdentity.ArtistKey(name), name, reader.GetInt64(4), reader.GetInt64(5), reader.GetInt64(6));
        }
    }

    public static string Display(TransactionSql sql, string groupKey, string preferred)
    {
        var total = Convert.ToInt64(sql.Value("SELECT COUNT(*) FROM ag_tracks WHERE group_key=$0 AND hidden=0", groupKey));
        var reliable = Convert.ToInt64(sql.Value("SELECT COUNT(*) FROM ag_credits WHERE group_key=$0 AND reliable_count>0", groupKey));
        var exactCount = Convert.ToInt64(sql.Value("SELECT COUNT(*) FROM ag_credits WHERE group_key=$0", groupKey));
        string? dominant = null;
        using (var read = sql.Command("SELECT name,count FROM ag_components WHERE group_key=$0 ORDER BY count DESC,group_position,position LIMIT 2", groupKey))
        using (var reader = read.ExecuteReader())
        {
            if (total > 1 && reader.Read())
            {
                var name = reader.GetString(0);
                var top = reader.GetInt64(1);
                var next = reader.Read() ? reader.GetInt64(1) : 0;
                if (top == total || top >= (total <= 3 ? total : Math.Ceiling(total * 0.65)) && top > next) dominant = name;
            }
        }
        if (reliable == 1)
        {
            var name = (string)sql.Value("SELECT name FROM ag_credits WHERE group_key=$0 AND reliable_count>0", groupKey)!;
            return Generic(AlbumIdentity.ArtistKey(name)) ? dominant ?? name : name;
        }
        return reliable > 1 || exactCount > 1 ? dominant ?? "Various Artists" : preferred;
    }
}
