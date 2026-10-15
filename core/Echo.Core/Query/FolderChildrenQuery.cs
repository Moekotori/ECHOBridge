using System.Runtime.InteropServices;
using Echo.Core.Database;
using Echo.Core.Protocol;
using Echo.Core.Query.Sql;

namespace Echo.Core.Query;

public static class FolderChildrenQuery
{
    private sealed class ChildState(LibraryFolderNodeDto node)
    {
        public LibraryFolderNodeDto Node { get; } = node;
        public HashSet<string> ChildNames { get; } = new(StringComparer.Ordinal);
        public HashSet<string> CoverIds { get; } = new(StringComparer.Ordinal);
    }

    public static List<LibraryFolderNodeDto> Execute(ReadOnlyLibraryDatabase database, CoreFolderChildrenQuery query)
    {
        var folderId = query.FolderId.Trim();
        if (folderId.Length == 0) throw new UnsupportedQueryException("getFolderChildren requires folderId.");

        var root = Path.GetFullPath(FolderPathScopeBuilder.ReadFolderRoot(database, folderId));
        var requestedPath = string.IsNullOrWhiteSpace(query.ParentPath)
            ? root
            : Path.GetFullPath(query.ParentPath.Trim());
        var parent = FolderPathScopeBuilder.ResolveScopedPath(root, requestedPath);
        var separator = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "\\" : "/";
        var prefix = $"{parent.TrimEnd('\\', '/')}{separator}";
        var likePrefix = $"{SqlFragments.EscapeLike(prefix)}%";
        var states = new Dictionary<string, ChildState>(StringComparer.Ordinal);
        const string sql = """
            SELECT tracks.path, tracks.duration, tracks.size_bytes, tracks.cover_id, covers.source_type
            FROM tracks
            LEFT JOIN covers ON covers.id = tracks.cover_id
            WHERE tracks.folder_id = ? AND tracks.missing = 0 AND tracks.library_hidden = 0 AND tracks.path LIKE ? ESCAPE '\'
            ORDER BY tracks.path COLLATE NOCASE
            """;

        using var transaction = database.BeginDeferredRead();
        using var reader = CachedQuery.Reader(database, sql, command =>
        {
            command.Add(folderId);
            command.Add(likePrefix);
        });
        while (reader.Read())
        {
            var path = reader.GetString(0);
            var parts = path.Length >= prefix.Length
                ? path[prefix.Length..].Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries)
                : [];
            if (parts.Length <= 1) continue;

            var name = parts[0];
            var childPath = $"{parent.TrimEnd('\\', '/')}{separator}{name}";
            if (!states.TryGetValue(childPath, out var state))
            {
                var rootPrefixLength = root.EndsWith('\\') || root.EndsWith('/') ? root.Length : root.Length + 1;
                var depth = childPath.Length >= rootPrefixLength
                    ? childPath[rootPrefixLength..].Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries).Length
                    : 0;
                state = new ChildState(new LibraryFolderNodeDto
                {
                    FolderId = folderId,
                    Path = childPath,
                    ParentPath = parent,
                    Name = name,
                    Depth = depth,
                });
                states.Add(childPath, state);
            }

            state.Node.TrackCount++;
            state.Node.TotalDuration += reader.IsDBNull(1) ? 0 : reader.GetDouble(1);
            state.Node.TotalSizeBytes += reader.IsDBNull(2) ? 0 : reader.GetInt64(2);
            if (parts.Length == 2) state.Node.DirectTrackCount++;
            else state.ChildNames.Add(parts[1]);

            if (!reader.IsDBNull(3) && state.CoverIds.Count < 4
                && !string.Equals(reader.IsDBNull(4) ? null : reader.GetString(4), "default", StringComparison.Ordinal))
            {
                var coverId = reader.GetString(3).Trim();
                if (coverId.Length > 0 && state.CoverIds.Add(coverId))
                    state.Node.CoverThumbs.Add($"echo-cover://album/{Uri.EscapeDataString(coverId)}");
            }
        }

        var result = new List<LibraryFolderNodeDto>(states.Count);
        foreach (var state in states.Values)
        {
            state.Node.ChildFolderCount = state.ChildNames.Count;
            result.Add(state.Node);
        }
        return result;
    }
}
