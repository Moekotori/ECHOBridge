using System.Text.Json.Serialization;

namespace Echo.Core.Protocol;

public sealed class ScanFileFingerprint
{
    [JsonPropertyName("path")] public string Path { get; set; } = string.Empty;
    [JsonPropertyName("sizeBytes")] public double SizeBytes { get; set; }
    [JsonPropertyName("mtimeMs")] public double MtimeMs { get; set; }
    [JsonPropertyName("cueRevision")] public string? CueRevision { get; set; }
}

public sealed class ScanChangedFilesParams
{
    [JsonPropertyName("folderId")] public string? FolderId { get; set; }
    [JsonPropertyName("files")] public List<ScanFileFingerprint> Files { get; set; } = [];
    [JsonPropertyName("caseInsensitive")] public bool CaseInsensitive { get; set; }
}

public sealed class ScanChangedFileDto
{
    [JsonPropertyName("index")] public int Index { get; set; }
    [JsonPropertyName("state")] public ScanCacheStateRowDto? State { get; set; }
}

public sealed class ScanChangedFilesResult
{
    [JsonPropertyName("items")] public List<ScanChangedFileDto> Items { get; set; } = [];
}
