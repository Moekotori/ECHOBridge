using System.Text.Json.Serialization;

namespace Echo.Core.Protocol;

public sealed class PlaybackHistoryDaySummaryDto
{
    [JsonPropertyName("date")]
    public string Date { get; set; } = string.Empty;

    [JsonPropertyName("playCount")]
    public long PlayCount { get; set; }

    [JsonPropertyName("uniqueTracks")]
    public long UniqueTracks { get; set; }

    [JsonPropertyName("playedSeconds")]
    public double PlayedSeconds { get; set; }

    [JsonPropertyName("pendingCount")]
    public long PendingCount { get; set; }
}
