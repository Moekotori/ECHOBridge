using Echo.Core.Database;
using Echo.Core.Query.Sql;

namespace Echo.Core.Query;

/// <summary>Bounded watcher lookup. Never hydrates metadata or scans the library.</summary>
public static class ExistingTrackPathsQuery
{
    public const string Capability = "watcher.existingPaths/v1";

    public static Dictionary<string, bool> Execute(ReadOnlyLibraryDatabase database, List<string>? paths)
    {
        if (paths is null || paths.Count > 64 || paths.Any(path => string.IsNullOrEmpty(path) || path.Length > 32768)
            || paths.Sum(path => (long)path.Length) > 262144)
            throw new ArgumentException("Invalid watcher path batch.");
        var result = new Dictionary<string, bool>(StringComparer.Ordinal);
        if (paths.Count == 0) return result;
        var unique = paths.Distinct(StringComparer.Ordinal).ToArray();
        var placeholders = string.Join(',', unique.Select(_ => "?"));
        using var reader = CachedQuery.Reader(database,
            $"SELECT path FROM tracks WHERE missing = 0 AND path IN ({placeholders})",
            command => { foreach (var path in unique) command.Add(path); });
        while (reader.Read()) result[reader.GetString(0)] = true;
        return result;
    }
}
