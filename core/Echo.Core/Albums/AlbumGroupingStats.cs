using Echo.Core.Database;

namespace Echo.Core.Albums;

/// <summary>Stats are derived in SQLite; only one bounded key page is held by the host.</summary>
internal static class AlbumGroupingStats
{
    public static void Build(TransactionSql sql, CancellationToken cancellation, bool seeded = false)
    {
        AlbumCredits.Build(sql, cancellation);
        sql.Run("""
            CREATE TEMP TABLE ag_groups(group_key TEXT PRIMARY KEY,physical_key TEXT,title TEXT,artist TEXT,
              year INTEGER,cover_id TEXT,track_count INTEGER,duration REAL,position INTEGER,id TEXT) WITHOUT ROWID;
            INSERT INTO ag_groups
            SELECT group_key,physical_key,
              COALESCE(NULLIF((SELECT title FROM ag_tracks first WHERE first.group_key=t.group_key ORDER BY group_position,position LIMIT 1),''),'Unknown Album'),
              COALESCE((SELECT credit FROM ag_tracks first WHERE first.group_key=t.group_key AND hidden=0 ORDER BY group_position,position LIMIT 1),
            """ + (seeded ? "''" : "(SELECT credit FROM ag_tracks first WHERE first.group_key=t.group_key ORDER BY group_position,position LIMIT 1)") + """
              ),
              (SELECT year FROM ag_tracks years WHERE years.group_key=t.group_key AND hidden=0
                AND typeof(year)='integer' AND year BETWEEN 1000 AND 9999
                GROUP BY year ORDER BY COUNT(*) DESC,year DESC LIMIT 1),
              (SELECT cover_id FROM ag_tracks covers WHERE covers.group_key=t.group_key AND NULLIF(cover_id,'') IS NOT NULL ORDER BY group_position,position LIMIT 1),
              SUM(CASE WHEN hidden=0 THEN 1 ELSE 0 END),SUM(CASE WHEN hidden=0 THEN COALESCE(duration,0) ELSE 0 END),MIN(position),NULL
            FROM ag_tracks t GROUP BY group_key;
            """);
        foreach (var key in Keys(sql))
        {
            cancellation.ThrowIfCancellationRequested();
            var preferred = (string)sql.Value("SELECT artist FROM ag_groups WHERE group_key=$0", key)!;
            sql.Run("UPDATE ag_groups SET artist=$0 WHERE group_key=$1", AlbumCredits.Display(sql, key, preferred), key);
        }
    }

    public static IEnumerable<string> Keys(TransactionSql sql, string? where = null)
    {
        var cursor = "";
        for (;;)
        {
            var page = new List<string>(128);
            using (var command = sql.Command("SELECT group_key FROM ag_groups WHERE group_key>$0"
                + (where is null ? "" : " AND " + where) + " ORDER BY group_key LIMIT 128", cursor))
            using (var reader = command.ExecuteReader())
                while (reader.Read()) page.Add(reader.GetString(0));
            if (page.Count == 0) yield break;
            cursor = page[^1];
            foreach (var key in page) yield return key;
        }
    }
}
