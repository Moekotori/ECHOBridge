using System.Text.Json.Serialization;

namespace Echo.Core.Protocol;

// Full tracks extend the list fields; list projections allocate only this compact object.
[JsonDerivedType(typeof(LibraryTrackDto))]
public class LibraryTrackListRowDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("mediaType")]
    public string MediaType { get; set; } = "local";

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("artist")]
    public string Artist { get; set; } = string.Empty;

    [JsonPropertyName("album")]
    public string Album { get; set; } = string.Empty;

    [JsonPropertyName("trackNo")]
    public int? TrackNo { get; set; }

    [JsonPropertyName("discNo")]
    public int? DiscNo { get; set; }

    [JsonPropertyName("genre")]
    public string? Genre { get; set; }

    [JsonPropertyName("duration")]
    public double Duration { get; set; }

    [JsonPropertyName("codec")]
    public string? Codec { get; set; }

    [JsonPropertyName("mqa")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Mqa { get; set; }

    [JsonPropertyName("hasOsuStoryboard")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool HasOsuStoryboard { get; set; }

    [JsonPropertyName("sampleRate")]
    public int? SampleRate { get; set; }

    [JsonPropertyName("bitDepth")]
    public int? BitDepth { get; set; }

    [JsonPropertyName("bitrate")]
    public int? Bitrate { get; set; }

    [JsonPropertyName("bpm")]
    public double? Bpm { get; set; }

    [JsonPropertyName("bpmConfidence")]
    public double? BpmConfidence { get; set; }

    [JsonPropertyName("analysisStatus")]
    public string? AnalysisStatus { get; set; }

    [JsonPropertyName("coverId")]
    public string? CoverId { get; set; }

    [JsonPropertyName("coverThumb")]
    public string? CoverThumb { get; set; }

    [JsonPropertyName("playCount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int PlayCount { get; set; }

    [JsonPropertyName("lastPlayedAt")]
    public string? LastPlayedAt { get; set; }

    [JsonPropertyName("fieldSources")]
    public Dictionary<string, string>? FieldSources { get; set; }

    [JsonPropertyName("fileName")]
    public string? FileName { get; set; }

    [JsonPropertyName("hasReplayGain")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool HasReplayGain { get; set; }

    [JsonPropertyName("unavailable")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Unavailable { get; set; }

    [JsonPropertyName("vocalPresence")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? VocalPresence { get; set; }
}
