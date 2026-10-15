using Echo.Core.Database;

namespace Echo.Core.Albums;

/// <summary>Album-dependent artist rebuild in the same transaction, with disk-backed membership.</summary>
internal static class GroupingArtistWriter
{
    public static void Rebuild(TransactionSql sql, string strategy, string now, CancellationToken cancellation)
    {
        var policy = new GroupingArtistPolicy(sql,strategy);
        sql.Run("""
            CREATE TEMP TABLE ag_artist_old(artist_key TEXT PRIMARY KEY,id TEXT,merge_key TEXT,weight INTEGER,claimed INTEGER DEFAULT 0);
            CREATE INDEX ag_artist_old_id ON ag_artist_old(id);
            CREATE TEMP TABLE ag_artist_stats(ordinal INTEGER PRIMARY KEY,id TEXT UNIQUE,artist_key TEXT UNIQUE,name TEXT,cover_id TEXT,cover_score INTEGER);
            CREATE INDEX ag_artist_prefix ON ag_artist_stats(substr(artist_key,1,3));
            CREATE INDEX ag_artist_suffix ON ag_artist_stats(substr(artist_key,-3));
            CREATE TEMP TABLE ag_artist_tracks(artist_id TEXT,track_id TEXT,source_name TEXT,position INTEGER,PRIMARY KEY(artist_id,track_id)) WITHOUT ROWID;
            CREATE TEMP TABLE ag_artist_albums(artist_id TEXT,album_id TEXT,source_name TEXT,PRIMARY KEY(artist_id,album_id)) WITHOUT ROWID;
            """);
        using(var read=sql.Command("SELECT artist_key,id,name,track_count FROM artists"))
        using(var reader=read.ExecuteReader())
            while(reader.Read()) sql.Run("INSERT INTO ag_artist_old(artist_key,id,merge_key,weight) VALUES($0,$1,$2,$3)",reader.GetString(0),reader.GetString(1),policy.Key(reader.GetString(2)),reader.GetInt64(3));
        sql.Run("CREATE INDEX ag_artist_old_merge ON ag_artist_old(merge_key)");
        long ordinal=0;
        string Ensure(string name,string? trackId,string? albumId)
        {
            var key=policy.Key(name);
            if(sql.Value("SELECT id FROM ag_artist_stats WHERE artist_key=$0",key) is not string id)
            {
                if(strategy=="standard" && key.Length>=7) key=GroupingArtistSimilarity.Resolve(sql,key,trackId,albumId,cancellation);
                id=sql.Value("SELECT id FROM ag_artist_stats WHERE artist_key=$0",key) as string ?? "";
                if(id.Length==0)
                {
                    id=sql.Value("SELECT id FROM ag_artist_old WHERE claimed=0 AND (artist_key=$0 OR merge_key=$0) ORDER BY (artist_key=$0) DESC,weight DESC,id LIMIT 1",key) as string ?? Guid.NewGuid().ToString();
                    sql.Run("UPDATE ag_artist_old SET claimed=1 WHERE id=$0",id);
                    sql.Run("INSERT INTO ag_artist_stats VALUES($0,$1,$2,$3,NULL,9007199254740991)",ordinal++,id,key,name);
                    return id;
                }
            }
            var current=(string)sql.Value("SELECT name FROM ag_artist_stats WHERE id=$0",id)!;
            sql.Run("UPDATE ag_artist_stats SET name=$0 WHERE id=$1",GroupingArtistPolicy.Choose(current,name),id);
            return id;
        }
        void Link(string name,string? trackId,string? albumId,string? coverId,string? coverType,string source,long position)
        {
            cancellation.ThrowIfCancellationRequested();
            var id=Ensure(name,trackId,albumId);
            if(trackId is not null) sql.Run("INSERT OR REPLACE INTO ag_artist_tracks VALUES($0,$1,$2,$3)",id,trackId,source,position);
            if(albumId is null) return;
            sql.Run("INSERT OR REPLACE INTO ag_artist_albums VALUES($0,$1,$2)",id,albumId,source);
            if(coverId is null || coverType=="default") return;
            var key=(string)sql.Value("SELECT artist_key FROM ag_artist_stats WHERE id=$0",id)!;
            uint hash=2166136261;
            foreach(var character in key+":"+albumId) { hash^=character; hash=unchecked(hash*16777619); }
            sql.Run("UPDATE ag_artist_stats SET cover_id=$0,cover_score=$1 WHERE id=$2 AND cover_score>$1",coverId,(long)hash,id);
        }
        using(var read=sql.Command("""
            SELECT tracks.id,tracks.artist,albums.id,albums.cover_id,covers.source_type
            FROM tracks LEFT JOIN album_tracks ON album_tracks.track_id=tracks.id
            LEFT JOIN albums ON albums.id=album_tracks.album_id LEFT JOIN covers ON covers.id=albums.cover_id
            WHERE tracks.missing=0 AND tracks.library_hidden=0 AND TRIM(COALESCE(tracks.artist,''))!=''
            ORDER BY tracks.created_at,tracks.id
            """))
        using(var reader=read.ExecuteReader())
        {
            long position=0;
            while(reader.Read())
            {
                var source=AlbumIdentity.Display(reader.GetString(1));
                foreach(var name in policy.Participants(source)) Link(name,reader.GetString(0),reader.IsDBNull(2)?null:reader.GetString(2),reader.IsDBNull(3)?null:reader.GetString(3),reader.IsDBNull(4)?null:reader.GetString(4),source,position);
                position++;
            }
        }
        using(var read=sql.Command("""
            SELECT albums.id,albums.album_artist,albums.cover_id,covers.source_type,album_tracks.track_id,album_tracks.position
            FROM albums JOIN album_tracks ON album_tracks.album_id=albums.id
            JOIN tracks ON tracks.id=album_tracks.track_id AND tracks.missing=0 AND tracks.library_hidden=0
            LEFT JOIN covers ON covers.id=albums.cover_id WHERE TRIM(COALESCE(albums.album_artist,''))!=''
            ORDER BY albums.title COLLATE NOCASE,album_tracks.position
            """))
        using(var reader=read.ExecuteReader())
            while(reader.Read())
            {
                var source=AlbumIdentity.Display(reader.GetString(1));
                foreach(var name in policy.Participants(source)) Link(name,reader.GetString(4),reader.GetString(0),reader.IsDBNull(2)?null:reader.GetString(2),reader.IsDBNull(3)?null:reader.GetString(3),source,reader.GetInt64(5));
            }
        sql.Run("DELETE FROM artist_tracks; DELETE FROM artist_albums; DELETE FROM artists");
        using(var read=sql.Command("SELECT id,artist_key,name,cover_id FROM ag_artist_stats ORDER BY ordinal"))
        using(var reader=read.ExecuteReader())
            while(reader.Read())
            {
                cancellation.ThrowIfCancellationRequested();
                var id=reader.GetString(0); var key=reader.GetString(1);
                sql.Run("""
                    INSERT INTO artists(id,artist_key,name,sort_name,role,track_count,album_count,cover_id,created_at,updated_at)
                    VALUES($0,$1,$2,$1,'track',(SELECT COUNT(*) FROM ag_artist_tracks WHERE artist_id=$0),
                      (SELECT COUNT(*) FROM ag_artist_albums WHERE artist_id=$0),$3,$4,$4)
                    """,id,key,policy.Display(key,reader.GetString(2)),reader.IsDBNull(3)?null:reader.GetString(3),now);
            }
        sql.Run("INSERT INTO artist_tracks SELECT * FROM ag_artist_tracks; INSERT INTO artist_albums SELECT * FROM ag_artist_albums");
        using(var read=sql.Command("""
            SELECT tag_id,target_id,title,subtitle FROM personal_tag_items items
            WHERE kind='artist' AND target_id NOT LIKE 'remote-%' AND target_id NOT LIKE 'album-artist:%'
              AND NOT EXISTS(SELECT 1 FROM artists WHERE artists.id=items.target_id)
            """))
        using(var reader=read.ExecuteReader())
            while(reader.Read())
            {
                cancellation.ThrowIfCancellationRequested();
                var key=policy.Key(reader.GetString(2));
                sql.Run("""
                    INSERT OR IGNORE INTO personal_tag_items(tag_id,kind,target_id,title,subtitle)
                    SELECT $0,'artist',id,name,$2 FROM artists WHERE artist_key=$1;
                    DELETE FROM personal_tag_items WHERE tag_id=$0 AND kind='artist' AND target_id=$3
                      AND EXISTS(SELECT 1 FROM artists WHERE artist_key=$1)
                    """,reader.GetString(0),key,reader.IsDBNull(3)?null:reader.GetString(3),reader.GetString(1));
            }
        sql.Run("""
            DROP TABLE ag_artist_old; DROP TABLE ag_artist_stats;
            DROP TABLE ag_artist_tracks; DROP TABLE ag_artist_albums;
            """);
    }
}
