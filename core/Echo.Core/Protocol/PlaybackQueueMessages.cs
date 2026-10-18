using System.Text.Json;

namespace Echo.Core.Protocol;

public sealed class PlaybackQueueRequest
{
    // Supplied only by the application-level Main owner, never by renderer input.
    public string StorePath { get; set; } = "";
    public string? ApplicationToken { get; set; }
    public PlaybackQueueRead? Read { get; set; }
    public PlaybackQueueCommand? Command { get; set; }
}
public sealed class PlaybackQueueRead
{
    public string Operation { get; set; } = "summary";
    public string? SessionId { get; set; }
    public long? Revision { get; set; }
    public int Offset { get; set; }
    public int Limit { get; set; } = 128;
    public string? Search { get; set; }
    public bool Display { get; set; }
    public bool SelectionOnly { get; set; }
    public string? QueueId { get; set; }
    public string? FromQueueId { get; set; }
    public List<string>? ExcludeQueueIds { get; set; }
    public string? CommandId { get; set; }
    public string? TrackId { get; set; }
    public string? FilePath { get; set; }
    public long? Cursor { get; set; }
    public int Direction { get; set; } = 1;
    public bool Shuffle { get; set; }
    public int AvoidRecentCount { get; set; } = 50;
    public string? SavedId { get; set; }
    public List<string>? LiveStagingIds { get; set; }
}
public sealed class PlaybackQueueCommand
{
    public string CommandId { get; set; } = "";
    public string SessionId { get; set; } = "";
    public long ExpectedRevision { get; set; }
    public string Operation { get; set; } = "";
    public List<JsonElement>? Items { get; set; }
    public List<string>? QueueIds { get; set; }
    public string? BeforeQueueId { get; set; }
    public string? CurrentQueueId { get; set; }
    public string? CurrentTrackId { get; set; }
    public JsonElement? Resume { get; set; }
    public JsonElement? Mode { get; set; }
    public string? UndoToken { get; set; }
    public string? StagingId { get; set; }
    public string? Name { get; set; }
    public string? TrackId { get; set; }
    public JsonElement? Patch { get; set; }
    public JsonElement? Flags { get; set; }
    public JsonElement? Temporary { get; set; }
    public bool RecordHistory { get; set; } = true;
    public bool Selected { get; set; }
}
