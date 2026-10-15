using Echo.Core.Protocol;
using Microsoft.Data.Sqlite;

namespace Echo.Core.Mapping;

internal readonly struct TrackListRowMapper
{
    private readonly SqliteDataReader _reader;
    private readonly int _id;
    private readonly int _title;
    private readonly int _artist;
    private readonly int _album;
    private readonly int _trackNo;
    private readonly int _discNo;
    private readonly int _genre;
    private readonly int _listPath;
    private readonly int _hasReplayGain;
    private readonly int _duration;
    private readonly int _codec;
    private readonly int _sampleRate;
    private readonly int _bitDepth;
    private readonly int _bitrate;
    private readonly int _bpm;
    private readonly int _bpmConfidence;
    private readonly int _analysisStatus;
    private readonly int _coverId;
    private readonly int _mqaSource;
    private readonly int _bpmSource;
    private readonly int _osuStoryboardSource;
    private readonly int _vocalPresence;

    private TrackListRowMapper(SqliteDataReader reader)
    {
        _reader = reader;
        _id = reader.GetOrdinal("id");
        _title = reader.GetOrdinal("title");
        _artist = reader.GetOrdinal("artist");
        _album = reader.GetOrdinal("album");
        _trackNo = reader.GetOrdinal("track_no");
        _discNo = reader.GetOrdinal("disc_no");
        _genre = reader.GetOrdinal("genre");
        _listPath = reader.GetOrdinal("list_path");
        _hasReplayGain = reader.GetOrdinal("has_replay_gain");
        _duration = reader.GetOrdinal("duration");
        _codec = reader.GetOrdinal("codec");
        _sampleRate = reader.GetOrdinal("sample_rate");
        _bitDepth = reader.GetOrdinal("bit_depth");
        _bitrate = reader.GetOrdinal("bitrate");
        _bpm = reader.GetOrdinal("bpm");
        _bpmConfidence = reader.GetOrdinal("bpm_confidence");
        _analysisStatus = reader.GetOrdinal("analysis_status");
        _coverId = reader.GetOrdinal("cover_id");
        _mqaSource = reader.GetOrdinal("mqa_source");
        _bpmSource = reader.GetOrdinal("bpm_source");
        _osuStoryboardSource = reader.GetOrdinal("osu_storyboard_source");
        _vocalPresence = RowMapperColumns.Optional(reader, "vocal_presence");
    }

    public static TrackListRowMapper Bind(SqliteDataReader reader) => new(reader);

    public LibraryTrackListRowDto Map()
    {
        var coverId = Text(_coverId);
        var mqaSource = Text(_mqaSource);
        return new LibraryTrackListRowDto
        {
            Id = Text(_id) ?? string.Empty,
            MediaType = "local",
            Title = DisplayText(Text(_title), static () => string.Empty),
            Artist = DisplayText(Text(_artist), static () => LibraryText.UnknownArtist),
            Album = DisplayText(Text(_album), static () => string.Empty),
            TrackNo = Int(_trackNo),
            DiscNo = Int(_discNo),
            Genre = LibraryText.SanitizeNullable(Text(_genre)),
            FileName = LibraryText.FileName(Text(_listPath)),
            HasReplayGain = Int(_hasReplayGain) is > 0,
            VocalPresence = _vocalPresence < 0 ? null : Text(_vocalPresence),
            Duration = Real(_duration) ?? 0,
            Codec = LibraryText.SanitizeNullable(Text(_codec), LibraryText.TechnicalLimit),
            Mqa = mqaSource is "embedded" or "technical",
            HasOsuStoryboard = Text(_osuStoryboardSource) == "osu",
            SampleRate = Int(_sampleRate),
            BitDepth = Int(_bitDepth),
            Bitrate = Int(_bitrate),
            Bpm = Real(_bpm),
            BpmConfidence = Real(_bpmConfidence),
            AnalysisStatus = OmitDefault(MapAnalysisStatus(Text(_analysisStatus)), "none"),
            CoverId = coverId,
            CoverThumb = string.IsNullOrEmpty(coverId) ? null : LibraryText.CoverUrl(coverId, "thumb"),
            FieldSources = Text(_bpmSource) == "audio_analysis"
                ? new Dictionary<string, string>(1, StringComparer.Ordinal) { ["bpm"] = "audio_analysis" }
                : null,
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

    private static string MapAnalysisStatus(string? value) =>
        value is "pending" or "analyzing" or "complete" or "low_confidence" or "error" ? value : "none";
}
