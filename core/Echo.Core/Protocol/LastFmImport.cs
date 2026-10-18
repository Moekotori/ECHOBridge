namespace Echo.Core.Protocol;

public sealed class LastFmImportTrack
{
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public bool Loved { get; set; }
    public int? PlayCount { get; set; }
}

public sealed record LastFmImportResult(int Matched = 0, int Unmatched = 0, int Ambiguous = 0,
    int LovedAdded = 0, int PlayCountsUpdated = 0);
