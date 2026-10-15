using System.Text.Json;
using System.Text.Json.Serialization;

namespace Echo.Core.Protocol;

public sealed class CoreAlbumFocusCriterion
{
    [JsonPropertyName("field")]
    public string Field { get; set; } = string.Empty;
    [JsonPropertyName("value")]
    public JsonElement Value { get; set; }
    [JsonPropertyName("exclude")]
    public bool Exclude { get; set; }
    [JsonPropertyName("label")]
    public string? Label { get; set; }
}
