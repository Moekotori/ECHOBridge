namespace Echo.Core.Protocol;

/// <summary>Per-play write events: history start / finish and the bare play counter.</summary>
public sealed class PlaybackCommand : IStreamedCommand
{
    public string? OperationId { get; set; }
    public long IssuedAt { get; set; }
    public bool LookupOnly { get; set; }
    public MutationStream? Stream { get; set; }
    /// <summary>start | finish | playCount</summary>
    public string Operation { get; set; } = "";
    public PlaybackStart? Start { get; set; }
    public PlaybackFinish? Finish { get; set; }
    public string? TrackId { get; set; }
    public string? Timestamp { get; set; }
}

public sealed class PlaybackStart
{
    public string Id { get; set; } = "";
    public string? TrackId { get; set; }
    public string TrackPath { get; set; } = "";
    public string MediaType { get; set; } = "local";
    public string? Provider { get; set; }
    public string? ProviderTrackId { get; set; }
    public string? StableKey { get; set; }
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public string AlbumArtist { get; set; } = "";
    public string? CoverId { get; set; }
    public string? CoverSnapshot { get; set; }
    public double DurationSeconds { get; set; }
    public double? DurationSnapshot { get; set; }
    public string? SourceType { get; set; }
    public string? SourceLabel { get; set; }
    public string? QueueId { get; set; }
    public string StartedAt { get; set; } = "";
}

public sealed class PlaybackFinish
{
    public string HistoryId { get; set; } = "";
    public double PlayedSeconds { get; set; }
    public double? DurationSeconds { get; set; }
    public bool? Completed { get; set; }
    public bool Skipped { get; set; }
    public string EndedAt { get; set; } = "";
}

public sealed record PlaybackCommandResult(string State = "committed", string? HistoryId = null, bool Found = false, bool Completed = false);
