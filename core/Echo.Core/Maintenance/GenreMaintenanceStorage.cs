using Microsoft.Data.Sqlite;
using static Echo.Core.Maintenance.SortKeyMaintenanceSql;

namespace Echo.Core.Maintenance;

internal sealed record GenreSourceRow(long RowId, string Id, string? Genre, string? Album, string? Cover);

internal static class GenreMaintenanceStorage
{
    // Match JS String.trim used by the existing persisted cover selection.
    private static readonly char[] JsWhitespace = "\u0009\u000a\u000b\u000c\u000d\u0020\u00a0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200a\u2028\u2029\u202f\u205f\u3000\ufeff".ToCharArray();
    public const string FlagKey = "local_genre_pages.synced_revision";
    public static string Revision(SqliteConnection db, SqliteTransaction? tx) =>
        $"libraryGenrePage/v1|rev:{Scalar(db, tx, "SELECT revision FROM local_genre_page_revision WHERE id=1") ?? 0}";

    public static void Initialize(SqliteConnection db) => Execute(db, null, """
        PRAGMA temp_store=FILE;
        PRAGMA temp.cache_size=-1024;
        CREATE TEMP TABLE genre_source (
          id TEXT PRIMARY KEY, genre_key TEXT NOT NULL, display_name TEXT, name_key TEXT NOT NULL,
          album_key TEXT, cover_id TEXT
        );
        """);

    public static (List<GenreSourceRow> Rows, bool End) Read(SqliteConnection db, SqliteTransaction tx, long? after)
    {
        using var command = Command(db, tx, """
            SELECT rowid, substr(id,1,65537), substr(genre,1,65537),
              substr(album || CHAR(31) || album_artist,1,65537), substr(cover_id,1,65537)
            FROM tracks WHERE missing=0 AND library_hidden=0
            """ + (after is null ? "" : " AND rowid>$p0") + " ORDER BY rowid LIMIT 512", after is null ? [] : [after]);
        using var reader = command.ExecuteReader();
        var rows = new List<GenreSourceRow>();
        var chars = 0;
        while (reader.Read())
        {
            var values = Enumerable.Range(1, 4).Select(i => reader.IsDBNull(i) ? null : reader.GetString(i)).ToArray();
            if (values.Any(value => value?.Length > 65536)) throw new InvalidOperationException("Genre source field exceeds text budget.");
            var size = values.Sum(value => value?.Length ?? 0);
            if (chars + size > 262144) return (rows, false);
            chars += size;
            rows.Add(new(reader.GetInt64(0), values[0]!, values[1], values[2], values[3]));
        }
        return (rows, rows.Count < 512);
    }

    public static void Append(SqliteConnection db, SqliteTransaction tx, List<GenreSourceRow> rows, string[][] keys)
    {
        using var command = Command(db, tx, "INSERT INTO temp.genre_source VALUES($p0,$p1,$p2,$p3,$p4,$p5)", new object?[6]);
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            object?[] values = [row.Id, keys[i][1], row.Genre, keys[i][0], row.Album, row.Cover];
            for (var j = 0; j < values.Length; j++) command.Parameters[j].Value = values[j] ?? DBNull.Value;
            command.ExecuteNonQuery();
        }
    }

    public static void Publish(SqliteConnection db, SqliteTransaction tx)
    {
        db.CreateFunction<string?, string?>("echo_genre_cover", value => value is not null && value.Trim(JsWhitespace).Length > 0 ? value : null);
        Execute(db, tx, "DELETE FROM local_genre_pages");
        Execute(db, tx, """
            INSERT INTO local_genre_pages
              (genre_key,display_name,name_key,name_text,track_count,album_count,cover_id,unclassified)
            WITH casing AS (
              SELECT genre_key,display_name,name_key,
                ROW_NUMBER() OVER (PARTITION BY genre_key ORDER BY COUNT(*) DESC,
                  name_key COLLATE NOCASE ASC, display_name COLLATE NOCASE ASC) AS rn
              FROM temp.genre_source GROUP BY genre_key,display_name
            ), covers AS (
              SELECT genre_key,cover_id,ROW_NUMBER() OVER (PARTITION BY genre_key ORDER BY
                CASE WHEN cover_id IS NULL OR TRIM(cover_id)='' THEN 1 ELSE 0 END,id) AS rn
              FROM temp.genre_source
            )
            SELECT source.genre_key,
              CASE WHEN source.genre_key='__unclassified__' THEN '' ELSE COALESCE(MAX(casing.display_name),'') END,
              CASE WHEN source.genre_key='__unclassified__' THEN '' ELSE MAX(casing.name_key) END,
              CASE WHEN source.genre_key='__unclassified__' THEN '' ELSE COALESCE(MAX(casing.display_name),'') END,
              COUNT(DISTINCT source.id),COUNT(DISTINCT source.album_key),echo_genre_cover(MAX(covers.cover_id)),
              source.genre_key='__unclassified__'
            FROM temp.genre_source source
            JOIN casing ON casing.genre_key=source.genre_key AND casing.rn=1
            LEFT JOIN covers ON covers.genre_key=source.genre_key AND covers.rn=1
            GROUP BY source.genre_key
            """);
    }
}
