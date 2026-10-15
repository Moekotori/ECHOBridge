namespace Echo.Core.Protocol;

public sealed record ScanAlbumCoverRefresh(List<string> TrackIds, string UpdatedAt);
public sealed record ScanAlbumCoverRefreshResult(bool RequiresGrouping, List<string> AlbumIds);
