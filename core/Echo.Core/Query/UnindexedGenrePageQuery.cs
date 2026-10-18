using Echo.Core.Database;
using Echo.Core.Mapping;
using Echo.Core.Protocol;
using Echo.Core.Query.Sql;

namespace Echo.Core.Query;

/// <summary>Standalone read-only aggregation when the desktop has not published its genre index.</summary>
internal static class UnindexedGenrePageQuery
{
    private const string Source = """
        WITH source AS (
          SELECT id,echo_genre_key(genre) AS genre_key,genre AS display_name,
                 album || CHAR(31) || album_artist AS album_key,cover_id
          FROM tracks WHERE missing=0 AND library_hidden=0
        ), casing AS (
          SELECT genre_key,display_name,ROW_NUMBER() OVER (PARTITION BY genre_key
            ORDER BY COUNT(*) DESC,display_name COLLATE NOCASE,display_name) AS rn
          FROM source GROUP BY genre_key,display_name
        ), covers AS (
          SELECT genre_key,cover_id,ROW_NUMBER() OVER (PARTITION BY genre_key
            ORDER BY CASE WHEN cover_id IS NULL OR TRIM(cover_id)='' THEN 1 ELSE 0 END,id) AS rn FROM source
        ), genres AS (
          SELECT source.genre_key,
            CASE WHEN source.genre_key='__unclassified__' THEN '' ELSE COALESCE(MAX(casing.display_name),'') END AS display_name,
            COUNT(DISTINCT source.id) AS track_count,COUNT(DISTINCT source.album_key) AS album_count,
            MAX(covers.cover_id) AS cover_id,source.genre_key='__unclassified__' AS unclassified
          FROM source JOIN casing ON casing.genre_key=source.genre_key AND casing.rn=1
          LEFT JOIN covers ON covers.genre_key=source.genre_key AND covers.rn=1 GROUP BY source.genre_key
        ), local_genres AS (
          SELECT *,display_name AS name_key,display_name AS name_text FROM genres
          WHERE (?='' OR display_name LIKE ? ESCAPE '\')
        )
        """;

    internal static LibraryPageDto<LibraryGenreDto> Execute(ReadOnlyLibraryDatabase database, CoreLibraryPageQuery? query)
    {
        using (var temporary = database.RequireConnection().CreateCommand())
        { temporary.CommandText="PRAGMA temp_store=FILE"; temporary.ExecuteNonQuery(); }
        var page=PageQueryNormalizer.Normalize(query);
        var text=query?.Search?.Trim() ?? "";
        void Bind(Microsoft.Data.Sqlite.SqliteCommand command)
        { command.Add(text); command.Add("%"+SqlFragments.EscapeLike(text)+"%"); }
        using var transaction=database.BeginDeferredRead();
        var total=Convert.ToInt64(CachedQuery.Scalar(database,Source+"SELECT COUNT(*) FROM local_genres",Bind) ?? 0);
        var sql=Source+$"SELECT genre_key,display_name,track_count,album_count,cover_id,unclassified FROM local_genres {GenrePageQuery.GenreOrderSql(QueryCapabilities.PrimarySort(query))} LIMIT ? OFFSET ?";
        var items=new List<LibraryGenreDto>(page.PageSize);
        using (var rows=CachedQuery.Reader(database,sql,command=> { Bind(command); command.Add(page.PageSize); command.Add(page.Offset); }))
        {
            var mapper=GenreRowMapper.Bind(rows);
            while(rows.Read()) items.Add(mapper.Map());
        }
        return new() { Items=items,Total=total,Page=page.Page,PageSize=page.PageSize,HasMore=page.Offset+items.Count<total };
    }
}
