using Echo.Core.Database;

namespace Echo.Core.Albums;

internal static class AlbumVersionIndexMigration
{
    public static void Apply(TransactionSql sql, CancellationToken cancellation)
    {
        if (sql.Value("SELECT 1 FROM sqlite_master WHERE type='table' AND name='album_version_members'") is null) return;
        if (sql.Value("SELECT 1 FROM album_version_members LIMIT 1") is null) return;
        sql.Run("""
            CREATE TEMP TABLE version_index_next AS
            SELECT targets.id AS album_id,member.group_id,
              CASE WHEN member.is_primary=1 AND targets.id=(SELECT id FROM ag_targets chosen
                WHERE chosen.old_id=member.album_id ORDER BY count DESC,id LIMIT 1) THEN 2 ELSE 0 END AS primary_rank
            FROM album_version_members member JOIN ag_targets targets ON targets.old_id=member.album_id;
            CREATE INDEX version_index_album ON version_index_next(album_id);
            CREATE INDEX version_index_group ON version_index_next(group_id);
            """);
        for (;;)
        {
            cancellation.ThrowIfCancellationRequested();
            string? keeper = null, donor = null;
            using (var read = sql.Command("SELECT MIN(group_id),MAX(group_id) FROM version_index_next GROUP BY album_id HAVING COUNT(DISTINCT group_id)>1 LIMIT 1"))
            using (var reader = read.ExecuteReader())
                if (reader.Read()) { keeper = reader.GetString(0); donor = reader.GetString(1); }
            if (keeper is null) break;
            sql.Run("UPDATE version_index_next SET group_id=$0 WHERE group_id=$1", keeper, donor);
        }
        if (sql.Value("SELECT 1 FROM version_index_next GROUP BY group_id HAVING COUNT(DISTINCT album_id)>64 LIMIT 1") is not null)
            throw new InvalidOperationException("Album version group limit exceeded during index reconciliation.");
        sql.Run("""
            CREATE TEMP TABLE version_index_final AS
              SELECT album_id,MIN(group_id) AS group_id,MAX(primary_rank) AS primary_rank FROM version_index_next GROUP BY album_id;
            CREATE INDEX version_index_final_group ON version_index_final(group_id,primary_rank DESC,album_id);
            DELETE FROM album_version_members;
            INSERT INTO album_version_members(album_id,group_id,is_primary)
            SELECT member.album_id,member.group_id,member.album_id=(SELECT album_id FROM version_index_final chosen
              WHERE chosen.group_id=member.group_id ORDER BY primary_rank DESC,album_id LIMIT 1)
            FROM version_index_final member WHERE (SELECT COUNT(*) FROM version_index_final sibling WHERE sibling.group_id=member.group_id)>1;
            DROP TABLE version_index_final;
            DROP TABLE version_index_next;
            """);
    }
}
