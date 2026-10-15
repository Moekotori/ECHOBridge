using System.Text.Json;
using System.Text.Json.Serialization;

namespace Echo.Core.Protocol;

public sealed class LibraryMutation : IStreamedCommand
{
  public string? OperationId { get; set; }
  public long IssuedAt { get; set; }
  public bool LookupOnly { get; set; }
  public MutationStream? Stream { get; set; }
  public string Operation { get; set; } = "";
  public string? ScanJobId { get; set; }
  public string? ScanTimestamp { get; set; }
  public bool QueueArtistRefresh { get; set; }
  public List<ScanWriteItem> Items { get; set; } = [];
  public List<CleanupCandidate> Candidates { get; set; } = [];
  public List<ScanRepairItem> Repairs { get; set; } = [];
  public List<ScanSnapshotItem> Snapshots { get; set; } = [];
  public ScanSeenPaths? Seen { get; set; }
  public ScanMarkMissing? MarkMissing { get; set; }
  public ScanArtistRefresh? ArtistRefresh { get; set; }
  public ScanAlbumCoverRefresh? AlbumCovers { get; set; }
  public AlbumGroupingBatch? AlbumGrouping { get; set; }
  public GroupingRebuild? GroupingRebuild { get; set; }
}

public sealed record AlbumGroupingBatch(List<string> TrackIds, string UpdatedAt, string Strategy = "standard", bool PreserveMembership = false);
public sealed record GroupingRebuild(string UpdatedAt, bool NeedsAlbumRefresh, string AlbumStrategy = "standard",
  string ArtistStrategy = "standard", string RemoteAlbumStrategy = "conservative", string? VisualEvidencePath = null);

/// <summary>Scan-tail repair of an unchanged file: refreshed cover derivative and/or identity observation.</summary>
public sealed class ScanRepairItem
{
  public string? CheckpointKey { get; set; }
  public bool CheckpointSkipped { get; set; }
  public string TrackId { get; set; } = "";
  public string UpdatedAt { get; set; } = "";
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public JsonElement Cover { get; set; }
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public JsonElement Identity { get; set; }
}
public sealed record ScanRepairResult(string Id, bool CoverChanged);
public sealed record ScanSnapshotItem(string FolderId, string Path, double MtimeMs, string EntriesJson, string UpdatedAt);
/// <summary>Paths seen by one scan job, accumulated in <c>core_scan_seen_paths</c> until <c>markMissing</c>.</summary>
public sealed record ScanSeenPaths(string ScanJobId, List<string> Paths, bool CaseInsensitive);
public sealed record ScanMarkMissing(string ScanJobId, string FolderId, List<string> ExcludeDirectories, string UpdatedAt, bool CaseInsensitive);
public sealed record ScanArtistRefresh(List<string> TrackIds, List<string> AlbumIds, string CreatedAt);

public sealed record MutationStream(string Id, long IssuedAt, long Sequence);

public sealed class ScanWriteItem
{
  public string? CheckpointKey { get; set; }
  public JsonElement Track { get; set; }
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public JsonElement Cover { get; set; }
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public JsonElement IdentityMove { get; set; }
  public string SearchTerms { get; set; } = "";
  public string? GraphFingerprint { get; set; }
}
public sealed record CleanupCandidate(string Id, string Path, double SizeBytes, double MtimeMs, double Duration, string Reason);
public sealed record ScanWriteResult(string Id, bool Added);
public sealed record LibraryMutationResult(string State = "committed", List<ScanWriteResult>? Tracks = null, int Removed = 0, List<string>? Deleted = null,
  List<ScanRepairResult>? Repairs = null, ScanAlbumCoverRefreshResult? AlbumCovers = null, List<string>? AlbumIds = null);
public sealed record CleanupPage(List<CleanupRow> Items, long Cursor, long Ceiling);
public sealed record CleanupRow(long RowId, string Id, string Path, double SizeBytes, double MtimeMs, double Duration);
