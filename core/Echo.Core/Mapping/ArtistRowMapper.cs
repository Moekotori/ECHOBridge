using Echo.Core.Protocol;
using Microsoft.Data.Sqlite;

namespace Echo.Core.Mapping;

internal readonly struct ArtistRowMapper
{
    private readonly SqliteDataReader _reader;
    private readonly int _id;
    private readonly int _artistKey;
    private readonly int _name;
    private readonly int _sortName;
    private readonly int _trackCount;
    private readonly int _albumCount;
    private readonly int _coverId;
    private readonly int _coverSource;
    private readonly int _avatarStatus;
    private readonly int _avatarProvider;
    private readonly int _avatarSourceHash;
    private readonly int _avatarThumbPath;
    private readonly int _avatarMediumPath;
    private readonly int _avatarLargePath;

    private ArtistRowMapper(SqliteDataReader reader)
    {
        _reader = reader;
        _id = reader.GetOrdinal("id");
        _artistKey = reader.GetOrdinal("artist_key");
        _name = reader.GetOrdinal("name");
        _sortName = reader.GetOrdinal("sort_name");
        _trackCount = reader.GetOrdinal("track_count");
        _albumCount = reader.GetOrdinal("album_count");
        _coverId = reader.GetOrdinal("cover_id");
        _coverSource = reader.GetOrdinal("cover_source");
        _avatarStatus = reader.GetOrdinal("avatar_status");
        _avatarProvider = reader.GetOrdinal("avatar_provider");
        _avatarSourceHash = reader.GetOrdinal("avatar_source_hash");
        _avatarThumbPath = reader.GetOrdinal("avatar_thumb_path");
        _avatarMediumPath = reader.GetOrdinal("avatar_medium_path");
        _avatarLargePath = reader.GetOrdinal("avatar_large_path");
    }

    public static ArtistRowMapper Bind(SqliteDataReader reader) => new(reader);

    public LibraryArtistDto Map()
    {
        var trackCount = Int(_trackCount) ?? 0;
        var albumCount = Int(_albumCount) ?? 0;
        var artistKey = Text(_artistKey) ?? string.Empty;
        var name = DisplayText(Text(_name), static () => LibraryText.UnknownArtist);
        var coverId = Text(_coverId);
        var avatarStatus = Text(_avatarStatus);
        var avatarSourceHash = Text(_avatarSourceHash);
        var hasMatchedAvatar = avatarStatus == "matched" && IsCurrentAvatarHash(avatarSourceHash);
        var large = Text(_avatarLargePath);
        var medium = Text(_avatarMediumPath);
        var thumb = Text(_avatarThumbPath);
        return new LibraryArtistDto
        {
            Id = Text(_id) ?? string.Empty,
            MediaType = "local",
            ArtistKey = artistKey,
            Name = name,
            SortName = DisplayText(Text(_sortName) ?? name, () => name),
            Role = trackCount > 0 && albumCount > 0 ? "both" : albumCount > 0 ? "album" : "track",
            TrackCount = trackCount,
            AlbumCount = albumCount,
            CoverId = coverId,
            CoverThumb = string.IsNullOrEmpty(coverId) ? null : LibraryText.CoverUrl(coverId, "album"),
            CoverSource = Text(_coverSource),
            AvatarThumbUrl = hasMatchedAvatar && thumb is not null ? LibraryText.ArtistImageUrl(artistKey, "thumb") : null,
            AvatarUrl = hasMatchedAvatar && (large ?? medium ?? thumb) is not null
                ? LibraryText.ArtistImageUrl(artistKey, large is not null ? "large" : "medium")
                : null,
            AvatarStatus = avatarStatus,
            AvatarProvider = Text(_avatarProvider),
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

    private static bool IsCurrentAvatarHash(string? sourceHash) =>
        sourceHash is "artist-image-cache-v5"
        || sourceHash?.StartsWith("artist-image-cache-v5:", StringComparison.Ordinal) == true;
}
