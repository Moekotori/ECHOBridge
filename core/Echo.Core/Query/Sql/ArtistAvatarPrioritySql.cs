namespace Echo.Core.Query.Sql;

// Keep eligibility aligned with library/artistOrderSql.ts, including empty non-null paths.
internal static class ArtistAvatarPrioritySql
{
    internal const string Join = "LEFT JOIN artist_image_cache AS avatar ON avatar.artist_key = artists.artist_key";
    internal const string Order = """
        CASE WHEN avatar.status = 'matched'
          AND (avatar.source_hash = 'artist-image-cache-v5' OR avatar.source_hash LIKE 'artist-image-cache-v5:%')
          AND COALESCE(avatar.large_path, avatar.medium_path, avatar.thumb_path) IS NOT NULL
        THEN 0 ELSE 1 END ASC
        """;
}
