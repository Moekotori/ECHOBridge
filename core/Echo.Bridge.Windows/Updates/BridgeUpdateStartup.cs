using System.Diagnostics;
using System.Text.Json;

namespace Echo.Bridge.Windows.Updates;

/// <summary>The updater owns rollback until the exact restarted process confirms initialization.</summary>
internal static class BridgeUpdateStartup
{
    private sealed record Receipt(int Pid, long Started, string Version);
    private static string ReceiptPath(string job) => Path.Combine(Path.GetDirectoryName(job)!, "startup-ready.json");

    public static void Confirm(string job, Version version)
    {
        using var process = Process.GetCurrentProcess();
        var receipt = new Receipt(process.Id, process.StartTime.ToUniversalTime().Ticks, version.ToString());
        var file = ReceiptPath(job);
        var temporary = file + ".tmp";
        File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(receipt));
        File.Move(temporary, file);
    }

    public static void Wait(Process process, string job, Version version, TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();
        var file = ReceiptPath(job);
        while (clock.Elapsed < timeout)
        {
            if (process.HasExited) throw new IOException("Updated Bridge exited before confirming startup.");
            if (File.Exists(file))
            {
                if (new FileInfo(file).Length > 512) throw new InvalidDataException("Invalid Bridge startup confirmation.");
                var receipt = JsonSerializer.Deserialize<Receipt>(File.ReadAllBytes(file));
                if (receipt is null || receipt.Pid != process.Id || receipt.Started != process.StartTime.ToUniversalTime().Ticks
                    || receipt.Version != version.ToString()) throw new InvalidDataException("Bridge startup process does not match.");
                return;
            }
            process.WaitForExit((int)Math.Clamp((timeout - clock.Elapsed).TotalMilliseconds, 1, 100));
        }
        throw new IOException("Updated Bridge did not confirm startup in time.");
    }

    public static void Stop(Process process)
    {
        if (process.HasExited) return;
        process.Kill(entireProcessTree: true);
        if (!process.WaitForExit(5000)) throw new IOException("Updated Bridge did not finish exiting.");
    }
}
