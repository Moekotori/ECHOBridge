using Microsoft.Data.Sqlite;

namespace Echo.Core.Database;

public sealed class SchemaCapabilityReport
{
    public bool Ok { get; init; }
    public string? Error { get; init; }
    public Dictionary<string, string> Sqlite { get; init; } = [];
}

public static class SchemaCapabilityProbe
{
    public static SchemaCapabilityReport Probe(SqliteConnection connection)
    {
        try
        {
            var version = Scalar(connection, "SELECT sqlite_version()") ?? "unknown";
            var fts = Scalar(connection, "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'tracks_fts'") == "1";
            var tracks = Scalar(connection, "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'tracks'") == "1";
            var sortKeys = Scalar(connection, "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'local_track_sort_keys'") == "1";
            var json = Scalar(connection, "SELECT json_extract('{\"ok\":1}', '$.ok')") == "1";
            if (!tracks)
            {
                return new SchemaCapabilityReport { Ok = false, Error = "tracks table is missing." };
            }

            if (!fts)
            {
                return new SchemaCapabilityReport { Ok = false, Error = "FTS5 tracks_fts table is missing." };
            }

            if (!sortKeys)
            {
                return new SchemaCapabilityReport { Ok = false, Error = "local_track_sort_keys table is missing." };
            }

            if (!json)
            {
                return new SchemaCapabilityReport { Ok = false, Error = "SQLite JSON1 is unavailable." };
            }

            return new SchemaCapabilityReport
            {
                Ok = true,
                Sqlite = new Dictionary<string, string>
                {
                    ["version"] = version,
                    ["fts5"] = "ok",
                    ["json1"] = "ok",
                },
            };
        }
        catch (Exception exception)
        {
            return new SchemaCapabilityReport { Ok = false, Error = exception.Message };
        }
    }

    private static string? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar()?.ToString();
    }
}
