using System.Runtime.InteropServices;
using Echo.Core.Database;
using Echo.Core.Query.Sql;

namespace Echo.Core.Query;

public readonly record struct FolderPathScope(string FolderId, string Sql, IReadOnlyList<object?> Parameters);

public static class FolderPathScopeBuilder
{
    public static FolderPathScope Resolve(
        ReadOnlyLibraryDatabase database,
        string? folderId,
        string? requestedPath,
        bool recursive)
    {
        var id = folderId?.Trim() ?? string.Empty;
        if (id.Length == 0)
        {
            throw new UnsupportedQueryException("getFolderTracks requires folderId.");
        }

        var rootPath = ReadFolderRoot(database, id);
        var folderPath = ResolveScopedPath(rootPath, requestedPath);
        var separator = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "\\" : "/";
        var prefix = $"{StripTrailingSeparators(folderPath)}{separator}";
        var likePrefix = $"{SqlFragments.EscapeLike(prefix)}%";

        if (recursive)
        {
            return new FolderPathScope(
                id,
                "tracks.folder_id = ? AND tracks.missing = 0 AND tracks.library_hidden = 0 AND tracks.path LIKE ? ESCAPE '\\'",
                [id, likePrefix]);
        }

        return new FolderPathScope(
            id,
            """
            tracks.folder_id = ?
              AND tracks.missing = 0 AND tracks.library_hidden = 0
              AND tracks.path LIKE ? ESCAPE '\'
              AND INSTR(SUBSTR(tracks.path, ?), ?) = 0
              AND INSTR(SUBSTR(tracks.path, ?), ?) = 0
            """,
            [id, likePrefix, prefix.Length + 1, "\\", prefix.Length + 1, "/"]);
    }

    internal static string ReadFolderRoot(ReadOnlyLibraryDatabase database, string folderId)
    {
        const string sql = "SELECT path FROM folders WHERE id = ? LIMIT 1";
        var path = Convert.ToString(CachedQuery.Scalar(database, sql, command => command.Add(folderId)));
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new UnsupportedQueryException($"Unknown folder {folderId}");
        }

        return path;
    }

    internal static string ResolveScopedPath(string rootPath, string? requestedPath)
    {
        var root = StripTrailingSeparators(rootPath);
        var target = StripTrailingSeparators(string.IsNullOrWhiteSpace(requestedPath) ? root : requestedPath.Trim());
        if (!IsPathInsideOrEqual(root, target))
        {
            throw new InvalidOperationException("Folder path is outside the library root.");
        }

        return target;
    }

    private static bool IsPathInsideOrEqual(string rootPath, string candidatePath)
    {
        var root = CompareValue(rootPath);
        var candidate = CompareValue(candidatePath);
        return candidate == root
            || candidate.StartsWith($"{root}\\", StringComparison.Ordinal)
            || candidate.StartsWith($"{root}/", StringComparison.Ordinal);
    }

    private static string CompareValue(string value) =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? value.ToLowerInvariant() : value;

    private static string StripTrailingSeparators(string value) =>
        value.TrimEnd('\\', '/');
}
