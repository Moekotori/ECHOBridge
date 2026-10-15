using System.Text.Json;

namespace Echo.Core.Protocol;

public sealed class ScanCheckpoint
{
    public string Action { get; set; } = "";
    public string? Input { get; set; }
    public string? Context { get; set; }
    public List<JsonElement> Files { get; set; } = [];
    public List<string> Paths { get; set; } = [];
    public string Stage { get; set; } = "";
    public long Cursor { get; set; }
    public int Limit { get; set; } = 16;
    public string? Owner { get; set; }
}

public sealed record ScanCheckpointFile(string Facts, string Identity, string TrackId, string Stage, long Ordinal = 0);
public sealed record ScanCheckpointTask(string JobId, string Input, string ScanTimestamp, bool Sealed, string Context);
public sealed record ScanCheckpointResult(List<ScanCheckpointTask>? Tasks = null,
    List<ScanCheckpointFile>? Files = null, long Remaining = 0, long Cursor = 0, bool Exhausted = false);
