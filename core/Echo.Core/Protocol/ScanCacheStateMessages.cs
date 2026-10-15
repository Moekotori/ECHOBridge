using System.Text.Json.Serialization;

namespace Echo.Core.Protocol;

/// <summary>Scan cache lookup: either a bounded path batch or a rowid cursor page of one folder.</summary>
public sealed class ScanCacheStatesParams
{
    [JsonPropertyName("folderId")]
    public string? FolderId { get; set; }

    [JsonPropertyName("paths")]
    public List<string>? Paths { get; set; }

    /// <summary>Compare <c>tracks.path</c> with <c>COLLATE NOCASE</c> (Windows case-only renames).</summary>
    [JsonPropertyName("caseInsensitive")]
    public bool CaseInsensitive { get; set; }

    [JsonPropertyName("cursor")]
    public long? Cursor { get; set; }

    [JsonPropertyName("limit")]
    public int? Limit { get; set; }
}

/// <summary>
/// One <c>tracks LEFT JOIN covers</c> row. Property names are the SQL column names so the
/// Electron side can reuse its existing row mapper instead of a second camelCase mapping.
/// </summary>
public sealed class ScanCacheStateRowDto
{
    [JsonPropertyName("rowid")] public long RowId { get; set; }
    [JsonPropertyName("path")] public string Path { get; set; } = string.Empty;
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("size_bytes")] public double? SizeBytes { get; set; }
    [JsonPropertyName("mtime_ms")] public double? MtimeMs { get; set; }
    [JsonPropertyName("duration")] public double? Duration { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("artist")] public string? Artist { get; set; }
    [JsonPropertyName("album")] public string? Album { get; set; }
    [JsonPropertyName("album_artist")] public string? AlbumArtist { get; set; }
    [JsonPropertyName("track_no")] public double? TrackNo { get; set; }
    [JsonPropertyName("disc_no")] public double? DiscNo { get; set; }
    [JsonPropertyName("year")] public double? Year { get; set; }
    [JsonPropertyName("genre")] public string? Genre { get; set; }
    [JsonPropertyName("codec")] public string? Codec { get; set; }
    [JsonPropertyName("sample_rate")] public double? SampleRate { get; set; }
    [JsonPropertyName("bit_depth")] public double? BitDepth { get; set; }
    [JsonPropertyName("bitrate")] public double? Bitrate { get; set; }
    [JsonPropertyName("bpm")] public double? Bpm { get; set; }
    [JsonPropertyName("replay_gain_track_gain_db")] public double? ReplayGainTrackGainDb { get; set; }
    [JsonPropertyName("replay_gain_album_gain_db")] public double? ReplayGainAlbumGainDb { get; set; }
    [JsonPropertyName("replay_gain_track_peak")] public double? ReplayGainTrackPeak { get; set; }
    [JsonPropertyName("replay_gain_album_peak")] public double? ReplayGainAlbumPeak { get; set; }
    [JsonPropertyName("replay_gain_integrated_lufs")] public double? ReplayGainIntegratedLufs { get; set; }
    [JsonPropertyName("cover_id")] public string? CoverId { get; set; }
    [JsonPropertyName("metadata_status")] public string? MetadataStatus { get; set; }
    [JsonPropertyName("embedded_metadata_status")] public string? EmbeddedMetadataStatus { get; set; }
    [JsonPropertyName("embedded_cover_status")] public string? EmbeddedCoverStatus { get; set; }
    [JsonPropertyName("field_sources_json")] public string? FieldSourcesJson { get; set; }
    [JsonPropertyName("file_identity")] public string? FileIdentity { get; set; }
    [JsonPropertyName("file_identity_source")] public string? FileIdentitySource { get; set; }
    [JsonPropertyName("quick_hash")] public string? QuickHash { get; set; }
    [JsonPropertyName("quick_hash_version")] public double? QuickHashVersion { get; set; }
    [JsonPropertyName("identity_status")] public string? IdentityStatus { get; set; }
    [JsonPropertyName("identity_updated_at")] public string? IdentityUpdatedAt { get; set; }
    [JsonPropertyName("identity_error")] public string? IdentityError { get; set; }
    [JsonPropertyName("source_type")] public string? SourceType { get; set; }
    [JsonPropertyName("source_hash")] public string? SourceHash { get; set; }
    [JsonPropertyName("mime_type")] public string? MimeType { get; set; }
    [JsonPropertyName("thumb_path")] public string? ThumbPath { get; set; }
    [JsonPropertyName("album_path")] public string? AlbumPath { get; set; }
    [JsonPropertyName("large_path")] public string? LargePath { get; set; }
    [JsonPropertyName("original_ref")] public string? OriginalRef { get; set; }
    [JsonPropertyName("cache_version")] public double? CacheVersion { get; set; }
}

public sealed class ScanCacheStatesPage
{
    [JsonPropertyName("items")]
    public List<ScanCacheStateRowDto> Items { get; set; } = [];

    /// <summary>Last rowid of this page; pass back as <c>cursor</c>. Null when the request was a path batch.</summary>
    [JsonPropertyName("cursor")]
    public long? Cursor { get; set; }

    [JsonPropertyName("hasMore")]
    public bool HasMore { get; set; }
}
