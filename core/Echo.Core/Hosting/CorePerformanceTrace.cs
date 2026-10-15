using System.Diagnostics;
using System.Globalization;

namespace Echo.Core.Hosting;

/// <summary>Opt-in scalar diagnostics: no query text, paths, tags, or retained samples.</summary>
internal static class CorePerformanceTrace
{
    public static bool Enabled { get; } = Environment.GetEnvironmentVariable("ECHO_CORE_PERF") == "1";
    public static long Start() => Enabled ? Stopwatch.GetTimestamp() : 0;

    public static void Complete(long id, string stage, long started, int bytes = 0)
    {
        if (!Enabled) return;
        var milliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture);
        Console.Error.WriteLine($"[core-perf] id={id} stage={stage} ms={milliseconds} bytes={bytes}");
    }
}
