using Echo.Core.Database;
using Echo.Core.Protocol;
using static Echo.Core.Writes.JsonFields;

namespace Echo.Core.Writes;

internal static class ScanTrackWriter
{
    public static ScanWriteResult Write(TransactionSql sql, ScanWriteItem item)
    {
        var t=item.Track;
        var path=Path.GetFullPath(Text(t,"path") ?? throw new ArgumentException("Track path required."));
        var id=Text(t,"id") ?? throw new ArgumentException("Track ID required.");
        id=ScanIdentityWriter.Move(sql,item.IdentityMove,t,path) ?? id;
        var existing=sql.Value("SELECT id FROM tracks WHERE path=$0",path) as string;
        if(existing is null && sql.Value("SELECT id FROM tracks WHERE id=$0",id) is not null)
        { sql.Run("UPDATE tracks SET path=$0 WHERE id=$1",path,id); existing=id; }
        id=existing ?? id;
        var timestamp=Text(t,"updatedAt") ?? throw new ArgumentException("Timestamp required.");
        var created=sql.Value("SELECT created_at FROM tracks WHERE id=$0",id) as string ?? Text(t,"createdAt") ?? timestamp;
        var coverId=item.Cover.ValueKind==System.Text.Json.JsonValueKind.Object ? ScanCoverWriter.Write(sql,item.Cover,timestamp) : Text(t,"coverId");
        if(t.TryGetProperty("audioAnalysisIdentity",out var audio)) ScanIdentityWriter.Invalidate(sql,id,audio);
        var bpm=Number(t,"bpm");
        var tagged=Number(t,"replayGainTrackGainDb") is not null || Number(t,"replayGainAlbumGainDb") is not null;
        sql.Run("""
INSERT INTO tracks (
        id, path, folder_id, size_bytes, mtime_ms, title, artist, album, album_artist,
        track_no, disc_no, year, genre, duration, codec, sample_rate, bit_depth, bitrate,
        bpm, bpm_confidence, beat_offset_ms, analysis_status, analysis_version, analysis_error, analysis_updated_at,
        replay_gain_track_gain_db, replay_gain_album_gain_db, replay_gain_track_peak, replay_gain_album_peak,
        replay_gain_integrated_lufs, replay_gain_source, replay_gain_status, replay_gain_version, replay_gain_error, replay_gain_updated_at,
        file_identity, file_identity_source, quick_hash, quick_hash_version, identity_status, identity_updated_at, identity_error,
        search_terms, cover_id, metadata_status, embedded_metadata_status, embedded_cover_status, network_metadata_status,
        field_sources_json, missing, created_at, updated_at
      ) VALUES ($0, $1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15, $16, $17, $18, $19, $20, $21, $22, $23, $24, $25, $26, $27, $28, $29, $30, $31, $32, $33, $34, $35, $36, $37, $38, $39, $40, $41, $42, $43, $44, $45, $46, $47, $48, $49, $50, $51)
      ON CONFLICT(path) DO UPDATE SET
        folder_id = excluded.folder_id,
        size_bytes = excluded.size_bytes,
        mtime_ms = excluded.mtime_ms,
        title = excluded.title,
        artist = excluded.artist,
        album = excluded.album,
        album_artist = excluded.album_artist,
        track_no = excluded.track_no,
        disc_no = excluded.disc_no,
        year = excluded.year,
        genre = excluded.genre,
        duration = excluded.duration,
        codec = excluded.codec,
        sample_rate = excluded.sample_rate,
        bit_depth = excluded.bit_depth,
        bitrate = excluded.bitrate,
        bpm = COALESCE(excluded.bpm, tracks.bpm),
        bpm_confidence = COALESCE(excluded.bpm_confidence, tracks.bpm_confidence),
        beat_offset_ms = COALESCE(excluded.beat_offset_ms, tracks.beat_offset_ms),
        analysis_status = CASE WHEN excluded.bpm IS NOT NULL THEN excluded.analysis_status ELSE tracks.analysis_status END,
        analysis_version = CASE WHEN excluded.bpm IS NOT NULL THEN excluded.analysis_version ELSE tracks.analysis_version END,
        analysis_error = CASE WHEN excluded.bpm IS NOT NULL THEN excluded.analysis_error ELSE tracks.analysis_error END,
        analysis_updated_at = CASE WHEN excluded.bpm IS NOT NULL THEN excluded.analysis_updated_at ELSE tracks.analysis_updated_at END,
        replay_gain_track_gain_db = CASE WHEN excluded.replay_gain_source = 'tag' THEN excluded.replay_gain_track_gain_db ELSE tracks.replay_gain_track_gain_db END,
        replay_gain_album_gain_db = CASE WHEN excluded.replay_gain_source = 'tag' THEN excluded.replay_gain_album_gain_db ELSE tracks.replay_gain_album_gain_db END,
        replay_gain_track_peak = CASE WHEN excluded.replay_gain_source = 'tag' THEN excluded.replay_gain_track_peak ELSE tracks.replay_gain_track_peak END,
        replay_gain_album_peak = CASE WHEN excluded.replay_gain_source = 'tag' THEN excluded.replay_gain_album_peak ELSE tracks.replay_gain_album_peak END,
        replay_gain_integrated_lufs = CASE WHEN excluded.replay_gain_source = 'tag' THEN excluded.replay_gain_integrated_lufs ELSE tracks.replay_gain_integrated_lufs END,
        replay_gain_source = CASE WHEN excluded.replay_gain_source = 'tag' THEN excluded.replay_gain_source ELSE tracks.replay_gain_source END,
        replay_gain_status = CASE WHEN excluded.replay_gain_source = 'tag' THEN excluded.replay_gain_status ELSE tracks.replay_gain_status END,
        replay_gain_version = CASE WHEN excluded.replay_gain_source = 'tag' THEN excluded.replay_gain_version ELSE tracks.replay_gain_version END,
        replay_gain_error = CASE WHEN excluded.replay_gain_source = 'tag' THEN excluded.replay_gain_error ELSE tracks.replay_gain_error END,
        replay_gain_updated_at = CASE WHEN excluded.replay_gain_source = 'tag' THEN excluded.replay_gain_updated_at ELSE tracks.replay_gain_updated_at END,
        file_identity = CASE WHEN excluded.identity_status IS NOT NULL THEN excluded.file_identity ELSE tracks.file_identity END,
        file_identity_source = CASE WHEN excluded.identity_status IS NOT NULL THEN excluded.file_identity_source ELSE tracks.file_identity_source END,
        quick_hash = CASE WHEN excluded.identity_status IS NOT NULL THEN excluded.quick_hash ELSE tracks.quick_hash END,
        quick_hash_version = CASE WHEN excluded.identity_status IS NOT NULL THEN excluded.quick_hash_version ELSE tracks.quick_hash_version END,
        identity_status = CASE WHEN excluded.identity_status IS NOT NULL THEN excluded.identity_status ELSE tracks.identity_status END,
        identity_updated_at = CASE WHEN excluded.identity_status IS NOT NULL THEN excluded.identity_updated_at ELSE tracks.identity_updated_at END,
        identity_error = CASE WHEN excluded.identity_status IS NOT NULL THEN excluded.identity_error ELSE tracks.identity_error END,
        search_terms = excluded.search_terms,
        cover_id = excluded.cover_id,
        metadata_status = excluded.metadata_status,
        embedded_metadata_status = excluded.embedded_metadata_status,
        embedded_cover_status = excluded.embedded_cover_status,
        network_metadata_status = excluded.network_metadata_status,
        field_sources_json = CASE WHEN excluded.bpm IS NULL AND tracks.bpm IS NOT NULL
          AND json_extract(tracks.field_sources_json, '$.bpm') IS NOT NULL
          THEN json_set(excluded.field_sources_json, '$.bpm', json_extract(tracks.field_sources_json, '$.bpm'))
          ELSE excluded.field_sources_json END,
        updated_at = excluded.updated_at
        -- library_hidden stays at its stored value. A rescan must not unhide a track.
""", id,
            path,
            Value(t,"folderId"),
            Value(t,"sizeBytes"),
            Value(t,"mtimeMs"),
            Value(t,"title"),
            Value(t,"artist"),
            Value(t,"album"),
            Value(t,"albumArtist"),
            Value(t,"trackNo"),
            Value(t,"discNo"),
            Value(t,"year"),
            Value(t,"genre"),
            Value(t,"duration"),
            Value(t,"codec"),
            Value(t,"sampleRate"),
            Value(t,"bitDepth"),
            Value(t,"bitrate"),
            Value(t,"bpm"),
            bpm is not null && bpm != 0 ? 1 : null,
            null,
            bpm is not null && bpm != 0 ? "complete" : "none",
            bpm is not null && bpm != 0 ? 1 : 0,
            null,
            bpm is not null && bpm != 0 ? timestamp : null,
            Value(t,"replayGainTrackGainDb"),
            Value(t,"replayGainAlbumGainDb"),
            Value(t,"replayGainTrackPeak"),
            Value(t,"replayGainAlbumPeak"),
            Value(t,"replayGainIntegratedLufs"),
            tagged ? "tag" : "none",
            tagged ? "tagged" : "none",
            tagged ? 2 : 0,
            null,
            tagged ? timestamp : null,
            Value(t,"fileIdentity"),
            Value(t,"fileIdentitySource"),
            Value(t,"quickHash"),
            Value(t,"quickHashVersion"),
            Value(t,"identityStatus"),
            Value(t,"identityUpdatedAt"),
            Value(t,"identityError"),
            item.SearchTerms,
            coverId,
            Text(t,"metadataStatus") ?? "ok",
            Text(t,"embeddedMetadataStatus") ?? "pending",
            Text(t,"embeddedCoverStatus") ?? "pending",
            "none",
            Get(t,"fieldSources").GetRawText(),
            0,
            created,
            Value(t,"updatedAt"));
        // Leave visibility indexes untouched on the common present-track path.
        if (existing is not null) sql.Run("UPDATE tracks SET missing=0 WHERE id=$0 AND missing!=0",id);
        if(t.TryGetProperty("audioAnalysisIdentity",out audio)) ScanIdentityWriter.Save(sql,id,audio);
        if(t.TryGetProperty("musicGraph",out var graph)) ScanMusicGraphWriter.Sync(sql,id,Text(t,"title") ?? "",graph,item.GraphFingerprint,timestamp);
        ScanPlaylistRelinker.Relink(sql,id,path,t,coverId,timestamp);
        return new(id,existing is null);
    }
}
