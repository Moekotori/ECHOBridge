using System.Text.Json.Serialization;

namespace Echo.Core.Protocol;

/// <summary>Plain rowid cursor for bounded whole-table walks that return small rows.</summary>
public sealed class RowIdCursorParams
{
    [JsonPropertyName("cursor")]
    public long? Cursor { get; set; }

    [JsonPropertyName("limit")]
    public int? Limit { get; set; }
}

public sealed record TrackRefDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("path")] string Path);

public sealed class TrackRefPage
{
    [JsonPropertyName("items")]
    public List<TrackRefDto> Items { get; set; } = [];

    [JsonPropertyName("cursor")]
    public long Cursor { get; set; }

    [JsonPropertyName("hasMore")]
    public bool HasMore { get; set; }
}
