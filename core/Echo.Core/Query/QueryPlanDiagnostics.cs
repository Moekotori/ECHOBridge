using Echo.Core.Database;
using Echo.Core.Protocol;
using Echo.Core.Query.Sql;
using Microsoft.Data.Sqlite;

namespace Echo.Core.Query;

internal static class QueryPlanDiagnostics
{
    private static readonly Queue<string> Plans = new();
    private const int MaxPlans = 16;

    public static bool Enabled =>
        string.Equals(Environment.GetEnvironmentVariable("ECHO_CORE_QUERY_PLAN"), "1", StringComparison.Ordinal);

    public static IReadOnlyList<string> Snapshot()
    {
        lock (Plans)
        {
            return [.. Plans];
        }
    }

    public static void Capture(ReadOnlyLibraryDatabase database, string label, string sql, Action<SqliteCommand>? bind = null)
    {
        if (!Enabled)
        {
            return;
        }

        using var command = database.RequireConnection().CreateCommand();
        command.CommandText = $"EXPLAIN QUERY PLAN {sql}";
        bind?.Invoke(command);
        command.UseNamedPlaceholders();
        var details = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            details.Add(Convert.ToString(reader["detail"]) ?? string.Empty);
        }

        lock (Plans)
        {
            while (Plans.Count >= MaxPlans)
            {
                Plans.Dequeue();
            }

            Plans.Enqueue($"{label}: {string.Join(" | ", details)}");
        }
    }

    public static void Persist()
    {
        if (!Enabled)
        {
            return;
        }

        var snapshot = Snapshot();
        if (snapshot.Count == 0)
        {
            return;
        }

        var path = Environment.GetEnvironmentVariable("ECHO_CORE_QUERY_PLAN_PATH");
        if (!string.IsNullOrWhiteSpace(path))
        {
            File.WriteAllText(path, string.Join('\n', snapshot) + "\n");
        }

        Console.Error.WriteLine($"[echo-core-query-plan] {snapshot[^1]}");
    }
}
