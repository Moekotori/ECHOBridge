using System.Text.Json;
using System.Text.Json.Serialization;

namespace Echo.Core.Protocol;

public sealed class CoreRequest
{
    [JsonIgnore]
    public CancellationToken Cancellation { get; set; }
    [JsonPropertyName("v")]
    public int Version { get; set; }

    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    [JsonPropertyName("deadlineMs")]
    public int DeadlineMs { get; set; } = ProtocolConstants.DefaultDeadlineMs;

    [JsonPropertyName("databaseGeneration")]
    public long DatabaseGeneration { get; set; }

    [JsonPropertyName("params")]
    public CoreRequestParams? Params { get; set; }
}

public sealed class CoreRequestParams
{
    public ScanCueRequest? ScanCue { get; set; }
    public ScanJobCommand? ScanJobCommand { get; set; }
    public LibraryMutation? LibraryMutation { get; set; }
    [JsonPropertyName("nativeMetadata")]
    public NativeMetadataRequest? NativeMetadata { get; set; }
    [JsonPropertyName("coverCandidate")]
    public CoverCandidateRequest? CoverCandidate { get; set; }
    [JsonPropertyName("asciiSearchTerms")]
    public AsciiSearchTermsRequest? AsciiSearchTerms { get; set; }
    [JsonPropertyName("scanFinalize")]
    public ScanFinalizeCommand? ScanFinalize { get; set; }
    public long Cursor { get; set; }
    public long? Ceiling { get; set; }
    [JsonPropertyName("paths")]
    public List<string>? Paths { get; set; }
    [JsonPropertyName("playlistCommand")]
    public PlaylistCommand? PlaylistCommand { get; set; }
    [JsonPropertyName("personalTagCommand")]
    public PersonalTagCommand? PersonalTagCommand { get; set; }
    [JsonPropertyName("playbackCommand")]
    public PlaybackCommand? PlaybackCommand { get; set; }
    [JsonPropertyName("normalizeSearch")]
    public bool NormalizeSearch { get; set; }

    [JsonPropertyName("sortKeyMaintenance")]
    public SortKeyMaintenanceParams? SortKeyMaintenance { get; set; }

    [JsonPropertyName("scanCacheStates")]
    public ScanCacheStatesParams? ScanCacheStates { get; set; }
    [JsonPropertyName("scanChangedFiles")]
    public ScanChangedFilesParams? ScanChangedFiles { get; set; }

    [JsonPropertyName("scanMarkedMissing")]
    public ScanMarkedMissingParams? ScanMarkedMissing { get; set; }

    [JsonPropertyName("rowIdCursor")]
    public RowIdCursorParams? RowIdCursor { get; set; }

    [JsonPropertyName("databasePath")]
    public string? DatabasePath { get; set; }

    [JsonPropertyName("databaseGeneration")]
    public long? DatabaseGeneration { get; set; }

    [JsonPropertyName("searchOptions")]
    public CoreSearchOptions? SearchOptions { get; set; }

    [JsonPropertyName("query")]
    public CoreLibraryPageQuery? Query { get; set; }

    [JsonPropertyName("folderChildren")]
    public CoreFolderChildrenQuery? FolderChildren { get; set; }

    [JsonPropertyName("qualityIssues")]
    public CoreQualityIssueQuery? QualityIssues { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("inboxQuery")]
    public CoreInboxQuery? InboxQuery { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("personalTagTarget")]
    public CorePersonalTagTarget? PersonalTagTarget { get; set; }

    [JsonPropertyName("personalTagId")]
    public string? PersonalTagId { get; set; }

    [JsonPropertyName("personalTagPage")]
    public int? PersonalTagPage { get; set; }

    [JsonPropertyName("normalizedSearch")]
    public CoreNormalizedTrackSearch? NormalizedSearch { get; set; }

    [JsonPropertyName("normalizedEntitySearch")]
    public CoreNormalizedEntitySearch? NormalizedEntitySearch { get; set; }

    [JsonPropertyName("requestId")]
    public long? RequestId { get; set; }

    [JsonPropertyName("albumId")]
    public string? AlbumId { get; set; }

    [JsonPropertyName("artistId")]
    public string? ArtistId { get; set; }

    [JsonPropertyName("genreKey")]
    public string? GenreKey { get; set; }

    [JsonPropertyName("trackId")]
    public string? TrackId { get; set; }

    [JsonPropertyName("trackIds")]
    public List<string>? TrackIds { get; set; }

    [JsonPropertyName("duplicateMode")]
    public string? DuplicateMode { get; set; }

    [JsonPropertyName("duplicateCursor")]
    public long? DuplicateCursor { get; set; }

    [JsonPropertyName("duplicateRevision")]
    public string? DuplicateRevision { get; set; }

    public List<string>? ItemIds { get; set; }

    [JsonPropertyName("historyQuery")]
    public CorePlaybackHistoryQuery? HistoryQuery { get; set; }

    [JsonPropertyName("playlistId")]
    public string? PlaylistId { get; set; }
}

public sealed class CorePlaybackHistoryQuery
{
    [JsonPropertyName("sortDirection")]
    public string? SortDirection { get; set; }

    [JsonPropertyName("search")]
    public string? Search { get; set; }

    [JsonPropertyName("from")]
    public string? From { get; set; }

    [JsonPropertyName("to")]
    public string? To { get; set; }

    [JsonPropertyName("completedOnly")]
    public bool? CompletedOnly { get; set; }

    [JsonPropertyName("mediaType")]
    public string? MediaType { get; set; }

    [JsonPropertyName("activeRemoteSourceIds")]
    public List<string>? ActiveRemoteSourceIds { get; set; }

    [JsonPropertyName("page")]
    public int? Page { get; set; }

    [JsonPropertyName("pageSize")]
    public int? PageSize { get; set; }

    [JsonPropertyName("sort")]
    public string? Sort { get; set; }

    [JsonPropertyName("view")]
    public string? View { get; set; }

    [JsonPropertyName("statsMode")]
    public string? StatsMode { get; set; }

    [JsonPropertyName("snapshot")]
    public long? Snapshot { get; set; }
}

public sealed class CoreSearchOptions
{
    [JsonPropertyName("chineseCrossScriptSearchEnabled")]
    public bool ChineseCrossScriptSearchEnabled { get; set; } = true;

    [JsonPropertyName("artistMergeStrategy")]
    public string ArtistMergeStrategy { get; set; } = "standard";

    [JsonPropertyName("remoteAlbumMergeStrategy")]
    public string RemoteAlbumMergeStrategy { get; set; } = "conservative";
}

public sealed class CoreLibraryPageQuery
{
    [JsonPropertyName("page")]
    public int? Page { get; set; }

    [JsonPropertyName("pageSize")]
    public int? PageSize { get; set; }

    [JsonPropertyName("offset")]
    public int? Offset { get; set; }

    [JsonPropertyName("totalHint")]
    public int? TotalHint { get; set; }

    [JsonPropertyName("search")]
    public string? Search { get; set; }

    [JsonPropertyName("sort")]
    public string? Sort { get; set; }

    [JsonPropertyName("sorts")]
    public List<string>? Sorts { get; set; }

    [JsonPropertyName("audioFormatFilter")]
    public string? AudioFormatFilter { get; set; }

    [JsonPropertyName("fileFormatFilter")]
    public string? FileFormatFilter { get; set; }

    [JsonPropertyName("vocalFilter")]
    public string? VocalFilter { get; set; }

    [JsonPropertyName("folderIds")]
    public List<string>? FolderIds { get; set; }

    [JsonPropertyName("sourceProvider")]
    public string? SourceProvider { get; set; }

    [JsonPropertyName("sourceId")]
    public string? SourceId { get; set; }

    [JsonPropertyName("hideDuplicates")]
    public bool? HideDuplicates { get; set; }

    [JsonPropertyName("showDuplicatesOnly")]
    public bool? ShowDuplicatesOnly { get; set; }

    [JsonPropertyName("showOsuOnly")]
    public bool? ShowOsuOnly { get; set; }

    [JsonPropertyName("excludeOsuAlbums")]
    public bool? ExcludeOsuAlbums { get; set; }

    [JsonPropertyName("duplicateMode")]
    public string? DuplicateMode { get; set; }

    [JsonPropertyName("excludeTrackIds")]
    public List<string>? ExcludeTrackIds { get; set; }

    [JsonPropertyName("randomWindow")]
    public bool? RandomWindow { get; set; }

    [JsonPropertyName("folderInsight")]
    public string? FolderInsight { get; set; }

    [JsonPropertyName("folderId")]
    public string? FolderId { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("recursive")]
    public bool? Recursive { get; set; }

    [JsonPropertyName("personalTagId")]
    public string? PersonalTagId { get; set; }

    [JsonPropertyName("collapseAlbumVersions")]
    public bool? CollapseAlbumVersions { get; set; }

    [JsonPropertyName("albumFocus")]
    public CoreAlbumFocus? AlbumFocus { get; set; }

    [JsonPropertyName("projection")]
    public string? Projection { get; set; }

    [JsonPropertyName("artistGrouping")]
    public string? ArtistGrouping { get; set; }

    [JsonPropertyName("prioritizeArtistAvatars")]
    public bool? PrioritizeArtistAvatars { get; set; }
}

public sealed class CoreAlbumFocus
{
    [JsonPropertyName("decade")]
    public int? Decade { get; set; }

    [JsonPropertyName("genreKey")]
    public string? GenreKey { get; set; }

    [JsonPropertyName("audioFormat")]
    public string? AudioFormat { get; set; }

    [JsonPropertyName("bitDepth")]
    public int? BitDepth { get; set; }

    [JsonPropertyName("fileFormat")]
    public string? FileFormat { get; set; }

    [JsonPropertyName("criteria")]
    public List<CoreAlbumFocusCriterion>? Criteria { get; set; }

    [JsonPropertyName("listening")]
    public string? Listening { get; set; }
}

public sealed class CoreNormalizedTrackSearch
{
    [JsonPropertyName("ftsMatch")]
    public string? FtsMatch { get; set; }

    [JsonPropertyName("year")]
    public int? Year { get; set; }

    [JsonPropertyName("excludes")]
    public List<CoreSearchExclude>? Excludes { get; set; }
}

public sealed class CoreSearchExclude
{
    [JsonPropertyName("substring")]
    public bool Substring { get; set; }

    [JsonPropertyName("variants")]
    public List<string> Variants { get; set; } = [];
}

public sealed class CoreNormalizedEntitySearch
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = ProtocolConstants.LikeEntitySearchVersion;

    [JsonPropertyName("terms")]
    public List<CoreNormalizedEntitySearchTerm> Terms { get; set; } = [];
}

public sealed class CoreNormalizedEntitySearchTerm
{
    [JsonPropertyName("variants")]
    public List<string> Variants { get; set; } = [];
}

public sealed class CoreResponse
{
    [JsonPropertyName("v")]
    public int Version { get; set; } = ProtocolConstants.ProtocolVersion;

    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("executedBy")]
    public string ExecutedBy { get; set; } = "dotnet";

    [JsonPropertyName("result")]
    public JsonElement? Result { get; set; }

    [JsonPropertyName("error")]
    public CoreError? Error { get; set; }
}

public sealed class CoreError
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = "error";

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("retryable")]
    public bool Retryable { get; set; }
}

public sealed class HandshakeResult
{
    [JsonPropertyName("buildId")]
    public string BuildId { get; set; } = typeof(HandshakeResult).Module.ModuleVersionId.ToString("N");

    [JsonPropertyName("protocolVersion")]
    public int ProtocolVersion { get; set; } = ProtocolConstants.ProtocolVersion;

    [JsonPropertyName("schemaOk")]
    public bool SchemaOk { get; set; }

    [JsonPropertyName("databaseGeneration")]
    public long DatabaseGeneration { get; set; }

    [JsonPropertyName("capabilities")]
    public List<string> Capabilities { get; set; } = [];

    [JsonPropertyName("sqlite")]
    public Dictionary<string, string> Sqlite { get; set; } = [];
}

public sealed class PingResult
{
    [JsonPropertyName("pong")]
    public bool Pong { get; set; } = true;

    [JsonPropertyName("resourceUsage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Echo.Core.Hosting.CoreResourceUsage? ResourceUsage { get; set; }
}

public sealed class CancelResult
{
    [JsonPropertyName("received")]
    public bool Received { get; set; }

    [JsonPropertyName("queuedCancelled")]
    public bool QueuedCancelled { get; set; }

    [JsonPropertyName("running")]
    public bool Running { get; set; }

    [JsonPropertyName("sqlStopped")]
    public bool SqlStopped { get; set; }
}

public sealed class LibrarySummaryDto
{
    [JsonPropertyName("songCount")]
    public long SongCount { get; set; }

    [JsonPropertyName("albumCount")]
    public long AlbumCount { get; set; }

    [JsonPropertyName("artistCount")]
    public long ArtistCount { get; set; }

    [JsonPropertyName("folderCount")]
    public long FolderCount { get; set; }

    [JsonPropertyName("totalDuration")]
    public double TotalDuration { get; set; }

    [JsonPropertyName("lastScanAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? LastScanAt { get; set; }
}

public sealed class LibraryPageDto<T>
{
    [JsonPropertyName("items")]
    public List<T> Items { get; set; } = [];

    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; }

    [JsonPropertyName("total")]
    public long Total { get; set; }

    [JsonPropertyName("hasMore")]
    public bool HasMore { get; set; }

    [JsonPropertyName("snapshot")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Snapshot { get; set; }

    [JsonPropertyName("daySummaries")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<PlaybackHistoryDaySummaryDto>? DaySummaries { get; set; }
}

public sealed class LibraryAlbumDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("mediaType")]
    public string MediaType { get; set; } = "local";

    [JsonPropertyName("sourceId")]
    public string? SourceId { get; set; }

    [JsonPropertyName("sourceDisplayName")]
    public string? SourceDisplayName { get; set; }

    [JsonPropertyName("provider")]
    public string? Provider { get; set; }

    [JsonPropertyName("albumKey")]
    public string AlbumKey { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("albumArtist")]
    public string AlbumArtist { get; set; } = string.Empty;

    [JsonPropertyName("year")]
    public int? Year { get; set; }

    [JsonPropertyName("trackCount")]
    public int TrackCount { get; set; }

    [JsonPropertyName("duration")]
    public double Duration { get; set; }

    [JsonPropertyName("coverId")]
    public string? CoverId { get; set; }

    [JsonPropertyName("coverSource")]
    public string? CoverSource { get; set; }

    [JsonPropertyName("coverThumb")]
    public string? CoverThumb { get; set; }
}

public sealed class LibraryArtistDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("mediaType")]
    public string MediaType { get; set; } = "local";

    [JsonPropertyName("sourceId")]
    public string? SourceId { get; set; }

    [JsonPropertyName("sourceDisplayName")]
    public string? SourceDisplayName { get; set; }

    [JsonPropertyName("provider")]
    public string? Provider { get; set; }

    [JsonPropertyName("artistKey")]
    public string ArtistKey { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("sortName")]
    public string SortName { get; set; } = string.Empty;

    [JsonPropertyName("role")]
    public string Role { get; set; } = "track";

    [JsonPropertyName("trackCount")]
    public int TrackCount { get; set; }

    [JsonPropertyName("albumCount")]
    public int AlbumCount { get; set; }

    [JsonPropertyName("coverId")]
    public string? CoverId { get; set; }

    [JsonPropertyName("coverThumb")]
    public string? CoverThumb { get; set; }

    [JsonPropertyName("coverSource")]
    public string? CoverSource { get; set; }

    [JsonPropertyName("avatarThumbUrl")]
    public string? AvatarThumbUrl { get; set; }

    [JsonPropertyName("avatarUrl")]
    public string? AvatarUrl { get; set; }

    [JsonPropertyName("avatarStatus")]
    public string? AvatarStatus { get; set; }

    [JsonPropertyName("avatarProvider")]
    public string? AvatarProvider { get; set; }
}

public sealed class LibraryGenreDto
{
    [JsonPropertyName("genreKey")]
    public string GenreKey { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("unclassified")]
    public bool Unclassified { get; set; }

    [JsonPropertyName("mediaType")]
    public string MediaType { get; set; } = "local";

    [JsonPropertyName("sourceId")]
    public string? SourceId { get; set; }

    [JsonPropertyName("sourceDisplayName")]
    public string? SourceDisplayName { get; set; }

    [JsonPropertyName("trackCount")]
    public int TrackCount { get; set; }

    [JsonPropertyName("albumCount")]
    public int AlbumCount { get; set; }

    [JsonPropertyName("coverId")]
    public string? CoverId { get; set; }

    [JsonPropertyName("coverThumb")]
    public string? CoverThumb { get; set; }
}

public sealed class AlbumFocusFacetsDto
{
    [JsonPropertyName("decades")]
    public List<AlbumFocusDecadeFacetDto> Decades { get; set; } = [];

    [JsonPropertyName("genres")]
    public List<AlbumFocusGenreFacetDto> Genres { get; set; } = [];

    [JsonPropertyName("audioFormats")]
    public List<AlbumFocusAudioFormatFacetDto> AudioFormats { get; set; } = [];

    [JsonPropertyName("bitDepths")]
    public List<AlbumFocusBitDepthFacetDto> BitDepths { get; set; } = [];

    [JsonPropertyName("fileFormats")]
    public List<AlbumFocusFileFormatFacetDto> FileFormats { get; set; } = [];
}

public sealed class AlbumFocusDecadeFacetDto
{
    [JsonPropertyName("decade")]
    public int Decade { get; set; }

    [JsonPropertyName("albumCount")]
    public int AlbumCount { get; set; }
}

public sealed class AlbumFocusGenreFacetDto
{
    [JsonPropertyName("genreKey")]
    public string GenreKey { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("albumCount")]
    public int AlbumCount { get; set; }
}

public sealed class AlbumFocusAudioFormatFacetDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("albumCount")]
    public int AlbumCount { get; set; }
}

public sealed class AlbumFocusBitDepthFacetDto
{
    [JsonPropertyName("bits")]
    public int Bits { get; set; }

    [JsonPropertyName("albumCount")]
    public int AlbumCount { get; set; }
}

public sealed class AlbumFocusFileFormatFacetDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("albumCount")]
    public int AlbumCount { get; set; }
}

public sealed class LibraryPlaylistDto
{
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public System.Text.Json.JsonElement? SmartRules { get; set; }
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Description { get; set; }

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "manual";

    [JsonPropertyName("sourceProvider")]
    public string SourceProvider { get; set; } = "local";

    [JsonPropertyName("sourcePlaylistId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? SourcePlaylistId { get; set; }

    [JsonPropertyName("coverId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? CoverId { get; set; }

    [JsonPropertyName("coverThumb")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? CoverThumb { get; set; }

    [JsonPropertyName("sortMode")]
    public string SortMode { get; set; } = "manual";

    [JsonPropertyName("itemCount")]
    public int ItemCount { get; set; }

    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; set; } = string.Empty;

    [JsonPropertyName("updatedAt")]
    public string UpdatedAt { get; set; } = string.Empty;
}

public sealed class LibraryPlaylistItemDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("playlistId")]
    public string PlaylistId { get; set; } = string.Empty;

    [JsonPropertyName("mediaType")]
    public string MediaType { get; set; } = "track";

    [JsonPropertyName("mediaId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? MediaId { get; set; }

    [JsonPropertyName("sourceProvider")]
    public string SourceProvider { get; set; } = "local";

    [JsonPropertyName("sourceItemId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? SourceItemId { get; set; }

    [JsonPropertyName("titleSnapshot")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? TitleSnapshot { get; set; }

    [JsonPropertyName("artistSnapshot")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? ArtistSnapshot { get; set; }

    [JsonPropertyName("albumSnapshot")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? AlbumSnapshot { get; set; }

    [JsonPropertyName("durationSnapshot")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public double? DurationSnapshot { get; set; }

    [JsonPropertyName("coverId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? CoverId { get; set; }

    [JsonPropertyName("coverThumb")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? CoverThumb { get; set; }

    [JsonPropertyName("position")]
    public int Position { get; set; }

    [JsonPropertyName("addedAt")]
    public string AddedAt { get; set; } = string.Empty;

    [JsonPropertyName("addedFrom")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? AddedFrom { get; set; }

    [JsonPropertyName("unavailable")]
    public bool Unavailable { get; set; }

    [JsonPropertyName("track")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public LibraryTrackDto? Track { get; set; }

    [JsonPropertyName("album")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public LibraryAlbumDto? Album { get; set; }
}

public sealed class LibraryFolderOverviewDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = "active";

    [JsonPropertyName("importProfile")]
    public string ImportProfile { get; set; } = "normal";

    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; set; } = string.Empty;

    [JsonPropertyName("updatedAt")]
    public string UpdatedAt { get; set; } = string.Empty;

    [JsonPropertyName("lastScanAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? LastScanAt { get; set; }

    [JsonPropertyName("recentScan")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public LibraryScanStatusDto? RecentScan { get; set; }

    [JsonPropertyName("trackCount")]
    public int TrackCount { get; set; }

    [JsonPropertyName("albumCount")]
    public int AlbumCount { get; set; }

    [JsonPropertyName("artistCount")]
    public int ArtistCount { get; set; }

    [JsonPropertyName("totalDuration")]
    public double TotalDuration { get; set; }

    [JsonPropertyName("totalSizeBytes")]
    public long TotalSizeBytes { get; set; }

    [JsonPropertyName("missingTrackCount")]
    public int MissingTrackCount { get; set; }

    [JsonPropertyName("losslessTrackCount")]
    public int LosslessTrackCount { get; set; }

    [JsonPropertyName("hiResTrackCount")]
    public int HiResTrackCount { get; set; }

    [JsonPropertyName("unplayedTrackCount")]
    public int UnplayedTrackCount { get; set; }

    [JsonPropertyName("missingCoverTrackCount")]
    public int MissingCoverTrackCount { get; set; }

    [JsonPropertyName("missingLyricsTrackCount")]
    public int MissingLyricsTrackCount { get; set; }

    [JsonPropertyName("duplicateTrackCount")]
    public int DuplicateTrackCount { get; set; }

    [JsonPropertyName("recentlyAddedTrackCount")]
    public int RecentlyAddedTrackCount { get; set; }

    [JsonPropertyName("childFolderCount")]
    public int ChildFolderCount { get; set; }

    [JsonPropertyName("coverThumbs")]
    public List<string> CoverThumbs { get; set; } = [];
}

public sealed class LibraryScanStatusDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("folderId")]
    public string FolderId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = "failed";

    [JsonPropertyName("phase")]
    public string Phase { get; set; } = "queued";

    [JsonPropertyName("totalFiles")]
    public int TotalFiles { get; set; }

    [JsonPropertyName("processedFiles")]
    public int ProcessedFiles { get; set; }

    [JsonPropertyName("skippedFiles")]
    public int SkippedFiles { get; set; }

    [JsonPropertyName("addedTracks")]
    public int AddedTracks { get; set; }

    [JsonPropertyName("updatedTracks")]
    public int UpdatedTracks { get; set; }

    [JsonPropertyName("removedTracks")]
    public int RemovedTracks { get; set; }

    [JsonPropertyName("coverCount")]
    public int CoverCount { get; set; }

    [JsonPropertyName("errorCount")]
    public int ErrorCount { get; set; }

    [JsonPropertyName("errors")]
    public List<string> Errors { get; set; } = [];

    [JsonPropertyName("startedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? StartedAt { get; set; }

    [JsonPropertyName("finishedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? FinishedAt { get; set; }
}

public sealed class SteamAchievementHistoryStatsDto
{
    [JsonPropertyName("totalPlayedSeconds")]
    public double TotalPlayedSeconds { get; set; }

    [JsonPropertyName("completedUniqueTracks")]
    public int CompletedUniqueTracks { get; set; }

    [JsonPropertyName("qualifiedCompletedPlayCount")]
    public int QualifiedCompletedPlayCount { get; set; }

    [JsonPropertyName("longestCompletionStreakDays")]
    public int LongestCompletionStreakDays { get; set; }

    [JsonPropertyName("nightPlayedSeconds")]
    public double NightPlayedSeconds { get; set; }

    [JsonPropertyName("hasFavoriteAlbum")]
    public bool HasFavoriteAlbum { get; set; }

    [JsonPropertyName("completedShortUniqueTracks")]
    public int CompletedShortUniqueTracks { get; set; }

    [JsonPropertyName("hasCompletedZhaoXiaoliuTrack")]
    public bool HasCompletedZhaoXiaoliuTrack { get; set; }

    [JsonPropertyName("completedUniqueAlbums")]
    public int CompletedUniqueAlbums { get; set; }

    [JsonPropertyName("completedUniqueArtists")]
    public int CompletedUniqueArtists { get; set; }

    [JsonPropertyName("completedTimeBucketCount")]
    public int CompletedTimeBucketCount { get; set; }

    [JsonPropertyName("completedLongUniqueTracks")]
    public int CompletedLongUniqueTracks { get; set; }

    [JsonPropertyName("maxActiveDatesInRolling30Days")]
    public int MaxActiveDatesInRolling30Days { get; set; }
}

public sealed class PlaybackHistorySummaryDto
{
    [JsonPropertyName("todayCount")]
    public int TodayCount { get; set; }

    [JsonPropertyName("todayPlayedSeconds")]
    public double TodayPlayedSeconds { get; set; }

    [JsonPropertyName("totalCount")]
    public int TotalCount { get; set; }

    [JsonPropertyName("latestPlayedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? LatestPlayedAt { get; set; }

    [JsonPropertyName("rangeCount")]
    public int RangeCount { get; set; }

    [JsonPropertyName("rangeTrackCount")]
    public int RangeTrackCount { get; set; }

    [JsonPropertyName("rangePlayedSeconds")]
    public double RangePlayedSeconds { get; set; }

    [JsonPropertyName("rangeLatestPlayedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? RangeLatestPlayedAt { get; set; }
}

public sealed class PlaybackHistoryEntryDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("trackId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? TrackId { get; set; }

    [JsonPropertyName("trackPath")]
    public string TrackPath { get; set; } = string.Empty;

    [JsonPropertyName("mediaType")]
    public string MediaType { get; set; } = "local";

    [JsonPropertyName("provider")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Provider { get; set; }

    [JsonPropertyName("providerTrackId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? ProviderTrackId { get; set; }

    [JsonPropertyName("stableKey")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? StableKey { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("artist")]
    public string Artist { get; set; } = string.Empty;

    [JsonPropertyName("album")]
    public string Album { get; set; } = string.Empty;

    [JsonPropertyName("albumArtist")]
    public string AlbumArtist { get; set; } = string.Empty;

    [JsonPropertyName("coverId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? CoverId { get; set; }

    [JsonPropertyName("coverThumb")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? CoverThumb { get; set; }

    [JsonPropertyName("startedAt")]
    public string StartedAt { get; set; } = string.Empty;

    [JsonPropertyName("endedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? EndedAt { get; set; }

    [JsonPropertyName("playedSeconds")]
    public double PlayedSeconds { get; set; }

    [JsonPropertyName("durationSeconds")]
    public double DurationSeconds { get; set; }

    [JsonPropertyName("durationSnapshot")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public double? DurationSnapshot { get; set; }

    [JsonPropertyName("coverSnapshot")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? CoverSnapshot { get; set; }

    [JsonPropertyName("playCount")]
    public int PlayCount { get; set; }

    [JsonPropertyName("completed")]
    public bool Completed { get; set; }

    [JsonPropertyName("sourceType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? SourceType { get; set; }

    [JsonPropertyName("sourceLabel")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? SourceLabel { get; set; }

    [JsonPropertyName("queueId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? QueueId { get; set; }
}

public sealed class PlaybackStatsDashboardDto
{
    [JsonPropertyName("generatedAt")]
    public string GeneratedAt { get; set; } = string.Empty;

    [JsonPropertyName("totals")]
    public PlaybackStatsTotalsDto Totals { get; set; } = new();

    [JsonPropertyName("topTracks")]
    public List<PlaybackStatsTrackDto> TopTracks { get; set; } = [];

    [JsonPropertyName("topArtists")]
    public List<PlaybackStatsArtistDto> TopArtists { get; set; } = [];

    [JsonPropertyName("topAlbums")]
    public List<PlaybackStatsAlbumDto> TopAlbums { get; set; } = [];

    [JsonPropertyName("formatBreakdown")]
    public List<PlaybackStatsBreakdownDto> FormatBreakdown { get; set; } = [];

    [JsonPropertyName("qualityBreakdown")]
    public List<PlaybackStatsBreakdownDto> QualityBreakdown { get; set; } = [];

    [JsonPropertyName("dailyActivity")]
    public List<PlaybackStatsDayDto> DailyActivity { get; set; } = [];
}

public sealed class PlaybackStatsTotalsDto
{
    [JsonPropertyName("playCount")]
    public int PlayCount { get; set; }

    [JsonPropertyName("completedCount")]
    public int CompletedCount { get; set; }

    [JsonPropertyName("playedSeconds")]
    public double PlayedSeconds { get; set; }

    [JsonPropertyName("uniqueTracks")]
    public int UniqueTracks { get; set; }

    [JsonPropertyName("uniqueArtists")]
    public int UniqueArtists { get; set; }
}

public sealed class PlaybackStatsTrackDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("trackId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? TrackId { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("artist")]
    public string Artist { get; set; } = string.Empty;

    [JsonPropertyName("album")]
    public string Album { get; set; } = string.Empty;

    [JsonPropertyName("coverThumb")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? CoverThumb { get; set; }

    [JsonPropertyName("playCount")]
    public int PlayCount { get; set; }

    [JsonPropertyName("completedCount")]
    public int CompletedCount { get; set; }

    [JsonPropertyName("playedSeconds")]
    public double PlayedSeconds { get; set; }

    [JsonPropertyName("durationSeconds")]
    public double DurationSeconds { get; set; }

    [JsonPropertyName("lastPlayedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? LastPlayedAt { get; set; }
}

public sealed class PlaybackStatsArtistDto
{
    [JsonPropertyName("artist")]
    public string Artist { get; set; } = string.Empty;

    [JsonPropertyName("playCount")]
    public int PlayCount { get; set; }

    [JsonPropertyName("completedCount")]
    public int CompletedCount { get; set; }

    [JsonPropertyName("playedSeconds")]
    public double PlayedSeconds { get; set; }
}

public sealed class PlaybackStatsAlbumDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("albumId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? AlbumId { get; set; }

    [JsonPropertyName("mediaType")]
    public string MediaType { get; set; } = "local";

    [JsonPropertyName("albumKey")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? AlbumKey { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("albumArtist")]
    public string AlbumArtist { get; set; } = string.Empty;

    [JsonPropertyName("year")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public int? Year { get; set; }

    [JsonPropertyName("trackCount")]
    public int TrackCount { get; set; }

    [JsonPropertyName("duration")]
    public double Duration { get; set; }

    [JsonPropertyName("coverId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? CoverId { get; set; }

    [JsonPropertyName("coverThumb")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? CoverThumb { get; set; }

    [JsonPropertyName("playCount")]
    public int PlayCount { get; set; }

    [JsonPropertyName("completedCount")]
    public int CompletedCount { get; set; }

    [JsonPropertyName("playedSeconds")]
    public double PlayedSeconds { get; set; }

    [JsonPropertyName("lastPlayedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? LastPlayedAt { get; set; }
}

public sealed class PlaybackStatsBreakdownDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("playCount")]
    public int PlayCount { get; set; }

    [JsonPropertyName("playedSeconds")]
    public double PlayedSeconds { get; set; }
}

public sealed class PlaybackStatsDayDto
{
    [JsonPropertyName("date")]
    public string Date { get; set; } = string.Empty;

    [JsonPropertyName("playCount")]
    public int PlayCount { get; set; }

    [JsonPropertyName("playedSeconds")]
    public double PlayedSeconds { get; set; }
}

public sealed class PlaybackStatsInsightsDto
{
    [JsonPropertyName("comparison")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public PlaybackStatsInsightsComparisonDto? Comparison { get; set; }

    [JsonPropertyName("sessions")]
    public PlaybackStatsInsightsSessionsDto Sessions { get; set; } = new();

    [JsonPropertyName("discovery")]
    public PlaybackStatsInsightsDiscoveryDto Discovery { get; set; } = new();
}

public sealed class PlaybackStatsInsightsComparisonDto
{
    [JsonPropertyName("current")]
    public PlaybackStatsPeriodTotalsDto Current { get; set; } = new();

    [JsonPropertyName("previous")]
    public PlaybackStatsPeriodTotalsDto Previous { get; set; } = new();
}

public sealed class PlaybackStatsPeriodTotalsDto
{
    [JsonPropertyName("playCount")]
    public int PlayCount { get; set; }

    [JsonPropertyName("playedSeconds")]
    public double PlayedSeconds { get; set; }

    [JsonPropertyName("uniqueTracks")]
    public int UniqueTracks { get; set; }
}

public sealed class PlaybackStatsInsightsSessionsDto
{
    [JsonPropertyName("sessionCount")]
    public int SessionCount { get; set; }

    [JsonPropertyName("averagePlayedSeconds")]
    public double AveragePlayedSeconds { get; set; }

    [JsonPropertyName("longestPlayedSeconds")]
    public double LongestPlayedSeconds { get; set; }

    [JsonPropertyName("averageTrackCount")]
    public double AverageTrackCount { get; set; }

    [JsonPropertyName("activeDays")]
    public int ActiveDays { get; set; }

    [JsonPropertyName("currentStreakDays")]
    public int CurrentStreakDays { get; set; }

    [JsonPropertyName("longestStreakDays")]
    public int LongestStreakDays { get; set; }
}

public sealed class PlaybackStatsInsightsDiscoveryDto
{
    [JsonPropertyName("hasBoundedRange")]
    public bool HasBoundedRange { get; set; }

    [JsonPropertyName("newTrackCount")]
    public int NewTrackCount { get; set; }

    [JsonPropertyName("returningTrackCount")]
    public int ReturningTrackCount { get; set; }

    [JsonPropertyName("replayedTrackCount")]
    public int ReplayedTrackCount { get; set; }

    [JsonPropertyName("repeatPlayRate")]
    public double RepeatPlayRate { get; set; }

    [JsonPropertyName("rediscoveredTrackCount")]
    public int RediscoveredTrackCount { get; set; }
}

public sealed class PlaybackMemoryGraphDto
{
    [JsonPropertyName("generatedAt")]
    public string GeneratedAt { get; set; } = string.Empty;

    [JsonPropertyName("totals")]
    public PlaybackMemoryTotalsDto Totals { get; set; } = new();

    [JsonPropertyName("timeBuckets")]
    public List<PlaybackMemoryTimeBucketDto> TimeBuckets { get; set; } = [];

    [JsonPropertyName("lateNightTrack")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public PlaybackMemoryTrackInsightDto? LateNightTrack { get; set; }

    [JsonPropertyName("comebackTrack")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public PlaybackMemoryTrackInsightDto? ComebackTrack { get; set; }

    [JsonPropertyName("forgottenTrack")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public PlaybackMemoryTrackInsightDto? ForgottenTrack { get; set; }

    [JsonPropertyName("likedTrack")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public PlaybackMemoryTrackInsightDto? LikedTrack { get; set; }

    [JsonPropertyName("skippedTrack")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public PlaybackMemoryTrackInsightDto? SkippedTrack { get; set; }

    [JsonPropertyName("transition")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public PlaybackMemoryTransitionDto? Transition { get; set; }

    [JsonPropertyName("recentFlow")]
    public List<PlaybackMemoryTrackInsightDto> RecentFlow { get; set; } = [];

    [JsonPropertyName("coverage")]
    public PlaybackMemoryCoverageDto Coverage { get; set; } = new();
}

public sealed class PlaybackMemoryTotalsDto
{
    [JsonPropertyName("playCount")]
    public int PlayCount { get; set; }

    [JsonPropertyName("completedCount")]
    public int CompletedCount { get; set; }

    [JsonPropertyName("skippedCount")]
    public int SkippedCount { get; set; }

    [JsonPropertyName("playedSeconds")]
    public double PlayedSeconds { get; set; }

    [JsonPropertyName("uniqueTracks")]
    public int UniqueTracks { get; set; }

    [JsonPropertyName("transitionCount")]
    public int TransitionCount { get; set; }
}

public sealed class PlaybackMemoryTrackInsightDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("trackId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? TrackId { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("artist")]
    public string Artist { get; set; } = string.Empty;

    [JsonPropertyName("album")]
    public string Album { get; set; } = string.Empty;

    [JsonPropertyName("coverThumb")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? CoverThumb { get; set; }

    [JsonPropertyName("playCount")]
    public int PlayCount { get; set; }

    [JsonPropertyName("completedCount")]
    public int CompletedCount { get; set; }

    [JsonPropertyName("skippedCount")]
    public int SkippedCount { get; set; }

    [JsonPropertyName("playedSeconds")]
    public double PlayedSeconds { get; set; }

    [JsonPropertyName("durationSeconds")]
    public double DurationSeconds { get; set; }

    [JsonPropertyName("firstPlayedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? FirstPlayedAt { get; set; }

    [JsonPropertyName("lastPlayedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? LastPlayedAt { get; set; }

    [JsonPropertyName("isLiked")]
    public bool IsLiked { get; set; }
}

public sealed class PlaybackMemoryTransitionDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("from")]
    public PlaybackMemoryTrackInsightDto From { get; set; } = new();

    [JsonPropertyName("to")]
    public PlaybackMemoryTrackInsightDto To { get; set; } = new();

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("averageGapSeconds")]
    public double AverageGapSeconds { get; set; }

    [JsonPropertyName("lastPlayedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? LastPlayedAt { get; set; }
}

public sealed class PlaybackMemoryTimeBucketDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("playCount")]
    public int PlayCount { get; set; }

    [JsonPropertyName("completedCount")]
    public int CompletedCount { get; set; }

    [JsonPropertyName("skippedCount")]
    public int SkippedCount { get; set; }

    [JsonPropertyName("playedSeconds")]
    public double PlayedSeconds { get; set; }

    [JsonPropertyName("topTrack")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public PlaybackMemoryTrackInsightDto? TopTrack { get; set; }
}

public sealed class PlaybackMemoryCoverageDto
{
    [JsonPropertyName("rawEventCount")]
    public int RawEventCount { get; set; }

    [JsonPropertyName("likedTrackMatches")]
    public int LikedTrackMatches { get; set; }

    [JsonPropertyName("outputDeviceHistory")]
    public bool OutputDeviceHistory { get; set; }
}
