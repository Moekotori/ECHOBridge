using Echo.Core.Database;
using Echo.Core.Mapping;
using Echo.Core.Protocol;
using Echo.Core.Query.Sql;
using Microsoft.Data.Sqlite;

namespace Echo.Core.Query;

internal static class InboxAggregates
{
    public static CoreInboxStory Story(ReadOnlyLibraryDatabase database, ResolvedInboxQuery query)
    {
        var selection = InboxSelectionBuilder.Build(query);
        var sql = $"""
            SELECT COUNT(*),
                   COUNT(DISTINCT NULLIF(trim(tracks.album), '')),
                   COUNT(DISTINCT NULLIF(trim(tracks.artist), '')),
                   COUNT(DISTINCT tracks.folder_id),
                   SUM(CASE WHEN {InboxSelectionBuilder.MissingCover} THEN 1 ELSE 0 END),
                   SUM(CASE WHEN {InboxSelectionBuilder.MetadataIssue} THEN 1 ELSE 0 END),
                   SUM(CASE WHEN {InboxSelectionBuilder.UnknownArtist} THEN 1 ELSE 0 END),
                   SUM(CASE WHEN {InboxSelectionBuilder.UnknownAlbum} THEN 1 ELSE 0 END),
                   SUM(CASE WHEN {InboxSelectionBuilder.Suspicious} THEN 1 ELSE 0 END),
                   SUM(CASE WHEN COALESCE(library_inbox_item_states.status, 'pending') = 'pending' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN COALESCE(library_inbox_item_states.status, 'pending') = 'processed' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN COALESCE(library_inbox_item_states.status, 'pending') = 'ignored' THEN 1 ELSE 0 END),
                   SUM(tracks.duration)
            {selection.FromSql}
            {selection.WhereSql}
            """;
        CoreInboxStory story;
        using (var reader = CachedQuery.Reader(database, sql, selection.Bind))
        {
            if (!reader.Read()) return new CoreInboxStory();
            var count = Int(reader, 0);
            var missingCover = Int(reader, 4);
            var metadataIssue = Int(reader, 5);
            story = new CoreInboxStory
            {
                TrackCount = count,
                AlbumCount = Int(reader, 1),
                ArtistCount = Int(reader, 2),
                FolderCount = Int(reader, 3),
                MissingCoverCount = missingCover,
                MetadataIssueCount = metadataIssue,
                UnknownArtistCount = Int(reader, 6),
                UnknownAlbumCount = Int(reader, 7),
                SuspiciousCount = Int(reader, 8),
                PendingCount = Int(reader, 9),
                ProcessedCount = Int(reader, 10),
                IgnoredCount = Int(reader, 11),
                CoverCompleteness = Completeness(count, missingCover),
                MetadataCompleteness = Completeness(count, metadataIssue),
                TotalDuration = Real(reader, 12),
            };
        }
        story.TopFolders = Facet(database, query with { FolderId = null }, "folder").Take(3).ToList();
        story.TopArtists = Facet(database, query with { Artist = null }, "artist").Take(3).ToList();
        return story;
    }

    public static List<CoreInboxAlbum> Albums(ReadOnlyLibraryDatabase database, ResolvedInboxQuery query)
    {
        var selection = InboxSelectionBuilder.Build(query);
        var sql = $"""
            SELECT COALESCE(NULLIF(trim(tracks.album), ''), 'Unknown Album') AS album,
                   COALESCE(NULLIF(trim(tracks.album_artist), ''), NULLIF(trim(tracks.artist), ''), 'Unknown Artist') AS album_artist,
                   MAX(tracks.cover_id), COUNT(*),
                   SUM(CASE WHEN {InboxSelectionBuilder.MissingCover} THEN 1 ELSE 0 END),
                   SUM(CASE WHEN {InboxSelectionBuilder.MetadataIssue} THEN 1 ELSE 0 END),
                   SUM(tracks.duration)
            {selection.FromSql}
            {selection.WhereSql}
            GROUP BY 1, 2
            ORDER BY COUNT(*) DESC, 1 COLLATE NOCASE
            LIMIT 24
            """;
        var result = new List<CoreInboxAlbum>(24);
        using var reader = CachedQuery.Reader(database, sql, selection.Bind);
        while (reader.Read())
        {
            var coverId = Text(reader, 2);
            result.Add(new CoreInboxAlbum
            {
                Album = LibraryText.Sanitize(Text(reader, 0), "Unknown Album"),
                AlbumArtist = LibraryText.Sanitize(Text(reader, 1), "Unknown Artist"),
                CoverId = coverId,
                CoverThumb = coverId is null ? null : LibraryText.CoverUrl(coverId, "album"),
                TrackCount = Int(reader, 3),
                MissingCoverCount = Int(reader, 4),
                MetadataIssueCount = Int(reader, 5),
                Duration = Real(reader, 6),
            });
        }
        return result;
    }

    public static CoreInboxFacets Facets(ReadOnlyLibraryDatabase database, ResolvedInboxQuery query) => new()
    {
        Folders = Facet(database, query with { FolderId = null }, "folder"),
        Albums = Facet(database, query with { Album = null }, "album"),
        Artists = Facet(database, query with { Artist = null }, "artist"),
    };

    private static List<CoreInboxFacet> Facet(ReadOnlyLibraryDatabase database, ResolvedInboxQuery query, string kind)
    {
        var selection = InboxSelectionBuilder.Build(query);
        var (value, label, extra) = kind switch
        {
            "folder" => ("tracks.folder_id", "library_inbox_batches.folder_name", ""),
            "album" => ("tracks.album", "COALESCE(NULLIF(trim(tracks.album), ''), 'Unknown Album')", " AND trim(tracks.album) != ''"),
            _ => ("tracks.artist", "COALESCE(NULLIF(trim(tracks.artist), ''), 'Unknown Artist')", " AND trim(tracks.artist) != ''"),
        };
        var sql = $"""
            SELECT {value} AS value, {label} AS label, COUNT(*) AS count
            {selection.FromSql}
            {selection.WhereSql}{extra}
            GROUP BY value, label
            ORDER BY count DESC, label COLLATE NOCASE
            LIMIT 50
            """;
        var result = new List<CoreInboxFacet>(50);
        using var reader = CachedQuery.Reader(database, sql, selection.Bind);
        while (reader.Read())
        {
            var valueText = Text(reader, 0) ?? string.Empty;
            result.Add(new CoreInboxFacet
            {
                Value = valueText,
                Label = LibraryText.Sanitize(Text(reader, 1), valueText),
                Count = Int(reader, 2),
            });
        }
        return result;
    }

    private static int Completeness(int total, int missing) => total > 0
        ? Math.Clamp((int)Math.Round((double)(total - missing) / total * 100, MidpointRounding.AwayFromZero), 0, 100)
        : 0;
    private static int Int(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? 0 : reader.GetInt32(index);
    private static double Real(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? 0 : reader.GetDouble(index);
    private static string? Text(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetString(index);
}
