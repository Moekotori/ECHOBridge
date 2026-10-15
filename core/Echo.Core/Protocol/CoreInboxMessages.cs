namespace Echo.Core.Protocol;

public sealed class CoreInboxQuery
{
    public string? BatchId { get; set; }
    public string? Scope { get; set; }
    public string? Filter { get; set; }
    public string? Status { get; set; }
    public string? FolderId { get; set; }
    public string? Album { get; set; }
    public string? Artist { get; set; }
    public int? Page { get; set; }
    public int? PageSize { get; set; }
    public string? Search { get; set; }
    public string? FtsMatch { get; set; }
}

public sealed class CoreInboxBatch
{
    public string Id { get; set; } = string.Empty;
    public string ScanJobId { get; set; } = string.Empty;
    public string FolderId { get; set; } = string.Empty;
    public string FolderName { get; set; } = string.Empty;
    public string FolderPath { get; set; } = string.Empty;
    public int AddedCount { get; set; }
    public int MissingCoverCount { get; set; }
    public int MetadataIssueCount { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public string FinishedAt { get; set; } = string.Empty;
}

public sealed class CoreInboxFacet
{
    public string Value { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int Count { get; set; }
}

public sealed class CoreInboxStory
{
    public int TrackCount { get; set; }
    public int AlbumCount { get; set; }
    public int ArtistCount { get; set; }
    public int FolderCount { get; set; }
    public int MissingCoverCount { get; set; }
    public int MetadataIssueCount { get; set; }
    public int UnknownArtistCount { get; set; }
    public int UnknownAlbumCount { get; set; }
    public int SuspiciousCount { get; set; }
    public int PendingCount { get; set; }
    public int ProcessedCount { get; set; }
    public int IgnoredCount { get; set; }
    public int CoverCompleteness { get; set; }
    public int MetadataCompleteness { get; set; }
    public double TotalDuration { get; set; }
    public List<CoreInboxFacet> TopFolders { get; set; } = [];
    public List<CoreInboxFacet> TopArtists { get; set; } = [];
}

public sealed class CoreInboxAlbum
{
    public string Album { get; set; } = string.Empty;
    public string AlbumArtist { get; set; } = string.Empty;
    public string? CoverId { get; set; }
    public string? CoverThumb { get; set; }
    public int TrackCount { get; set; }
    public int MissingCoverCount { get; set; }
    public int MetadataIssueCount { get; set; }
    public double Duration { get; set; }
}

public sealed class CoreInboxItem
{
    public string BatchId { get; set; } = string.Empty;
    public string AddedAt { get; set; } = string.Empty;
    public LibraryTrackDto Track { get; set; } = new();
    public List<string> Reasons { get; set; } = [];
    public string InboxStatus { get; set; } = "pending";
}

public sealed class CoreInboxFacets
{
    public List<CoreInboxFacet> Folders { get; set; } = [];
    public List<CoreInboxFacet> Albums { get; set; } = [];
    public List<CoreInboxFacet> Artists { get; set; } = [];
}

public sealed class CoreInboxPage
{
    public List<CoreInboxItem> Items { get; set; } = [];
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int Total { get; set; }
    public bool HasMore { get; set; }
    public List<CoreInboxBatch> Batches { get; set; } = [];
    public CoreInboxBatch? SelectedBatch { get; set; }
    public string Scope { get; set; } = "latest";
    public string Filter { get; set; } = "all";
    public string Status { get; set; } = "all";
    public CoreInboxStory Story { get; set; } = new();
    public List<CoreInboxAlbum> Albums { get; set; } = [];
    public CoreInboxFacets Facets { get; set; } = new();
}
