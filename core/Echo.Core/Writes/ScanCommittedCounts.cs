using Echo.Core.Database;
using Echo.Core.Protocol;

namespace Echo.Core.Writes;

/// <summary>Task commit facts share the track/tail transaction and receipt; never count a replay.</summary>
internal static class ScanCommittedCounts
{
    public const string Capability = "library.scanCommittedCounts/v1";
    private const long MaxCount = 9_007_199_254_740_991;

    public static void Record(TransactionSql sql, LibraryMutation request, LibraryMutationResult result)
    {
        var jobId = request.Operation == "markMissing" ? request.MarkMissing?.ScanJobId : request.ScanJobId;
        if (jobId is null) return;
        long added = 0, updated = 0, removed = 0;
        if (request.Operation == "scan" && result.Tracks is not null)
            foreach (var track in result.Tracks) { if (track.Added) added++; else updated++; }
        else if (request.Operation == "repair" && result.Repairs is not null)
            foreach (var repair in result.Repairs) { if (repair.CoverChanged) updated++; }
        else if (request.Operation == "markMissing") removed = result.Removed;
        if (added + updated + removed == 0) return;
        if (sql.Run("""
            UPDATE scan_jobs SET added_tracks=COALESCE(added_tracks,0)+$0,
              updated_tracks=COALESCE(updated_tracks,0)+$1,removed_tracks=COALESCE(removed_tracks,0)+$2,
              updated_at=$3
            WHERE id=$4 AND status IN ('queued','running') AND cancel_requested=0
              AND COALESCE(added_tracks,0) BETWEEN 0 AND $5
              AND COALESCE(updated_tracks,0) BETWEEN 0 AND $6
              AND COALESCE(removed_tracks,0) BETWEEN 0 AND $7
            """, added, updated, removed, DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"), jobId,
            MaxCount - added, MaxCount - updated, MaxCount - removed) != 1)
            throw new InvalidOperationException("Scan commit counts could not be recorded safely.");
    }
}
