using Echo.Core.Database;

namespace Echo.Core.Albums;

/// <summary>Stable identity reconciliation without duplicating track membership in managed sets.</summary>
internal static class AlbumGroupingIdentityAssignment
{
    public static void Assign(TransactionSql sql, CancellationToken cancellation)
    {
        sql.Run("""
            CREATE TEMP TABLE ag_old AS SELECT id,album_key,user_rating,created_at FROM albums;
            CREATE UNIQUE INDEX ag_old_id ON ag_old(id);
            CREATE UNIQUE INDEX ag_old_key ON ag_old(album_key);
            CREATE TEMP TABLE ag_overlap AS
              SELECT t.group_key,old.album_id AS old_id,COUNT(*) AS count
              FROM ag_tracks t JOIN album_tracks old ON old.track_id=t.id GROUP BY t.group_key,old.album_id;
            CREATE INDEX ag_overlap_key ON ag_overlap(group_key,count DESC,old_id);
            CREATE TEMP TABLE ag_claimed(id TEXT PRIMARY KEY) WITHOUT ROWID;
            UPDATE ag_groups SET id=(SELECT id FROM ag_old WHERE album_key=ag_groups.group_key);
            INSERT INTO ag_claimed SELECT id FROM ag_groups WHERE id IS NOT NULL;
            """);
        foreach (var key in AlbumGroupingStats.Keys(sql, "id IS NULL"))
        {
            cancellation.ThrowIfCancellationRequested();
            var id = sql.Value("""
                SELECT old_id FROM ag_overlap WHERE group_key=$0 AND old_id NOT IN(SELECT id FROM ag_claimed)
                ORDER BY count DESC,old_id LIMIT 1
                """, key) as string ?? Guid.NewGuid().ToString();
            sql.Run("UPDATE ag_groups SET id=$0 WHERE group_key=$1", id, key);
            sql.Run("INSERT INTO ag_claimed VALUES($0)", id);
        }
        sql.Run("""
            CREATE TEMP TABLE ag_targets AS
              SELECT overlap.old_id,groups.id,count FROM ag_overlap overlap JOIN ag_groups groups USING(group_key);
            CREATE INDEX ag_targets_old ON ag_targets(old_id,count DESC,id);
            """);
    }
}
