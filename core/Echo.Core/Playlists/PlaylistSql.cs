using Microsoft.Data.Sqlite;

namespace Echo.Core.Playlists;

internal sealed class PlaylistSql(SqliteConnection connection, SqliteTransaction transaction) : Echo.Core.Database.TransactionSql(connection, transaction)
{
    public void Refresh(string id, string timestamp) => Run("""
        UPDATE playlists SET item_count = (SELECT COUNT(*) FROM playlist_items WHERE playlist_id = $0), updated_at = $1 WHERE id = $0
        """, id, timestamp);
    public void Resequence(string id) => Run("""
        WITH ranked AS MATERIALIZED (SELECT id, ROW_NUMBER() OVER (ORDER BY position ASC, added_at ASC) - 1 AS ordinal
          FROM playlist_items WHERE playlist_id = $0)
        UPDATE playlist_items SET position = (SELECT ordinal FROM ranked WHERE ranked.id = playlist_items.id) WHERE playlist_id = $0
        """, id);
}
