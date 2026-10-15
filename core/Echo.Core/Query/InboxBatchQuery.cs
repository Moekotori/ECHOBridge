using Echo.Core.Database;
using Echo.Core.Mapping;
using Echo.Core.Protocol;
using Echo.Core.Query.Sql;

namespace Echo.Core.Query;

public static class InboxBatchQuery
{
    public static List<CoreInboxBatch> List(ReadOnlyLibraryDatabase database)
    {
        const string sql = """
            SELECT id, scan_job_id, folder_id, folder_name, folder_path,
                   added_count, missing_cover_count, metadata_issue_count, created_at, finished_at
            FROM library_inbox_batches
            WHERE added_count > 0
            ORDER BY finished_at DESC, created_at DESC
            LIMIT 30
            """;
        var batches = new List<CoreInboxBatch>(30);
        using var reader = CachedQuery.Reader(database, sql);
        while (reader.Read()) batches.Add(Map(reader));
        return batches;
    }

    public static CoreInboxBatch? ById(ReadOnlyLibraryDatabase database, string id)
    {
        const string sql = """
            SELECT id, scan_job_id, folder_id, folder_name, folder_path,
                   added_count, missing_cover_count, metadata_issue_count, created_at, finished_at
            FROM library_inbox_batches WHERE id = ? AND added_count > 0
            """;
        using var reader = CachedQuery.Reader(database, sql, command => command.Add(id));
        return reader.Read() ? Map(reader) : null;
    }

    private static CoreInboxBatch Map(Microsoft.Data.Sqlite.SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        ScanJobId = reader.GetString(1),
        FolderId = reader.GetString(2),
        FolderName = LibraryText.Sanitize(reader.GetString(3), "Library Folder"),
        FolderPath = reader.GetString(4),
        AddedCount = reader.GetInt32(5),
        MissingCoverCount = reader.GetInt32(6),
        MetadataIssueCount = reader.GetInt32(7),
        CreatedAt = reader.GetString(8),
        FinishedAt = reader.GetString(9),
    };
}
