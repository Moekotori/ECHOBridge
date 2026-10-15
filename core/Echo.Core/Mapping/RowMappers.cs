using Echo.Core.Protocol;
using Microsoft.Data.Sqlite;

namespace Echo.Core.Mapping;

public static class RowMappers
{
    public static LibraryTrackDto MapTrack(SqliteDataReader reader) =>
        TrackRowMapper.Bind(reader).Map();

    public static LibraryAlbumDto MapAlbum(SqliteDataReader reader) =>
        AlbumRowMapper.Bind(reader).Map();

    public static LibraryArtistDto MapArtist(SqliteDataReader reader) =>
        ArtistRowMapper.Bind(reader).Map();

    public static LibraryGenreDto MapGenre(SqliteDataReader reader) =>
        GenreRowMapper.Bind(reader).Map();

    public static object? Read(SqliteDataReader reader, string name)
    {
        for (var index = 0; index < reader.FieldCount; index += 1)
        {
            if (!string.Equals(reader.GetName(index), name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return reader.IsDBNull(index) ? null : reader.GetValue(index);
        }

        return null;
    }

    public static long? ReadTotal(SqliteDataReader reader)
    {
        var value = Read(reader, Protocol.ProtocolConstants.EchoQueryTotalColumn);
        return value is null ? null : Convert.ToInt64(value);
    }
}
