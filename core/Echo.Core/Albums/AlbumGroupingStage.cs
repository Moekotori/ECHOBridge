using Echo.Core.Database;
using Microsoft.Data.Sqlite;

namespace Echo.Core.Albums;

/// <summary>Disk-backed track and owner evidence. No library-sized managed snapshots.</summary>
internal static class AlbumGroupingStage
{
    // CROSS JOIN fixes the tiny affected set as the outer cursor. Plain JOIN let
    // SQLite scan all growing album memberships once per incremental scan page.
    internal const string AffectedTracksFromSql = """
        FROM ag_affected CROSS JOIN albums ON albums.id=ag_affected.id
        CROSS JOIN album_tracks ON album_tracks.album_id=albums.id CROSS JOIN tracks ON tracks.id=album_tracks.track_id
        LEFT JOIN covers ON covers.id=tracks.cover_id WHERE tracks.missing=0
        """;
    public static void Register(SqliteConnection connection)
    {
        var memo = new AlbumGroupingReadMemo();
        using (var configure = connection.CreateCommand())
        {
            configure.CommandText = "PRAGMA temp_store=FILE; PRAGMA temp.cache_size=-2048";
            configure.ExecuteNonQuery();
        }
        connection.CreateFunction<string, string>("ag_artist", AlbumIdentity.ArtistKey, isDeterministic: true);
        connection.CreateCollation("ag_utf16", StringComparer.Ordinal.Compare);
        connection.CreateFunction<string, string>("ag_loose", AlbumIdentity.LooseTitle, isDeterministic: true);
        connection.CreateFunction<string, string>("ag_folder", path => AlbumIdentity.Folder(path), isDeterministic: true);
        connection.CreateFunction<string, string>("ag_display", AlbumIdentity.Display, isDeterministic: true);
        connection.CreateFunction<string, string, string>("ag_source", AlbumIdentity.Source, isDeterministic: true);
        connection.CreateFunction<string, bool>("ag_single_credit", name => AlbumCreditParts.Split(name).Take(2).Count() == 1, isDeterministic: true);
        connection.CreateFunction<string, bool>("ag_reliable", AlbumIdentity.Reliable, isDeterministic: true);
        connection.CreateFunction<string, string, bool>("ag_include", AlbumIdentity.Include, isDeterministic: true);
        connection.CreateFunction<string, string, string, string, long?, string>("ag_key", memo.Key, isDeterministic: true);
        connection.CreateFunction<string, string, long?, string>("ag_title", (title, path, disc) => memo.Read(title, path, disc).Title, isDeterministic: true);
        connection.CreateFunction<string, string, long?, long?>("ag_disc", (title, path, disc) => memo.Read(title, path, disc).DiscNo, isDeterministic: true);
        connection.CreateFunction("ag_release_reads", () => { memo.Clear(); return 0; });
    }

    public static void Build(TransactionSql sql, IReadOnlyList<string>? trackIds, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        sql.Run("""
            CREATE TEMP TABLE ag_requested(id TEXT PRIMARY KEY) WITHOUT ROWID;
            CREATE TEMP TABLE ag_folders(folder TEXT PRIMARY KEY) WITHOUT ROWID;
            CREATE TEMP TABLE ag_scopes(scope TEXT PRIMARY KEY) WITHOUT ROWID;
            CREATE TEMP TABLE ag_owners(scope TEXT, owner TEXT, PRIMARY KEY(scope,owner)) WITHOUT ROWID;
            CREATE TEMP TABLE ag_tracks(
              id TEXT,path TEXT,physical_key TEXT,group_key TEXT,standard_key TEXT,group_position INTEGER,title TEXT,
              artist TEXT,credit TEXT,credit_key TEXT,reliable INTEGER,hidden INTEGER,
              year INTEGER,duration REAL,cover_id TEXT,cover_type TEXT,cover_hash TEXT,cover_path TEXT,
              disc_no INTEGER,track_no INTEGER,position INTEGER,PRIMARY KEY(id,group_key)) WITHOUT ROWID;
            """);
        if (trackIds is not null)
            foreach (var id in trackIds) sql.Run("INSERT OR IGNORE INTO ag_requested VALUES($0)", id);
        var selected = trackIds is null ? "" : " AND tracks.id IN(SELECT id FROM ag_requested)";
        sql.Run("""
            INSERT INTO ag_tracks
            SELECT tracks.id,path,ag_key(album,path,tracks.id,'',tracks.disc_no),ag_key(album,path,tracks.id,'',tracks.disc_no),'',0,ag_title(album,path,tracks.disc_no),
              artist,COALESCE(NULLIF(ag_display(COALESCE(NULLIF(album_artist,''),artist)),''),'Unknown Artist'),
              ag_artist(COALESCE(NULLIF(ag_display(COALESCE(NULLIF(album_artist,''),artist)),''),'Unknown Artist')),ag_reliable(ag_source(field_sources_json,'albumArtist')),
              library_hidden,year,duration,tracks.cover_id,covers.source_type,covers.source_hash,covers.album_path,
              ag_disc(album,path,tracks.disc_no),track_no,ROW_NUMBER() OVER(ORDER BY album_artist COLLATE NOCASE,album COLLATE NOCASE,disc_no,track_no,tracks.title COLLATE NOCASE)-1
            FROM tracks LEFT JOIN covers ON covers.id=tracks.cover_id
            WHERE tracks.missing=0
            """ + selected);
        sql.Run("INSERT OR IGNORE INTO ag_folders SELECT ag_folder(path) FROM ag_tracks");
        sql.Run("INSERT OR IGNORE INTO ag_scopes SELECT physical_key FROM ag_tracks");
        // Selected tracks are authoritative evidence even when their parent
        // is a filesystem root or uses a different path separator. Rebuilding
        // must not depend on an incremental-neighbor path range finding them.
        sql.Run("""
            INSERT OR IGNORE INTO ag_owners
            SELECT staged.physical_key,ag_artist(tracks.album_artist)
            FROM ag_tracks staged JOIN tracks ON tracks.id=staged.id
            WHERE staged.hidden=0 AND staged.reliable=1
              AND ag_artist(tracks.album_artist) NOT IN('','unknownartist')
            """);
        // Incremental imports include reliable credits of existing neighbors,
        // Keep folders outermost and disqualify the low-selectivity status indexes.
        // Otherwise SQLite can evaluate ag_key for every active track before the
        // path range, once per scan batch. Unary + preserves these INTEGER flags.
        var separator = OperatingSystem.IsWindows() ? "\\" : "/";
        var upper = OperatingSystem.IsWindows() ? "]" : "0";
        if (trackIds is not null) sql.Run("""
            INSERT OR IGNORE INTO ag_owners
            SELECT ag_key(album,path,tracks.id,'',tracks.disc_no),ag_artist(album_artist)
            FROM ag_folders CROSS JOIN tracks ON tracks.path >= ag_folders.folder || $0
              AND tracks.path < ag_folders.folder || $1
            WHERE +tracks.missing=0 AND +tracks.library_hidden=0
              AND ag_reliable(ag_source(field_sources_json,'albumArtist'))
              AND ag_artist(album_artist) NOT IN('','unknownartist')
              AND ag_key(album,path,tracks.id,'',tracks.disc_no) IN(SELECT scope FROM ag_scopes)
            """, separator, upper);
        sql.Run("""
            UPDATE ag_tracks SET group_key=ag_key(tracks.album,ag_tracks.path,ag_tracks.id,
              CASE WHEN (SELECT COUNT(*) FROM ag_owners WHERE scope=ag_tracks.physical_key)>1
                AND ((ag_tracks.reliable=1 AND ag_tracks.credit_key NOT IN('','unknownartist'))
                  OR EXISTS(SELECT 1 FROM ag_owners WHERE scope=ag_tracks.physical_key AND owner=ag_tracks.credit_key))
              THEN ag_tracks.credit_key ELSE '' END,tracks.disc_no)
            FROM tracks WHERE tracks.id=ag_tracks.id;
            DELETE FROM ag_tracks WHERE NOT ag_include(title,(SELECT field_sources_json FROM tracks WHERE tracks.id=ag_tracks.id));
            CREATE INDEX ag_tracks_group ON ag_tracks(group_key,position);
            -- Each distinct credit selects one representative. Index both keys
            -- and its ordering so large compilations do not scan the album per credit.
            CREATE INDEX ag_tracks_credit ON ag_tracks(group_key,credit_key,reliable DESC,group_position,position) WHERE hidden=0;
            CREATE INDEX ag_tracks_scope ON ag_tracks(physical_key);
            UPDATE ag_tracks SET standard_key=group_key,
              group_position=(SELECT MIN(position) FROM ag_tracks first WHERE first.group_key=ag_tracks.group_key);
            """);
        cancellation.ThrowIfCancellationRequested();
    }

    public static void Release(TransactionSql sql)
    {
        sql.Value("SELECT ag_release_reads()");
        sql.Run("""
            DROP TABLE IF EXISTS temp.ag_requested;
            DROP TABLE IF EXISTS temp.ag_folders;
            DROP TABLE IF EXISTS temp.ag_scopes;
            DROP TABLE IF EXISTS temp.ag_owners;
            DROP TABLE IF EXISTS temp.ag_tracks;
            DROP TABLE IF EXISTS temp.ag_credits;
            DROP TABLE IF EXISTS temp.ag_components;
            DROP TABLE IF EXISTS temp.ag_groups;
            DROP TABLE IF EXISTS temp.ag_affected;
            DROP TABLE IF EXISTS temp.ag_old;
            DROP TABLE IF EXISTS temp.ag_overlap;
            DROP TABLE IF EXISTS temp.ag_claimed;
            DROP TABLE IF EXISTS temp.ag_targets;
            DROP TABLE IF EXISTS temp.ag_saved_items;
            DROP TABLE IF EXISTS temp.ag_playlist_claims;
            DROP TABLE IF EXISTS temp.ag_touched_playlists;
            DROP TABLE IF EXISTS temp.ag_cover_evidence;
            DROP TABLE IF EXISTS temp.ag_cover_candidates;
            DROP TABLE IF EXISTS temp.ag_cover_clusters;
            DROP TABLE IF EXISTS temp.ag_cover_members;
            DROP TABLE IF EXISTS temp.ag_cover_cluster_keys;
            DROP TABLE IF EXISTS temp.ag_visual_evidence;
            """);
    }

    public static void LoadAffectedStats(TransactionSql sql)
    {
        sql.Run("""
            DROP TABLE IF EXISTS ag_groups;
            DROP TABLE IF EXISTS ag_credits;
            DROP TABLE IF EXISTS ag_components;
            DELETE FROM ag_tracks;
            INSERT INTO ag_tracks
            SELECT tracks.id,path,ag_key(album,path,tracks.id,'',tracks.disc_no),albums.album_key,albums.album_key,0,ag_title(album,path,tracks.disc_no),
              tracks.artist,COALESCE(NULLIF(ag_display(COALESCE(NULLIF(tracks.album_artist,''),tracks.artist)),''),'Unknown Artist'),
              ag_artist(COALESCE(NULLIF(ag_display(COALESCE(NULLIF(tracks.album_artist,''),tracks.artist)),''),'Unknown Artist')),
              ag_reliable(ag_source(field_sources_json,'albumArtist')),library_hidden,tracks.year,tracks.duration,
              tracks.cover_id,covers.source_type,covers.source_hash,covers.album_path,
              ag_disc(album,path,tracks.disc_no),tracks.track_no,album_tracks.position
            """ + "\n" + AffectedTracksFromSql);
    }
}
