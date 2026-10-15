namespace Echo.Core.Protocol;

public sealed class CoverCandidateRequest
{
    public string FilePath { get; set; } = string.Empty;
    public string? EmbeddedPath { get; set; }
    public long? EmbeddedBytes { get; set; }
    public string? EmbeddedMimeType { get; set; }
}

public sealed record CoverCandidateResponse(
    string Status,
    string Source,
    string? Path = null,
    long? ByteLength = null,
    long? ModifiedMs = null,
    string? MimeType = null,
    string? SourceHash = null,
    bool SharedAcrossFolder = false,
    string? Warning = null,
    string? Error = null);
