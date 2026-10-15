using System.Text.Json.Serialization;

namespace Echo.Core.Protocol;

public sealed class CoreQualityIssueQuery
{
    public string Kind { get; set; } = string.Empty;
    public int? Page { get; set; }
    public int? PageSize { get; set; }
    public string? SourceProvider { get; set; }
    public string? Search { get; set; }
    public string? FtsMatch { get; set; }
}

public sealed class CoreQualityIssueItem
{
    public LibraryTrackDto Track { get; set; } = new();
    public List<string> Reasons { get; set; } = [];
    public int CandidateCount { get; set; }
}

public sealed class CoreQualityIssuePage
{
    public List<CoreQualityIssueItem> Items { get; set; } = [];
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int Total { get; set; }
    public bool HasMore { get; set; }
    public string Kind { get; set; } = string.Empty;
}
