namespace Echo.Core.Query;

internal static class PageTotal
{
    public static long Resolve(NormalizedPage page, int itemCount, Func<long> count)
    {
        if (page.TotalHint is { } hint)
        {
            return hint;
        }

        if (page.Offset == 0 && itemCount < page.PageSize)
        {
            return itemCount;
        }

        return count();
    }
}
