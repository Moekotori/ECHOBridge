using System.Text.Json;
using Echo.Core.Database;
using static Echo.Core.Writes.JsonFields;

namespace Echo.Core.Writes;

internal static class ScanIdentityWriter
{
    public static string? Move(TransactionSql sql, JsonElement move, JsonElement t, string path)
    {
        var id=Text(move,"id"); var oldPath=Text(move,"path");
        if(id is null || oldPath is null || path.Contains("#cueTrack=") || oldPath.Contains("#cueTrack=")
            || Text(t,"identityStatus")!="ok" || Text(t,"fileIdentitySource") is not ("win32-file-id" or "posix-dev-ino")
            || string.IsNullOrEmpty(Text(t,"fileIdentity")) || string.IsNullOrEmpty(Text(t,"quickHash"))) return null;
        if(Convert.ToInt64(sql.Value("SELECT COUNT(*) FROM tracks WHERE file_identity=$0 AND file_identity_source=$1 AND identity_status='ok'", Text(t,"fileIdentity"),Text(t,"fileIdentitySource")))!=1) return null;
        if(sql.Value("SELECT 1 FROM tracks WHERE id=$0 AND path=$1 AND size_bytes=$2 AND quick_hash=$3 AND quick_hash_version=$4 AND file_identity=$5 AND file_identity_source=$6 AND identity_status='ok'",id,oldPath,Number(t,"sizeBytes"),Text(t,"quickHash"),Number(t,"quickHashVersion"),Text(t,"fileIdentity"),Text(t,"fileIdentitySource")) is null
            || sql.Value("SELECT 1 FROM tracks WHERE path=$0",path) is not null) return null;
        sql.Run("UPDATE tracks SET path=$0 WHERE id=$1 AND path=$2",path,id,oldPath);
        sql.Run("UPDATE playback_history SET track_path=$0 WHERE track_id=$1",path,id);
        sql.Run("UPDATE playback_history_stats SET track_path=$0 WHERE track_id=$1",path,id);
        return id;
    }

    public static void Invalidate(TransactionSql sql,string id,JsonElement audio)
    {
        var fingerprint=Text(audio,"fingerprint");
        if(fingerprint is not null && Equals(sql.Value("SELECT fingerprint FROM track_audio_identity WHERE track_id=$0",id),fingerprint)) return;
        if (fingerprint is not null && sql.Value("""
            SELECT fingerprint FROM track_audio_identity WHERE track_id=$0
              AND fingerprint NOT LIKE 'snapshot-v2:%' AND size_bytes=$1 AND mtime_ms=$2 AND ctime_ms=$3
            """, id, Number(audio, "size"), Number(audio, "mtimeMs"), Number(audio, "ctimeMs")) is string oldFingerprint)
        {
            sql.Run("UPDATE track_energy_analysis SET fingerprint=$0 WHERE track_id=$1 AND fingerprint=$2", fingerprint, id, oldFingerprint);
            sql.Run("UPDATE track_vocal_analysis SET fingerprint=$0 WHERE track_id=$1 AND fingerprint=$2", fingerprint, id, oldFingerprint);
            return;
        }
        sql.Run("""
            UPDATE tracks SET bpm=NULL,bpm_confidence=NULL,beat_offset_ms=NULL,analysis_status='none',analysis_version=0,analysis_error=NULL,analysis_updated_at=NULL
            WHERE id=$0 AND COALESCE(json_extract(field_sources_json,'$.bpm'),'') NOT IN ('manual','embedded','osu')
            """,id);
        sql.Run("""
            UPDATE tracks SET replay_gain_track_gain_db=NULL,replay_gain_album_gain_db=NULL,replay_gain_track_peak=NULL,replay_gain_album_peak=NULL,
            replay_gain_integrated_lufs=NULL,replay_gain_source='none',replay_gain_status='none',replay_gain_version=0,replay_gain_error=NULL,replay_gain_updated_at=NULL
            WHERE id=$0 AND replay_gain_source='analysis'
            """,id);
        sql.Run("DELETE FROM track_energy_analysis WHERE track_id=$0",id);
        // Manual vocal marks are user decisions and survive; only the measured score is stale.
        sql.Run("DELETE FROM track_vocal_analysis WHERE track_id=$0",id);
    }

    public static void Save(TransactionSql sql,string id,JsonElement audio)
    {
        if(audio.ValueKind!=JsonValueKind.Object) { sql.Run("DELETE FROM track_audio_identity WHERE track_id=$0",id); return; }
        sql.Run("""
            INSERT INTO track_audio_identity VALUES ($0,$1,$2,$3,$4)
            ON CONFLICT(track_id) DO UPDATE SET fingerprint=excluded.fingerprint,size_bytes=excluded.size_bytes,mtime_ms=excluded.mtime_ms,ctime_ms=excluded.ctime_ms
            """,id,Text(audio,"fingerprint"),Number(audio,"size"),Number(audio,"mtimeMs"),Number(audio,"ctimeMs"));
    }
}
