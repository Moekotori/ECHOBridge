using Echo.Core.Protocol;

namespace Echo.Core.Query;

public static class SearchContract
{
    public static bool HasRawSearch(CoreLibraryPageQuery? query) =>
        !string.IsNullOrWhiteSpace(query?.Search);

    public static bool HasExecutableNormalizedSearch(CoreNormalizedTrackSearch? search)
    {
        if (search is null)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(search.FtsMatch) || search.Year is not null)
        {
            return true;
        }

        return search.Excludes is { Count: > 0 };
    }

    public static void EnsureExecutable(CoreLibraryPageQuery? query, CoreNormalizedTrackSearch? search)
    {
        if (HasRawSearch(query) && !HasExecutableNormalizedSearch(search))
        {
            throw new UnexecutableQueryException(
                "getTracks received a non-empty search without executable normalizedSearch (ftsMatch, year, or excludes).");
        }
    }

    public static void EnsureExecutableEntity(CoreLibraryPageQuery? query, CoreNormalizedEntitySearch? search)
    {
        if (!HasRawSearch(query))
        {
            return;
        }

        if (search is null)
        {
            throw new UnexecutableQueryException(
                "getAlbums/getArtists received a non-empty search without normalizedEntitySearch.");
        }

        if (!string.Equals(search.Version, ProtocolConstants.LikeEntitySearchVersion, StringComparison.Ordinal))
        {
            throw new UnsupportedQueryException(
                $"Unsupported entity search version '{search.Version}'.");
        }
    }
}

public sealed class UnexecutableQueryException : Exception
{
    public UnexecutableQueryException(string message)
        : base(message)
    {
    }
}
