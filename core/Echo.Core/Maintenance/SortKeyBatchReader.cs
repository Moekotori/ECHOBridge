using Microsoft.Data.Sqlite;
using static Echo.Core.Maintenance.SortKeyMaintenanceSql;

namespace Echo.Core.Maintenance;

internal sealed record SortKeyRow(string Id, string UpdatedAt, string?[] Texts);
internal sealed class SortKeyBatch
{
    public List<SortKeyRow> Rows { get; } = [];
    public List<string> DeletedIds { get; } = [];
    public string? LastId { get; set; }
    public long? LastRowId { get; set; }
    public bool End { get; set; }
}

internal static class SortKeyBatchReader
{
    public const int MaximumRows = 512;
    public const int MaximumTextChars = 262144;
    private const int MaximumFieldChars = 65536;

    public static SortKeyBatch Read(SqliteConnection connection, SqliteTransaction tx, SortKeySchema schema,
        bool prune, string? afterId, long? afterRowId)
    {
        var missing = schema.Kind == "track" ? "source.missing != 0" : "0";
        var dirty = string.Join(" OR ", schema.KeyNames.Select((key, i) => $"keys.{key}_text IS NOT source.{schema.TextColumns[i]}"));
        var textColumns = string.Join(",", schema.TextColumns.Select(text => $"substr(source.{text},1,{MaximumFieldChars + 1})"));
        var sql = prune
            ? $"SELECT substr(keys.{schema.IdColumn},1,{MaximumFieldChars + 1}), source.id IS NULL OR {missing} " +
              $"FROM {schema.Table} keys LEFT JOIN {schema.Source} source ON source.id=keys.{schema.IdColumn} " +
              (afterId is null ? "" : $"WHERE keys.{schema.IdColumn}>$p0 ") + $"ORDER BY keys.{schema.IdColumn} LIMIT {MaximumRows}"
            : $"SELECT source.rowid,substr(source.id,1,{MaximumFieldChars + 1}),substr(source.updated_at,1,{MaximumFieldChars + 1}), " +
              $"{missing}, keys.{schema.IdColumn} IS NULL OR {dirty}, {textColumns} " +
              $"FROM {schema.Source} source LEFT JOIN {schema.Table} keys ON keys.{schema.IdColumn}=source.id " +
              (afterRowId is null ? "" : "WHERE source.rowid>$p0 ") + $"ORDER BY source.rowid LIMIT {MaximumRows}";
        object? after = prune ? afterId : afterRowId;
        using var command = Command(connection, tx, sql, after is null ? [] : [after]);
        using var reader = command.ExecuteReader();
        var batch = new SortKeyBatch();
        var count = 0;
        var chars = 0;
        while (reader.Read())
        {
            if (prune)
            {
                var id = Text(reader, 0);
                if (chars + id.Length > MaximumTextChars) return batch;
                chars += id.Length;
                batch.LastId = id;
                if (reader.GetBoolean(1)) batch.DeletedIds.Add(id);
            }
            else
            {
                if (!reader.GetBoolean(3) && reader.GetBoolean(4))
                {
                    var row = new SortKeyRow(Text(reader, 1), Text(reader, 2),
                        Enumerable.Range(0, schema.TextColumns.Length).Select(i => reader.IsDBNull(i + 5) ? null : Text(reader, i + 5)).ToArray());
                    var rowChars = row.Id.Length + row.UpdatedAt.Length + row.Texts.Sum(text => text?.Length ?? 0);
                    if (rowChars > MaximumTextChars) throw new InvalidOperationException("Sort-key row exceeds maintenance text limit.");
                    if (chars + rowChars > MaximumTextChars) return batch;
                    chars += rowChars;
                    batch.Rows.Add(row);
                }
                batch.LastRowId = reader.GetInt64(0);
            }
            count++;
        }
        batch.End = count < MaximumRows;
        return batch;
    }

    private static string Text(SqliteDataReader reader, int ordinal)
    {
        var value = reader.IsDBNull(ordinal) ? "" : reader.GetString(ordinal);
        if (value.Length > MaximumFieldChars) throw new InvalidOperationException("Sort-key field exceeds maintenance text limit.");
        return value;
    }
}
