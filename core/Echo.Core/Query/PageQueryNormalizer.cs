using Echo.Core.Protocol;

namespace Echo.Core.Query;

public readonly record struct NormalizedPage(
    int Page,
    int PageSize,
    int Offset,
    int? TotalHint,
    IReadOnlyList<string> Sorts,
    IReadOnlyList<string> FolderIds,
    IReadOnlyList<string> ExcludeTrackIds,
    bool HideDuplicates,
    bool ShowDuplicatesOnly,
    bool ShowOsuOnly,
    string DuplicateMode);

public static class PageQueryNormalizer
{
    public static NormalizedPage Normalize(CoreLibraryPageQuery? query)
    {
        var page = Math.Max(1, query?.Page ?? 1);
        var pageSize = Math.Clamp(query?.PageSize ?? ProtocolConstants.DefaultPageSize, 1, ProtocolConstants.MaxPageSize);
        int offset;
        try
        {
            offset = query?.Offset is { } rawOffset && rawOffset >= 0
                ? rawOffset
                : checked((page - 1) * pageSize);
        }
        catch (OverflowException)
        {
            throw new UnsupportedQueryException("page/offset overflowed the supported integer range.");
        }

        if (query?.FolderIds is { Count: > ProtocolConstants.MaxFolderIds })
        {
            throw new UnsupportedQueryException("folderIds exceeded the Core parameter limit.");
        }

        int? totalHint = query?.TotalHint is { } hint && hint >= 0 ? hint : null;
        return new NormalizedPage(
            page,
            pageSize,
            offset,
            totalHint,
            QueryCapabilities.ActiveSorts(query),
            UniqueIds(query?.FolderIds, ProtocolConstants.MaxFolderIds),
            UniqueIds(query?.ExcludeTrackIds, 500),
            query?.HideDuplicates == true,
            query?.ShowDuplicatesOnly == true,
            query?.ShowOsuOnly == true,
            query?.DuplicateMode == "strict" ? "strict" : "strict");
    }

    private static IReadOnlyList<string> UniqueIds(IEnumerable<string>? values, int limit)
    {
        if (values is null)
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var value in values)
        {
            var id = value?.Trim();
            if (string.IsNullOrEmpty(id) || !seen.Add(id))
            {
                continue;
            }

            if (result.Count >= limit)
            {
                throw new UnsupportedQueryException("filter id list exceeded the Core parameter limit.");
            }

            result.Add(id);
        }

        return result;
    }
}
