using Echo.Core.Database;
using Echo.Core.Protocol;
using Echo.Core.Query;

namespace Echo.Core.Hosting;

public sealed class RequestExecutor : IDisposable
{
    private readonly Echo.Core.Maintenance.SortKeyMaintenance _sortKeys = new();
    private readonly Echo.Core.Maintenance.GenrePageMaintenance _genres = new();
    public void ReleaseIdleMaintenance() { _sortKeys.ReleasePending(); _genres.Clear(); }
    private readonly ReadOnlyLibraryDatabase _database;
    private long _authorizedGeneration = -1;
    private string? _authorizedPath;

    public RequestExecutor(int databaseBudgetParts = 4) => _database = new(databaseBudgetParts);

    public void ReleaseIdleDatabase() => _database.Close();

    public void InterruptActiveRead() => _database.InterruptReads();

    public object? Execute(CoreRequest request)
    {
        return request.Method switch
        {
            "handshake" => Handshake(request),
            "ping" => new PingResult(),
            "scanJobCommand" => WithOpenDatabase(request, _ => Echo.Core.Writes.ScanJobWriter.Execute(_authorizedPath!, request.Params?.ScanJobCommand
                ?? throw new ArgumentException("Scan task command required."))),
            "libraryMutation" => WithOpenDatabase(request, _ => Echo.Core.Writes.LibraryMutations.Execute(_authorizedPath!, request.Params?.LibraryMutation ?? throw new ArgumentException("Mutation required."), request.Cancellation)),
            "scanFinalize" => WithOpenDatabase(request, _ => Echo.Core.Writes.ScanFinalizeWriter.Execute(_authorizedPath!,
                request.Params?.ScanFinalize ?? throw new ArgumentException("Scan finalize command required."))),
            "rebuildDuplicateIndex" => WithOpenDatabase(request, database => Echo.Core.Duplicates.DuplicateIndexWriter.Rebuild(
                database.RequireConnection(), _authorizedPath!, request.Params?.DuplicateMode)),
            "getDuplicateIndexSummary" => WithOpenDatabase(request, database => Echo.Core.Duplicates.DuplicateIndexQueries.Summary(
                database, request.Params?.DuplicateMode)),
            "getDuplicateHiddenCounts" => WithOpenDatabase(request, database => Echo.Core.Duplicates.DuplicateIndexQueries.HiddenCounts(
                database, request.Params?.TrackIds, request.Params?.DuplicateMode)),
            "getDuplicateVersionsPage" => WithOpenDatabase(request, database => Echo.Core.Duplicates.DuplicateIndexQueries.Versions(
                database, request.Params?.TrackId, request.Params?.DuplicateCursor ?? 0)),
            "getDuplicatePreviewPage" => WithOpenDatabase(request, database => Echo.Core.Duplicates.DuplicateIndexQueries.Preview(
                database, request.Params?.DuplicateMode, request.Params?.DuplicateRevision, request.Params?.DuplicateCursor ?? 0)),
            "getDuplicateCleanupCandidatesPage" => WithOpenDatabase(request, database => Echo.Core.Duplicates.DuplicateCleanupCandidatesQuery.Page(
                database, request.Params?.DuplicateRevision, request.Params?.DuplicateCursor ?? 0)),
            "cleanupPage" => WithOpenDatabase(request, database => Echo.Core.Writes.CleanupWriter.Page(database, request.Params?.Cursor ?? 0, request.Params?.Ceiling)),
            "getExistingTrackPaths" => WithOpenDatabase(request, database => ExistingTrackPathsQuery.Execute(database, request.Params?.Paths)),
            "getScanCacheStates" => WithOpenDatabase(request, database => ScanCacheStatesQuery.Execute(database, request.Params?.ScanCacheStates)),
            "classifyScanFiles" => WithOpenDatabase(request, database => ScanChangedFilesQuery.Execute(database, request.Params?.ScanChangedFiles)),
            "getScanMarkedMissing" => WithOpenDatabase(request, database => ScanMarkedMissingQuery.Execute(database, request.Params?.ScanMarkedMissing)),
            "getCueTrackRefs" => WithOpenDatabase(request, database => CueTrackRefsQuery.Execute(database, request.Params?.RowIdCursor)),
            "getLikedTrackIds" => WithOpenDatabase(request, database => LikedTrackIdsQuery.Execute(database, request.Params?.TrackIds)),
            "getLikedAlbumIds" => WithOpenDatabase(request, database => LikedTrackIdsQuery.Execute(database, request.Params?.TrackIds, albums: true)),
            "prepareFavorites" => WithOpenDatabase(request, _ => Echo.Core.Playlists.FavoriteCommands.Prepare(_authorizedPath!, request.Params?.PlaylistCommand?.MediaType)),
            "previewSmartPlaylist" => WithOpenDatabase(request,database => Echo.Core.SmartPlaylists.SmartPlaylistQueries.Preview(database,request.Params?.PlaylistCommand?.SmartInput ?? default)),
            "getPlaylistCommandResult" => WithOpenDatabase(request, database => PlaylistCommandResultQuery.Execute(database, request.Params?.PlaylistId, request.Params?.ItemIds)),
            "cancel" => new PingResult(),
            "playlistCommand" => WithOpenDatabase(request, _ => Echo.Core.Playlists.PlaylistCommands.Execute(_authorizedPath!,
                request.Params?.PlaylistCommand ?? throw new ArgumentException("Playlist command is required."))),
            "personalTagCommand" => WithOpenDatabase(request, _ => Echo.Core.Personal.PersonalTagCommands.Execute(_authorizedPath!,
                request.Params?.PersonalTagCommand ?? throw new ArgumentException("Personal tag command is required."))),
            "playbackCommand" => WithOpenDatabase(request, _ => Echo.Core.Playback.PlaybackCommands.Execute(_authorizedPath!,
                request.Params?.PlaybackCommand ?? throw new ArgumentException("Playback command is required."))),
            "shutdown" => Shutdown(),
            "prepareSortKeys" => WithOpenDatabase(request, database => request.Params?.SortKeyMaintenance?.Kind == "genre"
                ? _genres.Prepare(database.RequireConnection()) : _sortKeys.Prepare(database.RequireConnection(), request.Params?.SortKeyMaintenance?.Kind)),
            "commitSortKeys" => WithOpenDatabase(request, _ => request.Params?.SortKeyMaintenance?.Kind == "genre"
                ? _genres.Commit(_authorizedPath!, request.Params.SortKeyMaintenance) : _sortKeys.Commit(_authorizedPath!, request.Params?.SortKeyMaintenance)),
            "discardSortKeys" => WithOpenDatabase(request, _ => request.Params?.SortKeyMaintenance?.Kind == "genre"
                ? _genres.Discard(request.Params.SortKeyMaintenance.Token) : _sortKeys.Discard(request.Params?.SortKeyMaintenance?.Token)),
            "getAlbumForTrack" => WithOpenDatabase(request, database => AlbumForTrackQuery.Execute(database, request.Params?.TrackId)),
            "getSummary" => WithOpenDatabase(request, LibrarySummaryQuery.Execute),
            "getTracks" => WithOpenDatabase(request, database =>
            {
                SearchContract.EnsureExecutable(request.Params?.Query, request.Params?.NormalizedSearch);
                var page = TrackPageQuery.Execute(database, request.Params?.Query, request.Params?.NormalizedSearch);
                QueryPlanDiagnostics.Persist();
                return page;
            }),
            "getAlbums" => WithOpenDatabase(request, database =>
            {
                SearchContract.EnsureExecutableEntity(request.Params?.Query, request.Params?.NormalizedEntitySearch);
                var page = AlbumPageQuery.Execute(database, request.Params?.Query, request.Params?.NormalizedEntitySearch);
                QueryPlanDiagnostics.Persist();
                return page;
            }),
            "getArtists" => WithOpenDatabase(request, database =>
            {
                SearchContract.EnsureExecutableEntity(request.Params?.Query, request.Params?.NormalizedEntitySearch);
                var page = ArtistPageQuery.Execute(database, request.Params?.Query, request.Params?.NormalizedEntitySearch);
                QueryPlanDiagnostics.Persist();
                return page;
            }),
            "getGenres" => WithOpenDatabase(request, database =>
            {
                var page = GenrePageQuery.Execute(database, request.Params?.Query);
                QueryPlanDiagnostics.Persist();
                return page;
            }),
            "getAlbumTracks" => WithOpenDatabase(request, database =>
            {
                var page = AlbumTrackPageQuery.Execute(database, request.Params?.AlbumId, request.Params?.Query);
                QueryPlanDiagnostics.Persist();
                return page;
            }),
            "getArtistAlbums" => WithOpenDatabase(request, database =>
                ArtistAlbumPageQuery.Execute(database, request.Params?.ArtistId, request.Params?.Query)),
            "getArtistTracks" => WithOpenDatabase(request, database =>
            {
                var page = ArtistTrackPageQuery.Execute(
                    database,
                    request.Params?.ArtistId,
                    request.Params?.Query,
                    request.Params?.SearchOptions);
                QueryPlanDiagnostics.Persist();
                return page;
            }),
            "getGenreTracks" => WithOpenDatabase(request, database =>
            {
                var page = GenreTrackPageQuery.Execute(database, request.Params?.GenreKey, request.Params?.Query);
                QueryPlanDiagnostics.Persist();
                return page;
            }),
            "getTracksByIds" => WithOpenDatabase(request, database =>
            {
                var items = TrackByIdQuery.Execute(database, request.Params?.TrackIds);
                QueryPlanDiagnostics.Persist();
                return items;
            }),
            "getGenreAlbums" => WithOpenDatabase(request, database =>
            {
                var page = GenreAlbumPageQuery.Execute(database, request.Params?.GenreKey, request.Params?.Query);
                QueryPlanDiagnostics.Persist();
                return page;
            }),
            "getAlbumFocusFacets" => WithOpenDatabase(request, database =>
            {
                var facets = AlbumFocusFacetsQuery.Execute(database);
                QueryPlanDiagnostics.Persist();
                return facets;
            }),
            "getPlaylists" => WithOpenDatabase(request, database =>
            {
                var items = PlaylistListQuery.Execute(database);
                QueryPlanDiagnostics.Persist();
                return items;
            }),
            "getFolderOverviews" => WithOpenDatabase(request, database =>
            {
                var items = FolderOverviewQuery.Execute(database);
                QueryPlanDiagnostics.Persist();
                return items;
            }),
            "getFolderChildren" => WithOpenDatabase(request, database => FolderChildrenQuery.Execute(database,
                request.Params?.FolderChildren ?? throw new ArgumentException("Folder children query required."))),
            "getLibraryQualityOverview" => WithOpenDatabase(request, QualityIssueQuery.Overview),
            "getLibraryQualityIssues" => WithOpenDatabase(request, database => QualityIssueQuery.Issues(database,
                request.Params?.QualityIssues ?? throw new ArgumentException("Quality issue query required."))),
            "getLibraryInboxBatches" => WithOpenDatabase(request, InboxBatchQuery.List),
            "getLibraryInboxTracks" => WithOpenDatabase(request, database => InboxPageQuery.Execute(database,
                request.Params?.InboxQuery ?? new CoreInboxQuery())),
            "getPersonalTags" => WithOpenDatabase(request, database => PersonalTagListQuery.Execute(database,
                request.Params?.PersonalTagTarget)),
            "getPersonalTagItems" => WithOpenDatabase(request, database => Echo.Core.Personal.PersonalTagItemsQuery.Execute(database,
                request.Params?.PersonalTagId, request.Params?.PersonalTagPage)),
            "getSteamAchievementHistoryStats" => WithOpenDatabase(request, database =>
            {
                var stats = SteamAchievementHistoryStatsQuery.Execute(database);
                QueryPlanDiagnostics.Persist();
                return stats;
            }),
            "getPlaybackHistorySummary" => WithOpenDatabase(request, database =>
            {
                var summary = PlaybackHistorySummaryQuery.Execute(
                    database,
                    request.Params?.HistoryQuery,
                    request.Params?.NormalizedEntitySearch);
                QueryPlanDiagnostics.Persist();
                return summary;
            }),
            "getPlaybackHistory" => WithOpenDatabase(request, database =>
            {
                var page = PlaybackHistoryPageQuery.Execute(
                    database,
                    request.Params?.HistoryQuery,
                    request.Params?.NormalizedEntitySearch);
                QueryPlanDiagnostics.Persist();
                return page;
            }),
            "getPlaybackStatsDashboard" => WithOpenDatabase(request, database =>
            {
                var dashboard = PlaybackStatsDashboardQuery.Execute(
                    database,
                    request.Params?.HistoryQuery,
                    request.Params?.NormalizedEntitySearch);
                QueryPlanDiagnostics.Persist();
                return dashboard;
            }),
            "getPlaybackStatsDashboardActivity" => WithOpenDatabase(request, database =>
            {
                var dashboard = PlaybackStatsDashboardQuery.ExecuteActivity(
                    database,
                    request.Params?.HistoryQuery,
                    request.Params?.NormalizedEntitySearch);
                QueryPlanDiagnostics.Persist();
                return dashboard;
            }),
            "getPlaybackStatsInsights" => WithOpenDatabase(request, database =>
            {
                var insights = PlaybackStatsInsightsQuery.Execute(database, request.Params?.HistoryQuery);
                QueryPlanDiagnostics.Persist();
                return insights;
            }),
            "getPlaybackMemoryGraph" => WithOpenDatabase(request, database =>
            {
                var graph = PlaybackMemoryGraphQuery.Execute(
                    database,
                    request.Params?.HistoryQuery,
                    request.Params?.NormalizedEntitySearch);
                QueryPlanDiagnostics.Persist();
                return graph;
            }),
            "getFolderTracks" => WithOpenDatabase(request, database =>
            {
                SearchContract.EnsureExecutableEntity(request.Params?.Query, request.Params?.NormalizedEntitySearch);
                var page = FolderTracksQuery.Execute(
                    database,
                    request.Params?.Query,
                    request.Params?.NormalizedEntitySearch);
                QueryPlanDiagnostics.Persist();
                return page;
            }),
            "getPlaylistItems" or "getFavoriteItems" => WithOpenDatabase(request, database =>
            {
                SearchContract.EnsureExecutableEntity(request.Params?.Query, request.Params?.NormalizedEntitySearch);
                var page = PlaylistItemsQuery.Execute(
                    database,
                    request.Params?.PlaylistId,
                    request.Params?.Query,
                    request.Params?.NormalizedEntitySearch, favoritePage: request.Method == "getFavoriteItems");
                QueryPlanDiagnostics.Persist();
                return page;
            }),
            _ => throw new UnsupportedQueryException($"Method {request.Method} is not on the Core whitelist."),
        };
    }

    public void Dispose() { _sortKeys.Clear(); _genres.Clear(); _database.Dispose(); }

    private HandshakeResult Handshake(CoreRequest request)
    {
        var path = request.Params?.DatabasePath;
        var generation = request.Params?.DatabaseGeneration ?? request.DatabaseGeneration;
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("handshake requires databasePath.");
        }

        var report = _database.Open(path, generation);
        _sortKeys.Clear();
        _genres.Clear();
        _authorizedPath = path;
        _authorizedGeneration = generation;
        var result = new HandshakeResult
        {
            SchemaOk = report.Ok,
            DatabaseGeneration = generation,
            Capabilities = [.. QueryCapabilities.HandshakeCapabilities, ExistingTrackPathsQuery.Capability, ScanCacheStatesQuery.Capability, ScanChangedFilesQuery.Capability, ScanChangedFilesQuery.LegacyCapability, CueTrackRefsQuery.Capability, Echo.Core.Duplicates.DuplicateIndexWriter.Capability, "library.duplicateCleanupPages/v1", Echo.Core.Writes.LibraryMutations.ScanTailCapability, Echo.Core.Writes.LibraryMutations.AlbumGroupingCapability, Echo.Core.Writes.LibraryMutations.GroupingRebuildCapability, Echo.Core.Writes.ScanArtistRefreshWriter.Capability, Echo.Core.Writes.ScanArtistRefreshWriter.InlineCapability, Echo.Core.Writes.ScanAlbumCoverRefreshWriter.Capability, Echo.Core.Writes.ScanFinalizeWriter.Capability, Echo.Core.Media.NativeMetadataProcess.Capability, Echo.Core.Media.NativeMetadataProcess.CancellationCapability, Echo.Core.Media.ScanCueReader.Capability, Echo.Core.Media.ScanCueReader.BatchCapability, Echo.Core.Media.ScanCueReader.IsolationCapability, Echo.Core.Media.ScanCueReader.RipSourcesCapability, Echo.Core.Media.CoverCandidateResolver.Capability, Echo.Core.Media.AsciiSearchTerms.Capability, Echo.Core.Playback.PlaybackCommands.Capability, "sortKeys.maintenance/v1", "genrePages.maintenance/v1", Echo.Core.Search.LibrarySearchNormalizer.Capability, Echo.Core.Playlists.PlaylistCommands.Capability, Echo.Core.Writes.LibraryMutations.Capability],
            Sqlite = report.Sqlite,
        };
        result.Capabilities.Add(CoreExecutionLanes.ParallelMediaCapability);
        result.Capabilities.Add(Echo.Core.Media.ScanCueReader.ValidationCapability);
        result.Capabilities.Add(Echo.Core.Media.ScanCueReader.CompatibilityCapability);
        result.Capabilities.Add(Echo.Core.Writes.ScanJobWriter.Capability);
        result.Capabilities.Add(Echo.Core.Writes.ScanCommittedCounts.Capability);
        result.Capabilities.Add(Echo.Core.Writes.ScanExecutionCheckpoint.Capability);
        result.Capabilities.Add(Echo.Core.Writes.ScanExecutionCheckpoint.PageBudgetCapability);
        result.Capabilities.Add(Echo.Core.Writes.ScanExecutionCheckpoint.PageCursorCapability);
        if (Echo.Core.Writes.ScanExecutionDispatcher.IsAvailable(_database.RequireConnection()))
            result.Capabilities.Add(Echo.Core.Writes.ScanExecutionDispatcher.Capability);
        result.Capabilities.Add(ScanChangedFilesQuery.CueRevisionCapability);
        result.Capabilities.Add(CoreExecutionLanes.ParallelMediaCapabilityV2);
        return result;
    }

    private PingResult Shutdown()
    {
        _sortKeys.Clear();
        _genres.Clear();
        _database.Close();
        _authorizedPath = null;
        _authorizedGeneration = -1;
        return new PingResult();
    }

    private T WithOpenDatabase<T>(CoreRequest request, Func<ReadOnlyLibraryDatabase, T> execute)
    {
        var generation = request.DatabaseGeneration;
        if (_authorizedGeneration < 0)
        {
            throw new InvalidOperationException("Core has not completed handshake.");
        }

        if (generation != _authorizedGeneration)
        {
            throw new StaleDatabaseGenerationException(_authorizedGeneration, generation);
        }

        if (!string.IsNullOrWhiteSpace(request.Params?.DatabasePath)
            && !string.Equals(request.Params.DatabasePath, _authorizedPath, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Request databasePath does not match the authorized handshake path.");
        }

        if (_database.OpenedGeneration != generation)
            _database.Open(_authorizedPath!, generation);
        Echo.Core.Search.LibrarySearchNormalizer.Prepare(request);
        return execute(_database);
    }
}

public sealed class StaleDatabaseGenerationException : Exception
{
    public StaleDatabaseGenerationException(long authorized, long received)
        : base($"Rejected stale database generation {received}; authorized generation is {authorized}.")
    {
        AuthorizedGeneration = authorized;
        ReceivedGeneration = received;
    }

    public long AuthorizedGeneration { get; }
    public long ReceivedGeneration { get; }
}
