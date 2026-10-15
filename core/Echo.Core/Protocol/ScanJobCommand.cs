namespace Echo.Core.Protocol;

public sealed class ScanJobCommand : IStreamedCommand
{
    public string? OperationId { get; set; }
    public long IssuedAt { get; set; }
    public bool LookupOnly { get; set; }
    public MutationStream? Stream { get; set; }
    public string Operation { get; set; } = "";
    public string JobId { get; set; } = "";
    public string? FolderId { get; set; }
    public bool FinishCancellation { get; set; }
    public ScanJobPatch? Patch { get; set; }
    public ScanCheckpoint? Checkpoint { get; set; }
}

public sealed class ScanJobPatch
{
    public string? Status { get; set; }
    public string? Phase { get; set; }
    public long? TotalFiles { get; set; }
    public long? ProcessedFiles { get; set; }
    public long? SkippedFiles { get; set; }
    public long? AddedTracks { get; set; }
    public long? UpdatedTracks { get; set; }
    public long? RemovedTracks { get; set; }
    public long? CoverCount { get; set; }
    public long? ErrorCount { get; set; }
    public List<string>? Errors { get; set; }
    public bool? CancelRequested { get; set; }
    public string? StartedAt { get; set; }
    public string? FinishedAt { get; set; }
}

public sealed record ScanJobState(string Id, string FolderId, string Status, string Phase,
    long TotalFiles, long ProcessedFiles, long SkippedFiles, long AddedTracks, long UpdatedTracks,
    long RemovedTracks, long CoverCount, long ErrorCount, List<string> Errors,
    string? StartedAt, string? FinishedAt, bool CancelRequested);

public sealed record ScanJobCommandResult(string State, ScanJobState? Job = null, int Removed = 0, ScanCheckpointResult? Checkpoint = null);
