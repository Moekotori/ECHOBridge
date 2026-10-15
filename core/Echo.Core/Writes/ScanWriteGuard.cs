using Echo.Core.Database;
using Echo.Core.Protocol;

namespace Echo.Core.Writes;

/// <summary>Checked inside the same transaction as the write, after receipt lookup.</summary>
internal static class ScanWriteGuard
{
    public static string RequireActive(TransactionSql sql, string jobId, string? folderId = null)
    {
        var owner = sql.Value("""
            SELECT jobs.folder_id FROM scan_jobs jobs JOIN folders ON folders.id=jobs.folder_id
            WHERE jobs.id=$0 AND jobs.status IN ('queued','running')
              AND jobs.cancel_requested=0 AND folders.status='active'
            """, jobId) as string;
        if (owner is null || folderId is not null && owner != folderId)
            throw new InvalidOperationException("Scan job is no longer active for this folder.");
        return owner;
    }

    public static void ValidateMutation(TransactionSql sql, LibraryMutation request)
    {
        if (request.ScanJobId is null) return;
        var folder = RequireActive(sql, request.ScanJobId);
        var matches = request.Operation switch
        {
            "scan" => request.Items.All(item => JsonFields.Text(item.Track, "folderId") == folder),
            "repair" => request.Repairs.All(item => sql.Value("SELECT folder_id FROM tracks WHERE id=$0", item.TrackId) as string == folder),
            "snapshots" => request.Snapshots.All(item => item.FolderId == folder),
            _ => true,
        };
        if (!matches) throw new InvalidOperationException("Scan batch does not belong to its task folder.");
    }
}
