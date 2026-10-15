using System.Text.Json;
using Echo.Core.Database;
using Echo.Core.Protocol;
using static Echo.Core.Writes.JsonFields;

namespace Echo.Core.Writes;

/// <summary>
/// Scan-tail writes for files whose bytes did not change: a regenerated cover derivative
/// and/or a fresh file-identity observation. Mirrors the legacy
/// <c>upsertCover + updateTrackCover</c> / <c>updateTrackIdentity</c> pair.
/// </summary>
internal static class ScanRepairWriter
{
    public static ScanRepairResult Write(TransactionSql sql, ScanRepairItem item)
    {
        if (string.IsNullOrWhiteSpace(item.TrackId) || string.IsNullOrWhiteSpace(item.UpdatedAt))
            throw new ArgumentException("Repair item requires trackId and updatedAt.");
        var coverChanged = false;
        if (item.Cover.ValueKind == JsonValueKind.Object)
        {
            var coverId = ScanCoverWriter.Write(sql, item.Cover, item.UpdatedAt);
            var current = sql.Value("SELECT cover_id FROM tracks WHERE id=$0", item.TrackId) as string;
            if (!string.Equals(coverId, current, StringComparison.Ordinal))
                coverChanged = sql.Run("UPDATE tracks SET cover_id=$0, updated_at=$1 WHERE id=$2", coverId, item.UpdatedAt, item.TrackId) > 0;
        }
        if (item.Identity.ValueKind == JsonValueKind.Object)
        {
            var identity = item.Identity;
            sql.Run("""
                UPDATE tracks SET file_identity=$0, file_identity_source=$1, quick_hash=$2, quick_hash_version=$3,
                  identity_status=$4, identity_updated_at=$5, identity_error=$6, updated_at=$7 WHERE id=$8
                """,
                Value(identity, "fileIdentity"), Value(identity, "fileIdentitySource"), Value(identity, "quickHash"), Value(identity, "quickHashVersion"),
                Value(identity, "identityStatus"), Text(identity, "identityUpdatedAt") ?? item.UpdatedAt, Value(identity, "identityError"),
                item.UpdatedAt, item.TrackId);
        }
        return new(item.TrackId, coverChanged);
    }
}
