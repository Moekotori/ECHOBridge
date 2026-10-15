namespace Echo.Core.Protocol;

public sealed class AsciiSearchFields
{
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Album { get; set; } = string.Empty;
    public string AlbumArtist { get; set; } = string.Empty;
    public string? Genre { get; set; }
    public string? Path { get; set; }
    public string? RemotePath { get; set; }
}

public sealed class AsciiSearchTermsRequest
{
    public List<AsciiSearchFields> Items { get; set; } = [];
}

public sealed record AsciiSearchTermsResult(List<string> Terms);
