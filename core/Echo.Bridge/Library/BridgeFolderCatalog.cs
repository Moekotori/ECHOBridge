using Echo.Core.Protocol;
using Echo.Core.Query;
using Echo.Core.Query.Sql;

namespace Echo.Bridge.Library;

/// <summary>Aggregate and sort the complete directory in SQLite; materialize only one page.</summary>
public sealed class BridgeFolderCatalog(BridgeLibraryReader reader, BridgeFolderReferences references)
{
    public (List<Dictionary<string, object?>> Entries, long Total) Page(
        string? parentId, string search, string? sort, int page, CancellationToken cancellation)
        => BridgeLibraryMetadata.Read(reader.Profile, cancellation, database =>
        {
            var order = sort switch
            {
                null or "default" or "title" or "titleAsc" => "name COLLATE NOCASE, node_id",
                "titleDesc" => "name COLLATE NOCASE DESC, node_id",
                "trackCountDesc" => "track_count DESC, name COLLATE NOCASE, node_id",
                "albumCountDesc" => "album_count DESC, name COLLATE NOCASE, node_id",
                "durationDesc" => "duration DESC, name COLLATE NOCASE, node_id",
                "sizeDesc" => "size_bytes DESC, name COLLATE NOCASE, node_id",
                "fileModifiedDesc" => "mtime_ms DESC, name COLLATE NOCASE, node_id",
                _ => throw new BridgeException(400, "invalid_peer_catalog_query"),
            };
            using var command = database.RequireConnection().CreateCommand();
            // The directory may be large: spill transient grouping/sorting to SQLite temp files.
            command.CommandText = "PRAGMA temp_store=FILE";
            command.ExecuteNonQuery();
            using var transaction = database.BeginDeferredRead();
            string scoped;
            string? folderId = null;
            if (parentId is null)
            {
                scoped = """
                    SELECT folders.id AS node_id, folders.name AS name, folders.path AS node_path,
                           tracks.id AS track_id, tracks.duration, tracks.size_bytes, tracks.mtime_ms
                    FROM folders LEFT JOIN tracks ON tracks.folder_id = folders.id
                        AND tracks.missing = 0 AND tracks.library_hidden = 0
                    WHERE folders.enabled = 1 AND folders.status != 'removed'
                    """;
            }
            else
            {
                var folder = references.Resolve(parentId);
                folderId = folder.FolderId;
                // Core owns path scoping and rejects a path outside its configured root.
                var scope = FolderPathScopeBuilder.Resolve(database, folder.FolderId, folder.Path, true);
                using var rootCommand = database.RequireConnection().CreateCommand();
                rootCommand.CommandText = "SELECT path FROM folders WHERE id=$id";
                rootCommand.Parameters.AddWithValue("$id", folder.FolderId);
                var parent = folder.Path ?? (string)rootCommand.ExecuteScalar()!;
                var prefix = parent.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                foreach (var value in scope.Parameters) command.Add(value);
                command.Parameters.AddWithValue("$start", prefix.Length + 1);
                command.Parameters.AddWithValue("$prefix", prefix);
                scoped = $$"""
                    SELECT $prefix || SUBSTR(tail, 1, INSTR(tail, '/') - 1) AS node_id,
                           SUBSTR(tail, 1, INSTR(tail, '/') - 1) AS name,
                           $prefix || SUBSTR(tail, 1, INSTR(tail, '/') - 1) AS node_path,
                           track_id, duration, size_bytes, mtime_ms
                    FROM (
                        SELECT tracks.id AS track_id, tracks.duration, tracks.size_bytes, tracks.mtime_ms,
                               REPLACE(SUBSTR(tracks.path, $start), '\', '/') AS tail
                        FROM tracks WHERE {{scope.Sql}}
                    ) WHERE INSTR(tail, '/') > 0
                    """;
            }
            var cte = $$"""
                WITH scoped AS ({{scoped}}),
                stats AS (
                    SELECT node_id, name, node_path, COUNT(track_id) AS track_count,
                           COALESCE(SUM(duration), 0) AS duration, COALESCE(SUM(size_bytes), 0) AS size_bytes,
                           COALESCE(MAX(mtime_ms), 0) AS mtime_ms
                    FROM scoped GROUP BY node_id
                ), albums AS (
                    SELECT scoped.node_id, COUNT(DISTINCT album_tracks.album_id) AS album_count
                    FROM scoped JOIN album_tracks ON album_tracks.track_id = scoped.track_id
                    GROUP BY scoped.node_id
                ), filtered AS (
                    SELECT stats.*, COALESCE(albums.album_count, 0) AS album_count
                    FROM stats LEFT JOIN albums ON albums.node_id = stats.node_id
                    WHERE INSTR(LOWER(stats.name), LOWER($search)) > 0
                )
                """;
            command.Parameters.AddWithValue("$search", search.Trim());
            command.Parameters.AddWithValue("$offset", (page - 1L) * 50);
            command.CommandText = cte + $"SELECT *, COUNT(*) OVER() AS total FROM filtered ORDER BY {order} LIMIT 50 OFFSET $offset";
            command.UseNamedPlaceholders();
            var entries = new List<Dictionary<string, object?>>(50);
            long total = 0;
            using (var rows = command.ExecuteReader())
            {
                while (rows.Read())
                {
                    cancellation.ThrowIfCancellationRequested();
                    var id = folderId is null ? "root:" + rows.GetString(0) : references.Remember(new LibraryFolderNodeDto
                    {
                        FolderId = folderId, Path = rows.GetString(2), Name = rows.GetString(1),
                    });
                    entries.Add(BridgeTrackProjection.Container("folder", id, rows.GetString(1), "", rows.GetInt32(3), null));
                    total = rows.GetInt64(8);
                }
            }
            if (entries.Count == 0)
            {
                command.CommandText = cte + "SELECT COUNT(*) FROM filtered";
                command.UseNamedPlaceholders();
                total = (long)command.ExecuteScalar()!;
            }
            return (entries, total);
        });
}
