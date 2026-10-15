using Microsoft.Data.Sqlite;

namespace Echo.Core.Mapping;

internal static class RowMapperColumns
{
    /// <summary>Ordinal of a column that only some projections select, or -1 when absent.</summary>
    public static int Optional(SqliteDataReader reader, string name)
    {
        for (var ordinal = 0; ordinal < reader.FieldCount; ordinal++)
        {
            if (string.Equals(reader.GetName(ordinal), name, StringComparison.OrdinalIgnoreCase))
            {
                return ordinal;
            }
        }

        return -1;
    }
}
