using Echo.Core.Database;
using Echo.Core.Protocol;
using Echo.Core.Query.Sql;

namespace Echo.Core.Query;

/// <summary>
/// Ids the current scan marked missing, paged by rowid so the scan can seed album/artist
/// maintenance without ever holding the folder's rows in the Electron process.
/// </summary>
public static class ScanMarkedMissingQuery
{
    public static ScanMarkedMissingPage Execute(ReadOnlyLibraryDatabase database, ScanMarkedMissingParams? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.FolderId) || string.IsNullOrWhiteSpace(request.UpdatedAt))
            throw new ArgumentException("marked-missing lookup requires folderId and updatedAt.");
        var limit = Math.Clamp(request.Limit ?? ProtocolConstants.MaxScanCacheStatePaths, 1, ProtocolConstants.MaxScanCacheStatePaths);
        var cursor = request.Cursor ?? 0;
        var page = new ScanMarkedMissingPage { Cursor = cursor };
        using var reader = CachedQuery.Reader(database,
            "SELECT rowid, id FROM tracks WHERE folder_id = ? AND missing = 1 AND updated_at = ? AND rowid > ? ORDER BY rowid LIMIT ?",
            command => { command.Add(request.FolderId); command.Add(request.UpdatedAt); command.Add(cursor); command.Add(limit + 1); });
        while (reader.Read())
        {
            if (page.Ids.Count == limit) { page.HasMore = true; break; }
            page.Cursor = reader.GetInt64(0);
            page.Ids.Add(reader.GetString(1));
        }
        return page;
    }
}
