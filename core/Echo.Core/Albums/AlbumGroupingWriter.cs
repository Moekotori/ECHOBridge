using Echo.Core.Database;

namespace Echo.Core.Albums;

/// <summary>Caller owns the transaction and receipt. Grouping never opens source media.</summary>
internal static class AlbumGroupingWriter
{
    public static void Rebuild(TransactionSql sql, string now, CancellationToken cancellation, bool loose = false)
    {
        AlbumGroupingStage.Build(sql, null, cancellation);
        AlbumGroupingStats.Build(sql, cancellation);
        if (loose)
        {
            AlbumLooseGrouping.Apply(sql, cancellation);
            AlbumGroupingStats.Build(sql, cancellation);
        }
        AlbumGroupingIdentityAssignment.Assign(sql, cancellation);
        cancellation.ThrowIfCancellationRequested();
        sql.Run("""
            DELETE FROM album_tracks;
            DELETE FROM albums;
            INSERT INTO albums(id,album_key,title,album_artist,year,cover_id,user_rating,track_count,duration,created_at,updated_at)
              SELECT groups.id,group_key,title,artist,year,cover_id,
                (SELECT user_rating FROM ag_old WHERE id=groups.id AND user_rating BETWEEN 1 AND 5),track_count,duration,
                COALESCE((SELECT created_at FROM ag_old WHERE id=groups.id),$0),$0 FROM ag_groups groups;
            INSERT INTO album_tracks(album_id,track_id,disc_no,track_no,position)
              SELECT groups.id,tracks.id,disc_no,track_no,tracks.position FROM ag_tracks tracks JOIN ag_groups groups USING(group_key);
            """, now);
        AlbumIndexTrackOrder.Apply(sql);
        AlbumPlaylistMigration.Apply(sql, now, cancellation);
        AlbumUserStateMigration.Apply(sql, cancellation);
        cancellation.ThrowIfCancellationRequested();
        sql.Run("""
            INSERT INTO library_maintenance_flags(key,value,updated_at) VALUES($0,'complete',$1)
              ON CONFLICT(key) DO UPDATE SET value=excluded.value,updated_at=excluded.updated_at
            """, AlbumIdentity.VersionKey, now);
    }

    public static IReadOnlyList<string> Seed(TransactionSql sql, IReadOnlyList<string> ids, string now, CancellationToken cancellation, bool loose = false)
    {
        if (ids.Count > 128 || ids.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 512))
            throw new ArgumentException("At most 128 bounded track IDs per album grouping batch.");
        if (ids.Count == 0) return [];
        AlbumGroupingStage.Build(sql, ids, cancellation);
        AlbumGroupingStats.Build(sql, cancellation);
        if (loose)
        {
            AlbumLooseGrouping.Apply(sql, cancellation);
            AlbumGroupingStats.Build(sql, cancellation);
        }
        sql.Run("""
            CREATE TEMP TABLE ag_affected(id TEXT PRIMARY KEY) WITHOUT ROWID;
            INSERT OR IGNORE INTO ag_affected SELECT album_id FROM album_tracks WHERE track_id IN(SELECT id FROM ag_requested);
            DELETE FROM album_tracks WHERE track_id IN(SELECT id FROM ag_requested);
            """);
        foreach (var key in AlbumGroupingStats.Keys(sql))
        {
            cancellation.ThrowIfCancellationRequested();
            var id = sql.Value("SELECT id FROM albums WHERE album_key=$0", key) as string;
            if (id is null)
            {
                var physical = (string)sql.Value("SELECT physical_key FROM ag_groups WHERE group_key=$0", key)!;
                var credit = (string)sql.Value("SELECT artist FROM ag_groups WHERE group_key=$0", key)!;
                var standard = sql.Value("SELECT standard_key FROM ag_tracks WHERE group_key=$0 ORDER BY group_position,position LIMIT 1", key) as string;
                id = physical != key && standard == key
                    ? sql.Value("SELECT id FROM albums WHERE album_key=$0 AND ag_artist(album_artist)=ag_artist($1)", physical, credit) as string : null;
                if (id is not null) sql.Run("UPDATE albums SET album_key=$0 WHERE id=$1", key, id);
            }
            id ??= Guid.NewGuid().ToString();
            sql.Run("UPDATE ag_groups SET id=$0 WHERE group_key=$1; INSERT OR IGNORE INTO ag_affected VALUES($0)", id, key);
        }
        sql.Run("""
            INSERT OR IGNORE INTO albums(id,album_key,title,album_artist,year,cover_id,track_count,duration,created_at,updated_at)
              SELECT id,group_key,title,artist,year,cover_id,track_count,duration,$0,$0 FROM ag_groups;
            INSERT OR REPLACE INTO album_tracks(album_id,track_id,disc_no,track_no,position)
              SELECT groups.id,tracks.id,disc_no,track_no,tracks.position FROM ag_tracks tracks JOIN ag_groups groups USING(group_key);
            """, now);
        return RefreshAffected(sql, now, cancellation);
    }

    public static IReadOnlyList<string> Refresh(TransactionSql sql, IReadOnlyList<string> ids, string now, CancellationToken cancellation)
    {
        if (ids.Count > 128 || ids.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 512))
            throw new ArgumentException("At most 128 bounded track IDs per album stats batch.");
        if (ids.Count == 0) return [];
        var placeholders = string.Join(',', Enumerable.Range(0, ids.Count).Select(index => "$" + index));
        if (Convert.ToInt64(sql.Value($"SELECT COUNT(DISTINCT track_id) FROM album_tracks WHERE track_id IN({placeholders})", ids.Cast<object?>().ToArray())) != ids.Distinct().Count())
            return Seed(sql, ids, now, cancellation);
        AlbumGroupingStage.Build(sql, ids, cancellation);
        sql.Run("""
            CREATE TEMP TABLE ag_affected(id TEXT PRIMARY KEY) WITHOUT ROWID;
            INSERT OR IGNORE INTO ag_affected SELECT album_id FROM album_tracks WHERE track_id IN(SELECT id FROM ag_requested);
            """);
        return RefreshAffected(sql, now, cancellation);
    }

    private static IReadOnlyList<string> RefreshAffected(TransactionSql sql, string now, CancellationToken cancellation)
    {
        AlbumGroupingStage.LoadAffectedStats(sql);
        AlbumGroupingStats.Build(sql, cancellation, seeded: true);
        sql.Run("""
            UPDATE ag_groups SET id=(SELECT id FROM albums WHERE album_key=group_key);
            UPDATE album_tracks SET disc_no=staged.disc_no FROM ag_tracks staged JOIN ag_groups groups ON groups.group_key=staged.group_key
              WHERE album_tracks.album_id=groups.id AND album_tracks.track_id=staged.id
                AND album_tracks.disc_no IS NOT staged.disc_no;
            UPDATE albums SET title=stats.title,album_artist=stats.artist,year=stats.year,cover_id=stats.cover_id,
              track_count=stats.track_count,duration=stats.duration,updated_at=$0
            FROM ag_groups stats WHERE albums.id=stats.id;
            DELETE FROM artist_albums WHERE album_id IN(SELECT id FROM ag_affected) AND album_id NOT IN(SELECT id FROM ag_groups);
            DELETE FROM album_tracks WHERE album_id IN(SELECT id FROM ag_affected) AND album_id NOT IN(SELECT id FROM ag_groups);
            DELETE FROM albums WHERE id IN(SELECT id FROM ag_affected) AND id NOT IN(SELECT id FROM ag_groups);
            """, now);
        AlbumIndexTrackOrder.Apply(sql, affectedOnly: true);
        var affected = new List<string>(256);
        if (Convert.ToInt64(sql.Value("SELECT COUNT(*) FROM ag_affected")) > 256)
            throw new InvalidOperationException("Too many overlapping album references; rebuild the library index.");
        using var read = sql.Command("SELECT id FROM ag_affected ORDER BY id");
        using var reader = read.ExecuteReader();
        while (reader.Read()) affected.Add(reader.GetString(0));
        cancellation.ThrowIfCancellationRequested();
        return affected;
    }
}
