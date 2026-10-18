namespace Echo.Core.Query.Sql;

internal static class ScopedTrackSearch
{
    public const string Predicate = " AND (? = '' OR tracks.title LIKE ? ESCAPE '\\' OR tracks.artist LIKE ? ESCAPE '\\' OR tracks.album LIKE ? ESCAPE '\\')";
    public static void Bind(Microsoft.Data.Sqlite.SqliteCommand command, string? search)
    {
        var text = search?.Trim() ?? "";
        var pattern = "%" + text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
        command.Add(text); command.Add(pattern); command.Add(pattern); command.Add(pattern);
    }
}
