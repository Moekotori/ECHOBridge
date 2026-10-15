using Echo.Core.Protocol;
using Microsoft.Data.Sqlite;

namespace Echo.Core.Mapping;

internal readonly struct AlbumRowMapper
{
    private readonly SqliteDataReader _reader;
    private readonly int _id;
    private readonly int _albumKey;
    private readonly int _title;
    private readonly int _albumArtist;
    private readonly int _year;
    private readonly int _trackCount;
    private readonly int _duration;
    private readonly int _coverId;
    private readonly int _coverSource;

    private AlbumRowMapper(SqliteDataReader reader)
    {
        _reader = reader;
        _id = reader.GetOrdinal("id");
        _albumKey = reader.GetOrdinal("album_key");
        _title = reader.GetOrdinal("title");
        _albumArtist = reader.GetOrdinal("album_artist");
        _year = reader.GetOrdinal("year");
        _trackCount = reader.GetOrdinal("track_count");
        _duration = reader.GetOrdinal("duration");
        _coverId = reader.GetOrdinal("cover_id");
        _coverSource = reader.GetOrdinal("cover_source");
    }

    public static AlbumRowMapper Bind(SqliteDataReader reader) => new(reader);

    public LibraryAlbumDto Map()
    {
        var coverId = Text(_coverId);
        var coverSource = Text(_coverSource);
        return new LibraryAlbumDto
        {
            Id = Text(_id) ?? string.Empty,
            MediaType = "local",
            AlbumKey = Text(_albumKey) ?? string.Empty,
            Title = DisplayText(Text(_title), static () => string.Empty),
            AlbumArtist = DisplayText(Text(_albumArtist), static () => LibraryText.UnknownArtist),
            Year = Int(_year),
            TrackCount = Int(_trackCount) ?? 0,
            Duration = Real(_duration) ?? 0,
            CoverId = coverId,
            CoverThumb = string.IsNullOrEmpty(coverId) ? null : LibraryText.CoverUrl(coverId, "album"),
            CoverSource = coverSource is "manual" or "embedded" or "folder" or "network" or "default"
                ? coverSource
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
}
