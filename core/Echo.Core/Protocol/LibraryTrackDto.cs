using System.Text.Json.Serialization;

namespace Echo.Core.Protocol;

public sealed class LibraryTrackDto : LibraryTrackListRowDto
{
    [JsonPropertyName("path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("sourceId")]
    public string? SourceId { get; set; }

    [JsonPropertyName("sourceDisplayName")]
    public string? SourceDisplayName { get; set; }

    [JsonPropertyName("provider")]
    public string? Provider { get; set; }

    [JsonPropertyName("providerTrackId")]
    public string? ProviderTrackId { get; set; }

    [JsonPropertyName("remotePath")]
    public string? RemotePath { get; set; }

    [JsonPropertyName("stableKey")]
    public string? StableKey { get; set; }

    [JsonPropertyName("albumArtist")]
    public string AlbumArtist { get; set; } = string.Empty;

    [JsonPropertyName("year")]
    public int? Year { get; set; }

    [JsonPropertyName("beatOffsetMs")]
    public double? BeatOffsetMs { get; set; }

    [JsonPropertyName("analysisUpdatedAt")]
    public string? AnalysisUpdatedAt { get; set; }

    [JsonPropertyName("replayGainTrackGainDb")]
    public double? ReplayGainTrackGainDb { get; set; }

    [JsonPropertyName("replayGainAlbumGainDb")]
    public double? ReplayGainAlbumGainDb { get; set; }

    [JsonPropertyName("replayGainTrackPeak")]
    public double? ReplayGainTrackPeak { get; set; }

    [JsonPropertyName("replayGainAlbumPeak")]
    public double? ReplayGainAlbumPeak { get; set; }

    [JsonPropertyName("replayGainIntegratedLufs")]
    public double? ReplayGainIntegratedLufs { get; set; }

    [JsonPropertyName("replayGainSource")]
    public string? ReplayGainSource { get; set; }

    [JsonPropertyName("replayGainStatus")]
    public string? ReplayGainStatus { get; set; }

    [JsonPropertyName("replayGainUpdatedAt")]
    public string? ReplayGainUpdatedAt { get; set; }

    [JsonPropertyName("createdAt")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("recommendationPreference")]
    public string? RecommendationPreference { get; set; }

    [JsonPropertyName("skipCount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int SkipCount { get; set; }

    [JsonPropertyName("lastSkippedAt")]
    public string? LastSkippedAt { get; set; }

    [JsonPropertyName("metadataStatus")]
    public string? MetadataStatus { get; set; }

    [JsonPropertyName("embeddedMetadataStatus")]
    public string? EmbeddedMetadataStatus { get; set; }

    [JsonPropertyName("embeddedCoverStatus")]
    public string? EmbeddedCoverStatus { get; set; }

    [JsonPropertyName("networkMetadataStatus")]
    public string? NetworkMetadataStatus { get; set; }
}
