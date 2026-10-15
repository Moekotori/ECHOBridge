using System.Text.Json.Serialization;

namespace Echo.Core.Protocol;

public sealed class CoreFolderChildrenQuery
{
    [JsonPropertyName("folderId")]
    public string FolderId { get; set; } = string.Empty;

    [JsonPropertyName("parentPath")]
    public string? ParentPath { get; set; }
}

public sealed class LibraryFolderNodeDto
{
    public string FolderId { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string ParentPath { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Depth { get; set; }
    public int TrackCount { get; set; }
    public int DirectTrackCount { get; set; }
    public int ChildFolderCount { get; set; }
    public double TotalDuration { get; set; }
    public long TotalSizeBytes { get; set; }
    public List<string> CoverThumbs { get; set; } = [];
}
