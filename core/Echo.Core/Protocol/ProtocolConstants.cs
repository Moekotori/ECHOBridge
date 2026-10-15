namespace Echo.Core.Protocol;

/// <summary>
/// Keep these values in lockstep with <c>src/main/core/echoCoreProtocol.ts</c>.
/// </summary>
public static class ProtocolConstants
{
    public const int ProtocolVersion = 1;
    public const int HeaderSize = 4;
    public const int MaxFrameBytes = 16 * 1024 * 1024;
    public const int MaxQueuedRequests = 16;
    public const int MaxQueuedBytes = 32 * 1024 * 1024;
    public const int MaxPendingResponses = 2;
    public const int MaxPendingControlResponses = 4;
    public const int DefaultDeadlineMs = 30_000;
    public const int MaxPageSize = 500;
    public const int DefaultPageSize = 100;
    public const int MaxFolderIds = 250;
    public const int MaxExcludeTrackIds = 500;
    public const int MaxTrackIds = 500;
    public const int SqliteCacheSizePages = -8192;
    public const int SqliteMmapBytes = 16 * 1024 * 1024;
    public const int StatementCacheLimit = 64;
    /// <summary>Same as the Electron main-process writer (`busy_timeout = 5000`).</summary>
    public const int WriterBusyTimeoutMs = 5000;
    public const int MaxScanCacheStatePaths = 500;
    public const int MaxScanCacheStatePathChars = 32768;
    public const int MaxScanCacheStateBatchChars = 1024 * 1024;
    public const string SyncedRevisionFlagKey = "local_track_sort_keys.synced_revision";
    public const string TrackPresentCountFlagKey = "local_track_sort_keys.present_count";
    public const string AlbumSyncedRevisionFlagKey = "local_album_sort_keys.synced_revision";
    public const string ArtistSyncedRevisionFlagKey = "local_artist_sort_keys.synced_revision";
    public const string GenrePageSyncedRevisionFlagKey = "local_genre_pages.synced_revision";
    public const string SortKeyFormat = "libraryTextSortKey/v1";
    public const string GenrePageFormat = "libraryGenrePage/v1";
    public const string LikeEntitySearchVersion = "like-entity/v1";
    public const string EchoQueryTotalColumn = "echo_query_total";
}
