using Echo.Core.Protocol;

namespace Echo.Core.Query;

public static class QueryCapabilities
{
    public const string GetAlbumForTrackLocal = "getAlbumForTrack.local";
    public const string GetSummary = "getSummary";
    public const string GetTracksLocalPage = "getTracks.localPage";
    public const string GetTracksTitleJump = "getTracks.titleJump/v1";
    public const string GetAlbumsLocalNumeric = "getAlbums.localNumeric";
    public const string GetAlbumsLocalText = "getAlbums.localText";
    public const string GetAlbumsLocalSearch = "getAlbums.localSearch";
    public const string GetArtistsLocalNumeric = "getArtists.localNumeric";
    public const string GetArtistsLocalText = "getArtists.localText";
    public const string GetArtistsAvatarPriority = "getArtists.avatarPriority";
    public const string GetArtistsLocalSearch = "getArtists.localSearch";
    public const string GetTracksAlbumText = "getTracks.albumText";
    public const string GetAlbumsPersonalTags = "getAlbums.personalTags";
    public const string GetAlbumsVersionGroups = "getAlbums.versionGroups";
    public const string GetAlbumsLocalFocus = "getAlbums.localFocus";
    public const string GetAlbumsAdvancedFocus = "getAlbums.advancedFocus/v1";
    public const string GetGenresLocalPage = "getGenres.localPage";
    public const string GetTracksLocalList = "getTracks.localList";
    public const string GetAlbumTracksLocalPage = "getAlbumTracks.localPage";
    public const string GetArtistAlbumsLocalPage = "getArtistAlbums.localPage";
    public const string GetArtistTracksLocalPage = "getArtistTracks.localPage";
    public const string GetGenreTracksLocalPage = "getGenreTracks.localPage";
    public const string GetTracksByIdsHydrate = "getTracksByIds.hydrate";
    public const string GetGenreAlbumsLocalPage = "getGenreAlbums.localPage";
    public const string GetAlbumFocusFacets = "getAlbumFocusFacets";
    public const string GetPlaylists = "getPlaylists";
    public const string GetFolderOverviews = "getFolderOverviews";
    public const string GetFolderChildren = "getFolderChildren.local";
    public const string GetLibraryQualityOverview = "getLibraryQualityOverview.local";
    public const string GetLibraryQualityIssues = "getLibraryQualityIssues.local";
    public const string GetLibraryInboxBatches = "getLibraryInboxBatches.local";
    public const string GetLibraryInboxTracks = "getLibraryInboxTracks.local";
    public const string GetPersonalTags = "getPersonalTags.local";
    public const string GetPersonalTagItems = "getPersonalTagItems.local";
    public const string GetSteamAchievementHistoryStats = "getSteamAchievementHistoryStats";
    public const string GetPlaybackHistorySummary = "getPlaybackHistorySummary.unfiltered";
    public const string GetPlaybackHistoryStatsPage = "getPlaybackHistory.statsPage";
    public const string GetPlaybackHistoryRecords = "getPlaybackHistory.records";
    public const string GetPlaybackHistoryFiltered = "getPlaybackHistory.filtered";
    public const string GetPlaybackHistorySummaryFiltered = "getPlaybackHistorySummary.filtered";
    public const string GetPlaybackStatsDashboard = "getPlaybackStatsDashboard.unfiltered";
    public const string GetPlaybackStatsDashboardFiltered = "getPlaybackStatsDashboard.filtered";
    public const string GetPlaybackStatsDashboardActivity = "getPlaybackStatsDashboardActivity";
    public const string GetPlaybackStatsInsights = "getPlaybackStatsInsights";
    public const string GetPlaybackMemoryGraph = "getPlaybackMemoryGraph";
    public const string GetTracksFolderInsight = "getTracks.folderInsight";
    public const string GetTracksFolderScoped = "getTracks.folderScoped";
    public const string GetFolderTracksPage = "getFolderTracks.page";
    public const string GetPlaylistItemsManualPage = "getPlaylistItems.manualPage";

    public static readonly IReadOnlyList<string> HandshakeCapabilities =
    [
        Echo.Core.Playlists.LastFmImportCommands.Capability,
        "playlists.management/v1",
        Echo.Core.Playlists.PlaylistRelinkCommand.Capability,
        "playlists.organization/v1",
        "playlists.insert-after/v1",
        "personalTags.commands/v1",
        LikedTrackIdsQuery.Capability,
        LikedTrackIdsQuery.AlbumCapability,
        "favorites.pages/v1",
        "playlistSortKeys.maintenance/v1",
        "searchGlobalCatalog.local/v1",
        "getArtistPageFacts.local/v1",
        "getArtistInsights.local/v1",
        "albumArtist.local/v1",
        Echo.Core.Queues.PlaybackQueueStorage.Capability,
        Echo.Core.Queues.PlaybackQueueStorage.AuthorityCapability,
        GetSummary,
        GetAlbumForTrackLocal,
        GetTracksLocalPage,
        GetTracksTitleJump,
        GetTracksAlbumText,
        GetAlbumsLocalNumeric,
        GetAlbumsLocalText,
        GetAlbumsLocalSearch,
        GetArtistsLocalNumeric,
        GetArtistsLocalText,
        GetArtistsLocalSearch,
        GetArtistsAvatarPriority,
        GetAlbumsLocalFocus,
        GetAlbumsAdvancedFocus,
        GetAlbumsVersionGroups,
        GetAlbumsPersonalTags,
        GetGenresLocalPage,
        GetTracksLocalList,
        GetAlbumTracksLocalPage,
        GetArtistTracksLocalPage,
        GetArtistAlbumsLocalPage,
        GetGenreTracksLocalPage,
        GetTracksByIdsHydrate,
        GetGenreAlbumsLocalPage,
        GetAlbumFocusFacets,
        GetPlaylists,
        GetFolderOverviews,
        GetFolderChildren,
        GetLibraryQualityOverview,
        GetLibraryQualityIssues,
        GetLibraryInboxBatches,
        GetLibraryInboxTracks,
        GetPersonalTags,
        GetPersonalTagItems,
        GetSteamAchievementHistoryStats,
        GetPlaybackHistorySummary,
        GetPlaybackHistoryStatsPage,
        GetPlaybackHistoryRecords,
        GetPlaybackHistoryFiltered,
        GetPlaybackHistorySummaryFiltered,
        GetPlaybackStatsDashboard,
        GetPlaybackStatsDashboardFiltered,
        GetPlaybackStatsDashboardActivity,
        GetPlaybackStatsInsights,
        GetPlaybackMemoryGraph,
        GetTracksFolderInsight,
        Sql.VocalPresenceSql.Capability,
        Sql.LyricsFilterSql.Capability,
        GetTracksFolderScoped,
        GetFolderTracksPage,
        GetPlaylistItemsManualPage,
    ];

    public static bool Supports(
        string method,
        CoreLibraryPageQuery? query,
        CoreNormalizedTrackSearch? search,
        CoreNormalizedEntitySearch? entitySearch = null)
    {
        return method switch
        {
            "getSummary" or "handshake" or "ping" or "shutdown" or "cancel" => true,
            "searchGlobalCatalog" or "getArtistPageFacts" or "getArtistInsights" => true,
            "getFolderChildren" => true,
            "getLibraryQualityOverview" or "getLibraryQualityIssues" => true,
            "getLibraryInboxBatches" or "getLibraryInboxTracks" => true,
            "getPersonalTags" or "getPersonalTagItems" => true,
            "getTracks" => SupportsTracks(query, search),
            "getAlbums" => SupportsAlbums(query, entitySearch),
            "getArtists" => SupportsArtists(query, entitySearch),
            "getGenres" => SupportsGenres(query),
            "getAlbumTracks" => SupportsAlbumTracks(null, query),
            "getArtistAlbums" => SupportsArtistAlbums(null, query),
            "getArtistTracks" => SupportsArtistTracks(null, query),
            "getGenreTracks" => SupportsGenreTracks(null, query),
            "getTracksByIds" => true,
            "getGenreAlbums" => SupportsGenreAlbums(null, query),
            "getAlbumFocusFacets" or "getPlaylists" or "getFolderOverviews" or "getSteamAchievementHistoryStats" => true,
            "getPlaybackHistorySummary" => true,
            "getPlaybackHistory" => true,
            "getPlaybackStatsDashboard" => true,
            "getPlaybackStatsDashboardActivity" => true,
            "getPlaybackStatsInsights" => true,
            "getPlaybackMemoryGraph" => true,
            "getFolderTracks" => SupportsFolderTracks(query),
            "getPlaylistItems" => SupportsPlaylistItems(null, query),
            _ => false,
        };
    }

    public static bool SupportsTracks(CoreLibraryPageQuery? query, CoreNormalizedTrackSearch? search)
    {
        if (AlbumFocusSql.IsActive(query?.AlbumFocus)
            || query?.RandomWindow == true
            || HasUnsupportedSource(query?.SourceProvider)
            || HasNonLocalSource(query))
        {
            return false;
        }

        if (!IsLocalOnly(query) && !HasFolderIds(query))
        {
            return false;
        }

        if (query?.FolderIds is { Count: > ProtocolConstants.MaxFolderIds }
            || query?.ExcludeTrackIds is { Count: > ProtocolConstants.MaxExcludeTrackIds })
        {
            return false;
        }

        _ = search;
        return true;
    }

    public static bool SupportsAlbums(CoreLibraryPageQuery? query, CoreNormalizedEntitySearch? search = null)
    {
        if (!IsLocalOnly(query)
            || query?.RandomWindow == true
            || HasUnsupportedSource(query?.SourceProvider)
            || (SearchContract.HasRawSearch(query) && search is null))
        {
            return false;
        }

        return IsNumericOrRecentSort(PrimarySort(query)) || IsAlbumTextSort(PrimarySort(query));
    }

    public static bool SupportsArtists(CoreLibraryPageQuery? query, CoreNormalizedEntitySearch? search = null)
    {
        if (!(query?.ArtistGrouping == "albumArtist" ? IsLocalOrOmitted(query) : IsLocalOnly(query))
            || query?.RandomWindow == true
            || HasUnsupportedSource(query?.SourceProvider)
            || (SearchContract.HasRawSearch(query) && search is null))
        {
            return false;
        }

        return IsArtistNumericSort(PrimarySort(query)) || IsArtistTextSort(PrimarySort(query));
    }

    public static bool SupportsGenres(CoreLibraryPageQuery? query)
    {
        if (!IsLocalOnly(query)
            || query?.RandomWindow == true
            || HasUnsupportedSource(query?.SourceProvider))
        {
            return false;
        }

        return IsGenreSort(PrimarySort(query));
    }

    public static bool SupportsAlbumTracks(string? albumId, CoreLibraryPageQuery? query)
    {
        return !string.IsNullOrWhiteSpace(albumId)
            && !albumId.StartsWith("remote-album:", StringComparison.Ordinal)
            && IsLocalOrOmitted(query)
            && query?.RandomWindow != true
            && !HasUnsupportedSource(query?.SourceProvider);
    }

    public static bool SupportsArtistAlbums(string? artistId, CoreLibraryPageQuery? query) =>
        SupportsArtistTracks(artistId, query)
        && !artistId!.StartsWith("remote-", StringComparison.Ordinal);

    public static bool SupportsArtistTracks(string? artistId, CoreLibraryPageQuery? query)
    {
        return !string.IsNullOrWhiteSpace(artistId)
            && (!artistId.StartsWith("album-artist:", StringComparison.Ordinal) || AlbumArtistQueries.LocalKey(artistId) is not null)
            && IsLocalOrOmitted(query)
            && query?.RandomWindow != true
            && !HasUnsupportedSource(query?.SourceProvider);
    }

    public static bool SupportsGenreTracks(string? genreKey, CoreLibraryPageQuery? query)
    {
        return !string.IsNullOrWhiteSpace(genreKey)
            && IsLocalOrOmitted(query)
            && query?.RandomWindow != true
            && !HasUnsupportedSource(query?.SourceProvider)
            && IsGenreTrackSort(PrimarySort(query));
    }

    public static bool SupportsGenreAlbums(string? genreKey, CoreLibraryPageQuery? query)
    {
        return !string.IsNullOrWhiteSpace(genreKey)
            && IsLocalOrOmitted(query)
            && query?.RandomWindow != true
            && !HasUnsupportedSource(query?.SourceProvider)
            && IsGenreAlbumSort(PrimarySort(query));
    }

    public static bool SupportsUnfilteredPlaybackHistory(CorePlaybackHistoryQuery? query)
    {
        if (query is null)
        {
            return true;
        }

        return string.IsNullOrWhiteSpace(query.Search)
            && string.IsNullOrWhiteSpace(query.From)
            && string.IsNullOrWhiteSpace(query.To)
            && query.CompletedOnly != true
            && query.MediaType is not ("local" or "streaming")
            && query.ActiveRemoteSourceIds is null;
    }

    public static bool SupportsPlaybackHistorySummary(CorePlaybackHistoryQuery? query)
    {
        _ = query;
        return true;
    }

    public static bool SupportsPlaybackHistory(CorePlaybackHistoryQuery? query)
    {
        _ = query;
        return true;
    }

    public static bool SupportsPlaybackStatsDashboard(CorePlaybackHistoryQuery? query)
    {
        _ = query;
        return true;
    }

    public static bool SupportsPlaybackStatsDashboardActivity(CorePlaybackHistoryQuery? query)
    {
        _ = query;
        return true;
    }

    public static bool SupportsPlaybackStatsInsights(CorePlaybackHistoryQuery? query)
    {
        _ = query;
        return true;
    }

    public static bool SupportsPlaybackMemoryGraph(CorePlaybackHistoryQuery? query)
    {
        _ = query;
        return true;
    }

    public static bool IsListProjection(CoreLibraryPageQuery? query) =>
        string.Equals(query?.Projection, "list", StringComparison.Ordinal);

    public static bool IsLocalTitleSort(CoreLibraryPageQuery? query, bool hasFts)
    {
        if (hasFts)
        {
            return false;
        }

        var sorts = ActiveSorts(query);
        return sorts.Count == 1 && IsTitleSort(sorts[0]);
    }

    public static IReadOnlyList<string> ActiveSorts(CoreLibraryPageQuery? query)
    {
        if (query?.Sorts is { Count: > 0 } sorts)
        {
            return sorts.Where(static sort => !string.IsNullOrWhiteSpace(sort)).ToArray();
        }

        return [string.IsNullOrWhiteSpace(query?.Sort) ? "default" : query.Sort];
    }

    public static string PrimarySort(CoreLibraryPageQuery? query)
    {
        var sorts = ActiveSorts(query);
        return sorts.Count > 0 ? sorts[0] : "default";
    }

    public static bool IsTitleSort(string sort) =>
        sort is "default" or "title" or "titleAsc";

    public static bool IsAlbumTextSort(string sort) =>
        sort is "default" or "title" or "titleAsc" or "titleDesc" or "album" or "artist" or "artistDesc" or "artistAlbum";

    public static bool IsAlbumRelatedTrackSort(string sort) =>
        sort is "album" or "albumDesc" or "artistAlbum";

    public static bool UsesAlbumRelatedTrackSort(CoreLibraryPageQuery? query) =>
        ActiveSorts(query).Any(IsAlbumRelatedTrackSort);

    public static bool IsArtistTextSort(string sort) =>
        sort is "default" or "title" or "titleAsc" or "titleDesc";

    public static bool IsGenreSort(string sort) =>
        sort is "default" or "title" or "titleAsc" or "titleDesc" or "trackCountAsc" or "random";

    public static bool IsGenreTrackSort(string sort) =>
        sort is "default" or "title" or "titleAsc" or "titleDesc" or "durationAsc" or "durationDesc" or "random";

    public static bool IsGenreAlbumSort(string sort) =>
        sort is "default" or "title" or "titleAsc" or "titleDesc" or "yearDesc";

    public static bool SupportsFolderTracks(CoreLibraryPageQuery? query)
    {
        if (string.IsNullOrWhiteSpace(query?.FolderId)
            || query.RandomWindow == true
            || query.ExcludeTrackIds is { Count: > ProtocolConstants.MaxExcludeTrackIds })
        {
            return false;
        }

        return true;
    }

    public static bool SupportsPlaylistItems(string? playlistId, CoreLibraryPageQuery? query)
    {
        if (playlistId is not null && string.IsNullOrWhiteSpace(playlistId))
        {
            return false;
        }

        return query?.RandomWindow != true;
    }

    private static bool HasFolderIds(CoreLibraryPageQuery? query) =>
        query?.FolderIds is { Count: > 0 };

    private static bool HasNonLocalSource(CoreLibraryPageQuery? query) =>
        query?.SourceProvider is { Length: > 0 } && query.SourceProvider != "local";

    private static bool IsLocalOnly(CoreLibraryPageQuery? query) =>
        query?.SourceProvider == "local" && string.IsNullOrWhiteSpace(query.SourceId);

    private static bool IsLocalOrOmitted(CoreLibraryPageQuery? query) =>
        (query?.SourceProvider is null or "" or "local") && string.IsNullOrWhiteSpace(query?.SourceId);

    private static bool HasUnsupportedSource(string? sourceProvider) =>
        sourceProvider is "netease" or "qqmusic" or "kugou" or "spotify";

    private static bool IsNumericOrRecentSort(string sort) =>
        sort is "recent"
            or "yearAsc" or "yearDesc"
            or "createdAsc" or "createdDesc"
            or "durationAsc" or "durationDesc"
            or "fileModifiedAsc" or "fileModifiedDesc"
            or "lastPlayed" or "playCountAsc" or "playCountDesc"
            or "trackCountAsc" or "trackCountDesc"
            or "random";

    private static bool IsArtistNumericSort(string sort) =>
        sort is "frequent" or "trackCountAsc" or "trackCountDesc" or "albumCountDesc"
            or "lastPlayed" or "playCountAsc" or "playCountDesc"
            or "recent" or "createdDesc" or "random";
}
