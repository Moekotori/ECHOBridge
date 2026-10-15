using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Echo.Core.Playlists;

internal static class PlaylistBackupWriter
{
    // The read snapshot and mutation share a transaction. Rows stream to disk;
    // even a large playlist never becomes a full in-memory backup document.
    public static void Write(PlaylistSql sql, string id, string reason, string? directory, string timestamp)
    {
        if (string.IsNullOrEmpty(directory)) return;
        if (!Path.IsPathFullyQualified(directory)) throw new ArgumentException("Backup directory must be absolute.");
        using var playlistCommand = sql.Command("SELECT * FROM playlists WHERE id = $0", id);
        using var playlist = playlistCommand.ExecuteReader();
        if (!playlist.Read()) return;
        var destination = Path.Combine(directory, "ECHO Playlist Backups");
        Directory.CreateDirectory(destination);
        // IDs, rather than user-controlled names, keep backup paths portable and safe.
        var name = timestamp.Replace(':', '-').Replace('.', '-') + "_" + Guid.NewGuid().ToString("N") + "_" + reason + ".json";
        var final = Path.Combine(destination, name);
        var temporary = final + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16 * 1024))
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteNumber("formatVersion", 1); writer.WriteString("app", "ECHO");
                writer.WriteString("reason", reason); writer.WriteString("backedUpAt", timestamp);
                writer.WritePropertyName("playlist"); WriteRow(writer, playlist); playlist.Close();
                writer.WriteNumber("itemCount", Convert.ToInt64(sql.Value("SELECT COUNT(*) FROM playlist_items WHERE playlist_id = $0", id)));
                writer.WriteStartArray("items");
                using var command = sql.Command("""
                    SELECT playlist_items.*, tracks.path AS track_path, tracks.title AS track_title,
                      tracks.artist AS track_artist, tracks.album AS track_album, tracks.album_artist AS track_album_artist,
                      tracks.duration AS track_duration, tracks.cover_id AS track_cover_id, tracks.missing AS track_missing,
                      streaming_tracks.title AS streaming_title, streaming_tracks.artist AS streaming_artist,
                      streaming_tracks.album AS streaming_album, streaming_tracks.duration AS streaming_duration,
                      streaming_tracks.cover_url AS streaming_cover_url, streaming_tracks.playable AS streaming_playable,
                      streaming_tracks.unavailable_reason AS streaming_unavailable_reason
                    FROM playlist_items LEFT JOIN tracks ON tracks.id = playlist_items.media_id
                    LEFT JOIN streaming_tracks ON streaming_tracks.provider = playlist_items.source_provider
                      AND streaming_tracks.provider_track_id = playlist_items.source_item_id
                    WHERE playlist_items.playlist_id = $0 ORDER BY playlist_items.position ASC, playlist_items.added_at ASC
                    """, id);
                using var reader = command.ExecuteReader();
                while (reader.Read()) { WriteRow(writer, reader); writer.Flush(); }
                writer.WriteEndArray(); writer.WriteEndObject(); writer.Flush(); stream.Flush(true);
            }
            File.Move(temporary, final);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static void WriteRow(Utf8JsonWriter writer, SqliteDataReader reader)
    {
        writer.WriteStartObject();
        for (var i = 0; i < reader.FieldCount; i++)
        {
            writer.WritePropertyName(reader.GetName(i));
            var value = reader.GetValue(i);
            switch (value)
            {
                case DBNull: writer.WriteNullValue(); break;
                case long integer: writer.WriteNumberValue(integer); break;
                case double number: writer.WriteNumberValue(number); break;
                case byte[] bytes: writer.WriteBase64StringValue(bytes); break;
                default: writer.WriteStringValue(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)); break;
            }
        }
        writer.WriteEndObject();
    }
}
