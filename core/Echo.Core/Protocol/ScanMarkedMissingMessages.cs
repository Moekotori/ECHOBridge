using System.Text.Json.Serialization;

namespace Echo.Core.Protocol;

/// <summary>Cursor over the ids one scan just marked missing (matched by the scan timestamp).</summary>
public sealed class ScanMarkedMissingParams
{
    [JsonPropertyName("folderId")]
    public string? FolderId { get; set; }

    [JsonPropertyName("updatedAt")]
    public string? UpdatedAt { get; set; }

    [JsonPropertyName("cursor")]
    public long? Cursor { get; set; }

    [JsonPropertyName("limit")]
    public int? Limit { get; set; }
}

public sealed class ScanMarkedMissingPage
{
    [JsonPropertyName("ids")]
    public List<string> Ids { get; set; } = [];

    [JsonPropertyName("cursor")]
    public long Cursor { get; set; }

    [JsonPropertyName("hasMore")]
    public bool HasMore { get; set; }
}
