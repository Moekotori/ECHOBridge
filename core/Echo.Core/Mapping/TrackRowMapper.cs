using Echo.Core.Protocol;
using Microsoft.Data.Sqlite;

namespace Echo.Core.Mapping;

internal readonly struct TrackRowMapper
{
    private readonly SqliteDataReader _reader;
    private readonly int _id;
    private readonly int _path;
    private readonly int _title;
    private readonly int _artist;
    private readonly int _album;
    private readonly int _albumArtist;
    private readonly int _trackNo;
    private readonly int _discNo;
    private readonly int _year;
    private readonly int _genre;
    private readonly int _duration;
    private readonly int _codec;
    private readonly int _sampleRate;
    private readonly int _bitDepth;
    private readonly int _bitrate;
    private readonly int _bpm;
    private readonly int _bpmConfidence;
    private readonly int _beatOffsetMs;
    private readonly int _analysisStatus;
    private readonly int _analysisUpdatedAt;
    private readonly int _replayGainTrackGainDb;
    private readonly int _replayGainAlbumGainDb;
    private readonly int _replayGainTrackPeak;
    private readonly int _replayGainAlbumPeak;
    private readonly int _replayGainIntegratedLufs;
    private readonly int _replayGainSource;
    private readonly int _replayGainStatus;
    private readonly int _replayGainUpdatedAt;
    private readonly int _coverId;
    private readonly int _createdAt;
    private readonly int _recommendationPreference;
    private readonly int _playCount;
    private readonly int _skipCount;
    private readonly int _lastPlayedAt;
    private readonly int _lastSkippedAt;
    private readonly int _metadataStatus;
    private readonly int _embeddedMetadataStatus;
    private readonly int _embeddedCoverStatus;
    private readonly int _networkMetadataStatus;
    private readonly int _fieldSourcesJson;
    private readonly int _vocalPresence;

    private TrackRowMapper(SqliteDataReader reader)
    {
        _reader = reader;
        _id = reader.GetOrdinal("id");
        _path = reader.GetOrdinal("path");
        _title = reader.GetOrdinal("title");
        _artist = reader.GetOrdinal("artist");
        _album = reader.GetOrdinal("album");
        _albumArtist = reader.GetOrdinal("album_artist");
        _trackNo = reader.GetOrdinal("track_no");
        _discNo = reader.GetOrdinal("disc_no");
        _year = reader.GetOrdinal("year");
        _genre = reader.GetOrdinal("genre");
        _duration = reader.GetOrdinal("duration");
        _codec = reader.GetOrdinal("codec");
        _sampleRate = reader.GetOrdinal("sample_rate");
        _bitDepth = reader.GetOrdinal("bit_depth");
        _bitrate = reader.GetOrdinal("bitrate");
        _bpm = reader.GetOrdinal("bpm");
        _bpmConfidence = reader.GetOrdinal("bpm_confidence");
        _beatOffsetMs = reader.GetOrdinal("beat_offset_ms");
        _analysisStatus = reader.GetOrdinal("analysis_status");
        _analysisUpdatedAt = reader.GetOrdinal("analysis_updated_at");
        _replayGainTrackGainDb = reader.GetOrdinal("replay_gain_track_gain_db");
        _replayGainAlbumGainDb = reader.GetOrdinal("replay_gain_album_gain_db");
        _replayGainTrackPeak = reader.GetOrdinal("replay_gain_track_peak");
        _replayGainAlbumPeak = reader.GetOrdinal("replay_gain_album_peak");
        _replayGainIntegratedLufs = reader.GetOrdinal("replay_gain_integrated_lufs");
        _replayGainSource = reader.GetOrdinal("replay_gain_source");
        _replayGainStatus = reader.GetOrdinal("replay_gain_status");
        _replayGainUpdatedAt = reader.GetOrdinal("replay_gain_updated_at");
        _coverId = reader.GetOrdinal("cover_id");
        _createdAt = reader.GetOrdinal("created_at");
        _recommendationPreference = reader.GetOrdinal("recommendation_preference");
        _playCount = reader.GetOrdinal("play_count");
        _skipCount = reader.GetOrdinal("skip_count");
        _lastPlayedAt = reader.GetOrdinal("last_played_at");
        _lastSkippedAt = reader.GetOrdinal("last_skipped_at");
        _metadataStatus = reader.GetOrdinal("metadata_status");
        _embeddedMetadataStatus = reader.GetOrdinal("embedded_metadata_status");
        _embeddedCoverStatus = reader.GetOrdinal("embedded_cover_status");
        _networkMetadataStatus = reader.GetOrdinal("network_metadata_status");
        _fieldSourcesJson = reader.GetOrdinal("field_sources_json");
        _vocalPresence = RowMapperColumns.Optional(reader, "vocal_presence");
    }

    public static TrackRowMapper Bind(SqliteDataReader reader) => new(reader);

    public LibraryTrackDto Map()
    {
        var path = Text(_path) ?? string.Empty;
        var rawTitle = Text(_title);
        var rawArtist = Text(_artist);
        (string Title, string? Artist)? filename = null;
        (string Title, string? Artist) Filename() => filename ??= LibraryText.FilenameFallback(path);
        var title = DisplayText(rawTitle, () => Filename().Title);
        var artist = DisplayText(rawArtist, () => Filename().Artist ?? LibraryText.UnknownArtist);
        var album = DisplayText(Text(_album), static () => string.Empty);
        var albumArtist = DisplayText(Text(_albumArtist), () => artist);
        var fieldSources = LibraryText.ParseFieldSources(Text(_fieldSourcesJson));
        var coverId = LibraryText.TextOrNull(Text(_coverId));
        var playCount = Int(_playCount);
        var skipCount = Int(_skipCount);
        return new LibraryTrackDto
        {
            Id = Text(_id) ?? string.Empty,
            MediaType = "local",
            Path = path,
            Title = title,
            Artist = artist,
            Album = album,
            AlbumArtist = albumArtist,
            TrackNo = Int(_trackNo),
            DiscNo = Int(_discNo),
            Year = Int(_year),
            Genre = LibraryText.SanitizeNullable(Text(_genre)),
            Duration = Real(_duration) ?? 0,
            Codec = LibraryText.SanitizeNullable(Text(_codec), LibraryText.TechnicalLimit),
            Mqa = fieldSources.TryGetValue("mqa", out var mqa) && mqa is "embedded" or "technical",
            HasOsuStoryboard = fieldSources.TryGetValue("osuStoryboard", out var storyboard) && storyboard == "osu",
            SampleRate = Int(_sampleRate),
            BitDepth = Int(_bitDepth),
            Bitrate = Int(_bitrate),
            Bpm = Real(_bpm),
            BpmConfidence = Real(_bpmConfidence),
            BeatOffsetMs = Real(_beatOffsetMs),
            AnalysisStatus = OmitDefault(MapAnalysisStatus(Text(_analysisStatus)), "none"),
            AnalysisUpdatedAt = Text(_analysisUpdatedAt),
            ReplayGainTrackGainDb = Real(_replayGainTrackGainDb),
            ReplayGainAlbumGainDb = Real(_replayGainAlbumGainDb),
            ReplayGainTrackPeak = Real(_replayGainTrackPeak),
            ReplayGainAlbumPeak = Real(_replayGainAlbumPeak),
            ReplayGainIntegratedLufs = Real(_replayGainIntegratedLufs),
            ReplayGainSource = OmitDefault(MapReplayGainSource(Text(_replayGainSource)), "none"),
            ReplayGainStatus = OmitDefault(MapReplayGainStatus(Text(_replayGainStatus)), "none"),
            ReplayGainUpdatedAt = Text(_replayGainUpdatedAt),
            CoverId = coverId,
            CoverThumb = string.IsNullOrEmpty(coverId) ? null : LibraryText.CoverUrl(coverId, "thumb"),
            CreatedAt = Text(_createdAt),
            RecommendationPreference = Text(_recommendationPreference) == "never_recommend"
                ? "never_recommend"
                : null,
            PlayCount = playCount is > 0 ? playCount.Value : 0,
            SkipCount = skipCount is > 0 ? skipCount.Value : 0,
            LastPlayedAt = Text(_lastPlayedAt),
            LastSkippedAt = Text(_lastSkippedAt),
            MetadataStatus = OmitDefault(Text(_metadataStatus) ?? "ok", "ok"),
            EmbeddedMetadataStatus = OmitDefault(MapEmbeddedStatus(Text(_embeddedMetadataStatus)), "pending"),
            EmbeddedCoverStatus = OmitDefault(MapEmbeddedStatus(Text(_embeddedCoverStatus)), "pending"),
            NetworkMetadataStatus = OmitDefault(MapNetworkStatus(Text(_networkMetadataStatus)), "none"),
            FieldSources = fieldSources.Count == 0 ? null : fieldSources,
            VocalPresence = _vocalPresence < 0 ? null : Text(_vocalPresence),
        };
    }

    private string? Text(int ordinal) => _reader.IsDBNull(ordinal) ? null : _reader.GetString(ordinal);

    private int? Int(int ordinal) => _reader.IsDBNull(ordinal) ? null : _reader.GetInt32(ordinal);

    private double? Real(int ordinal) => _reader.IsDBNull(ordinal) ? null : _reader.GetDouble(ordinal);

    private static string DisplayText(string? raw, Func<string> fallback)
    {
        if (LibraryText.IsPlainDisplayText(raw))
        {
            return raw!;
        }

        return LibraryText.Sanitize(raw, fallback());
    }

    private static string? OmitDefault(string? value, string defaultValue) =>
        value is null || string.Equals(value, defaultValue, StringComparison.Ordinal) ? null : value;

    private static string MapEmbeddedStatus(string? value) =>
        value is "pending" or "reading" or "present" or "missing" or "error" ? value : "pending";

    private static string MapNetworkStatus(string? value) =>
        value is "none" or "pending" or "candidate_found" or "applied_missing_only" or "rejected" or "error"
            ? value
            : "none";

    private static string MapAnalysisStatus(string? value) =>
        value is "pending" or "analyzing" or "complete" or "low_confidence" or "error" ? value : "none";

    private static string MapReplayGainSource(string? value) =>
        value is "tag" or "analysis" ? value : "none";

    private static string MapReplayGainStatus(string? value) =>
        value is "tagged" or "analyzing" or "complete" or "missing" or "error" ? value : "none";
}
