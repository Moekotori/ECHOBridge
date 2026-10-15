using Echo.Core.Database;
using Echo.Core.Mapping;
using Echo.Core.Protocol;
using Echo.Core.Query.Sql;

namespace Echo.Core.Query;

public static class AlbumForTrackQuery
{
    public static LibraryAlbumDto? Execute(ReadOnlyLibraryDatabase database, string? trackId)
    {
        if (string.IsNullOrEmpty(trackId))
            throw new UnsupportedQueryException("getAlbumForTrack requires a track id.");

        using var transaction = database.BeginDeferredRead();
        using (var reader = CachedQuery.Reader(database, $"""
            SELECT albums.id, albums.album_key, albums.title, albums.album_artist,
                   albums.year, albums.track_count, albums.duration,
                   COALESCE(covers.id, albums.cover_id) AS cover_id,
                   covers.source_type AS cover_source
            FROM album_tracks
            INNER JOIN albums ON albums.id = album_tracks.album_id
            INNER JOIN tracks ON tracks.id = album_tracks.track_id
            {AlbumDisplayCoverSql.Joins}
            WHERE album_tracks.track_id = ? AND tracks.missing = 0 AND tracks.library_hidden = 0
            ORDER BY album_tracks.position ASC
            LIMIT 1
            """, command => command.Add(trackId)))
        {
            if (reader.Read()) return AlbumRowMapper.Bind(reader).Map();
        }

        // Remote IDs are opaque: a prefix cannot decide whether null is authoritative.
        // Local results take precedence, including when a remote row has the same ID.
        var remote = CachedQuery.Scalar(database,
            "SELECT 1 FROM remote_tracks WHERE id = ? LIMIT 1", command => command.Add(trackId));
        if (remote is not null)
            throw new UnsupportedQueryException("Remote track album lookup requires the compatibility reader.");

        return null;
    }
}
