using Echo.Core.Database;
using Echo.Core.Protocol;
using Echo.Core.Query.Sql;

namespace Echo.Core.Query;

/// <summary>
/// Active CUE virtual tracks (`…#cueTrack=N`) as bare id/path refs. File deletion needs only these
/// to detect a shared physical source, instead of mapping the whole library in the Electron process.
/// </summary>
public static class CueTrackRefsQuery
{
    public const string Capability = "library.cueTrackRefs/v1";

    public static TrackRefPage Execute(ReadOnlyLibraryDatabase database, RowIdCursorParams? request)
    {
        var limit = Math.Clamp(request?.Limit ?? ProtocolConstants.MaxPageSize, 1, ProtocolConstants.MaxPageSize);
        var cursor = request?.Cursor ?? 0;
        var page = new TrackRefPage { Cursor = cursor };
        using var reader = CachedQuery.Reader(database,
            "SELECT rowid, id, path FROM tracks WHERE missing = 0 AND path LIKE '%#cueTrack=%' AND rowid > ? ORDER BY rowid LIMIT ?",
            command => { command.Add(cursor); command.Add(limit + 1); });
        while (reader.Read())
        {
            if (page.Items.Count == limit) { page.HasMore = true; break; }
            page.Cursor = reader.GetInt64(0);
            page.Items.Add(new(reader.GetString(1), reader.GetString(2)));
        }
        return page;
    }
}
