using Echo.Core.Protocol;
using Echo.Core.Query;
using Microsoft.Data.Sqlite;

namespace Echo.Core.Mapping;

internal readonly struct GenreRowMapper
{
    private readonly SqliteDataReader _reader;
    private readonly int _genreKey;
    private readonly int _displayName;
    private readonly int _trackCount;
    private readonly int _albumCount;
    private readonly int _coverId;
    private readonly int _unclassified;

    private GenreRowMapper(SqliteDataReader reader)
    {
        _reader = reader;
        _genreKey = reader.GetOrdinal("genre_key");
        _displayName = reader.GetOrdinal("display_name");
        _trackCount = reader.GetOrdinal("track_count");
        _albumCount = reader.GetOrdinal("album_count");
        _coverId = reader.GetOrdinal("cover_id");
        _unclassified = reader.GetOrdinal("unclassified");
    }

    public static GenreRowMapper Bind(SqliteDataReader reader) => new(reader);

    public LibraryGenreDto Map()
    {
        var genreKey = Text(_genreKey) ?? GenreKey.Unclassified;
        var unclassified = (!_reader.IsDBNull(_unclassified) && _reader.GetInt32(_unclassified) != 0)
            || GenreKey.IsUnclassified(genreKey);
        var coverId = Text(_coverId);
        return new LibraryGenreDto
        {
            GenreKey = genreKey,
            Name = unclassified
                ? string.Empty
                : DisplayText(Text(_displayName), static () => string.Empty),
            Unclassified = unclassified,
            MediaType = "local",
            TrackCount = Math.Max(0, Int(_trackCount) ?? 0),
            AlbumCount = Math.Max(0, Int(_albumCount) ?? 0),
            CoverId = coverId,
            CoverThumb = string.IsNullOrEmpty(coverId) ? null : LibraryText.CoverUrl(coverId, "thumb"),
        };
    }

    private string? Text(int ordinal) => _reader.IsDBNull(ordinal) ? null : _reader.GetString(ordinal);

    private int? Int(int ordinal) => _reader.IsDBNull(ordinal) ? null : _reader.GetInt32(ordinal);

    private static string DisplayText(string? raw, Func<string> fallback)
    {
        if (LibraryText.IsPlainDisplayText(raw))
        {
            return raw!;
        }

        return LibraryText.Sanitize(raw, fallback());
    }
}
