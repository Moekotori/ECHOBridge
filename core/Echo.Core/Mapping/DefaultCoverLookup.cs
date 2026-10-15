using System.Runtime.CompilerServices;
using Echo.Core.Database;
using Echo.Core.Protocol;
using Echo.Core.Query.Sql;

namespace Echo.Core.Mapping;

/// <summary>
/// Mirrors the TypeScript defaultCoverLookup: stored covers with source_type=default
/// are hidden from library UI so themed mascot placeholders can show.
/// </summary>
internal static class DefaultCoverLookup
{
    private static readonly ConditionalWeakTable<ReadOnlyLibraryDatabase, HashSet<string>> IdsByDatabase = new();

    public static void Invalidate(ReadOnlyLibraryDatabase database) => IdsByDatabase.Remove(database);

    public static bool IsDefault(ReadOnlyLibraryDatabase database, string? coverId)
    {
        if (string.IsNullOrEmpty(coverId))
        {
            return false;
        }

        return Ids(database).Contains(coverId);
    }

    public static void SuppressTrackCovers(ReadOnlyLibraryDatabase database, List<LibraryTrackListRowDto> items)
    {
        if (items.Count == 0 || items.TrueForAll(static item => string.IsNullOrEmpty(item.CoverId)))
        {
            return;
        }

        ApplySuppression(items, Ids(database));
    }

    internal static void ApplySuppression(List<LibraryTrackListRowDto> items, HashSet<string> defaultCoverIds)
    {
        if (defaultCoverIds.Count == 0)
        {
            return;
        }

        foreach (var item in items)
        {
            if (item.CoverId is null || !defaultCoverIds.Contains(item.CoverId))
            {
                continue;
            }

            item.CoverId = null;
            item.CoverThumb = null;
        }
    }

    private static HashSet<string> Ids(ReadOnlyLibraryDatabase database) =>
        IdsByDatabase.GetValue(database, static db => LoadIds(db));

    private static HashSet<string> LoadIds(ReadOnlyLibraryDatabase database)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        using var reader = CachedQuery.Reader(database, "SELECT id FROM covers WHERE source_type = 'default'");
        while (reader.Read())
        {
            if (reader.IsDBNull(0))
            {
                continue;
            }

            var id = reader.GetString(0)?.Trim();
            if (!string.IsNullOrEmpty(id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }
}
