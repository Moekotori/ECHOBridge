using System.Diagnostics;
using System.Text.Json;

namespace Echo.Bridge.Windows.Updates;

internal sealed class BridgeUpdateJob
{
    public string Target { get; set; } = "";
    public string OldVersion { get; set; } = "";
    public string Version { get; set; } = "";
    public int OldPid { get; set; }
    public long OldStarted { get; set; }
    public int HelperPid { get; set; }
    public long HelperStarted { get; set; }
    public bool StartInTray { get; set; }
    public bool StartupConfirmed { get; set; }
    public string? Backup { get; set; }
}

internal static class BridgeUpdateHandoff
{
    internal static string Cache => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ECHO Bridge", "updates");
    internal static Version Current => BridgeReleaseClient.Normalize(typeof(BridgeUpdateHandoff).Assembly.GetName().Version!);
    private const string Owner = "ECHO Bridge update cache v1";
    internal static string Pending(string? cache = null) => Path.Combine(cache ?? Cache, "pending");

    public static string CreateStage()
    {
        var stage = Pending();
        if (Directory.Exists(stage))
        {
            if (File.ReadAllText(Path.Combine(stage, "owner")) != Owner) throw new InvalidDataException("Unknown Bridge update cache.");
            if (File.Exists(Path.Combine(stage, "job.json")))
            {
                var job = Read(Path.Combine(stage, "job.json"));
                using var process = Find(job.HelperPid, job.HelperStarted);
                if (process is not null || job.Backup is not null && Directory.Exists(job.Backup))
                    throw new IOException("A Bridge update or recovery is already pending.");
            }
            Directory.Delete(stage, recursive: true);
        }
        Directory.CreateDirectory(stage); File.WriteAllText(Path.Combine(stage, "owner"), Owner); return stage;
    }

    internal static BridgeUpdateJob Read(string file, string? diagnosticCache = null)
    {
        if (!Path.GetFullPath(file).Equals(Path.Combine(Pending(diagnosticCache), "job.json"), StringComparison.OrdinalIgnoreCase)
            || new FileInfo(file).Length > 65536) throw new InvalidDataException("Invalid Bridge update job location.");
        var job = JsonSerializer.Deserialize<BridgeUpdateJob>(File.ReadAllBytes(file))!;
        var target = Path.GetFullPath(job.Target);
        if (target == Path.GetPathRoot(target) || !Path.IsPathFullyQualified(job.Target)
            || diagnosticCache is not null && !target.StartsWith(Path.GetFullPath(diagnosticCache) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Invalid Bridge application location.");
        if (job.Backup is not null && (Path.GetDirectoryName(job.Backup) != target
                || !Path.GetFileName(job.Backup).StartsWith(".echo-bridge-backup-", StringComparison.Ordinal)))
            throw new InvalidDataException("Invalid Bridge recovery location.");
        return job;
    }

    internal static void Write(string file, BridgeUpdateJob job) => File.WriteAllBytes(file, JsonSerializer.SerializeToUtf8Bytes(job));
    private static Process? Find(int pid, long started)
    {
        if (pid <= 0) return null;
        try
        {
            var process = Process.GetProcessById(pid);
            if (process.StartTime.ToUniversalTime().Ticks == started) return process;
            process.Dispose(); throw new IOException("Bridge process identity changed.");
        }
        catch (ArgumentException) { return null; } // Process has already exited.
    }

    public static string Prepare(string stage, bool startInTray)
    {
        var target = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        BridgeUpdateManifest.Validate(target, Current);
        // Check actual write access before asking the running sharing session to exit.
        var probe = Path.Combine(target, ".echo-bridge-probe-" + Guid.NewGuid().ToString("N"));
        using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
        File.Delete(probe);
        var next = BridgeUpdateManifest.Validate(Path.Combine(stage, "package"),
            Version.Parse(File.ReadAllText(Path.Combine(stage, "version"))));
        var executableVersion = FileVersionInfo.GetVersionInfo(Path.Combine(stage, "package", "ECHO Bridge.exe")).FileVersion;
        if (!Version.TryParse(executableVersion, out var actualVersion) || BridgeReleaseClient.Normalize(actualVersion) != next.Version)
            throw new InvalidDataException("Bridge package executable version mismatch.");
        var process = Process.GetCurrentProcess();
        var file = Path.Combine(stage, "job.json");
        Write(file, new() { Target = target, OldVersion = Current.ToString(), Version = next.Version.ToString(),
            OldPid = process.Id, OldStarted = process.StartTime.ToUniversalTime().Ticks, StartInTray = startInTray });
        return file;
    }

    public static void Launch(string file)
    {
        var start = new ProcessStartInfo(Path.Combine(Path.GetDirectoryName(file)!, "package", "ECHO Bridge.exe"))
            { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(file)! };
        start.ArgumentList.Add("--apply-update"); start.ArgumentList.Add(file);
        using var process = Process.Start(start) ?? throw new IOException("Bridge updater did not start.");
    }

    public static void Apply(string file, string? diagnosticCache = null, TimeSpan? startupTimeout = null)
    {
        try
        {
            var job = Read(file, diagnosticCache);
            if (Version.Parse(job.Version) != Current) throw new InvalidDataException("Bridge updater version mismatch.");
            using (var old = Find(job.OldPid, job.OldStarted))
                if (old is not null && !old.WaitForExit(20000)) throw new IOException("Bridge did not finish exiting.");
            using var transaction = new BridgeUpdateInstaller(Path.Combine(Path.GetDirectoryName(file)!, "package"), job.Target,
                Version.Parse(job.OldVersion), Current);
            using var helper = Process.GetCurrentProcess();
            job.Backup = transaction.Backup; job.HelperPid = helper.Id; job.HelperStarted = helper.StartTime.ToUniversalTime().Ticks;
            Write(file, job);
            var restart = new ProcessStartInfo(Path.Combine(job.Target, "ECHO Bridge.exe"))
                { UseShellExecute = false, WorkingDirectory = diagnosticCache is null ? job.Target : Environment.CurrentDirectory };
            if (diagnosticCache is null) { restart.ArgumentList.Add("--updated"); restart.ArgumentList.Add(file); }
            else
            {
                restart.ArgumentList.Add("--updated-smoke"); restart.ArgumentList.Add(file); restart.ArgumentList.Add(diagnosticCache);
            }
            using var process = Process.Start(restart) ?? throw new IOException("Updated Bridge did not start.");
            try
            {
                BridgeUpdateStartup.Wait(process, file, Current, startupTimeout ?? TimeSpan.FromSeconds(20));
                job.StartupConfirmed = true; Write(file, job);
                transaction.Commit();
            }
            catch { BridgeUpdateStartup.Stop(process); throw; }
            if (diagnosticCache is not null)
                File.WriteAllBytes(Path.Combine(diagnosticCache, "apply-result.json"), JsonSerializer.SerializeToUtf8Bytes(new
                    { ok = true, restartedPid = process.Id, version = Current.ToString(), backupRetained = Directory.Exists(transaction.Backup) }));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or System.ComponentModel.Win32Exception or JsonException)
        {
            Environment.ExitCode = 1;
            if (diagnosticCache is not null)
            {
                File.WriteAllBytes(Path.Combine(diagnosticCache, "apply-result.json"), JsonSerializer.SerializeToUtf8Bytes(new
                    { ok = false, code = error.GetType().Name }));
                return;
            }
            MessageBox.Show("自动更新未完成。请重新打开原目录中的 ECHO Bridge；若恢复未完成，请保留原目录中的备份。",
                "ECHO Bridge", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static BridgeUpdateJob RestartJob(string file, string? diagnosticCache)
    {
        var job = Read(file, diagnosticCache);
        if (Version.Parse(job.Version) != Current || !Path.GetFullPath(job.Target).Equals(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Bridge restart does not match its update.");
        return job;
    }

    public static bool RestartInTray(string file)
    {
        var job = RestartJob(file, null);
        BridgeUpdateManifest.Validate(job.Target, Current);
        return job.StartInTray;
    }

    public static bool Complete(string file, string? diagnosticCache = null)
    {
        var job = RestartJob(file, diagnosticCache);
        BridgeUpdateStartup.Confirm(file, Current);
        using (var helper = Find(job.HelperPid, job.HelperStarted))
            if (helper is not null && !helper.WaitForExit(20000)) throw new IOException("Bridge updater is still running.");
        job = Read(file, diagnosticCache);
        if (!job.StartupConfirmed) throw new IOException("Bridge update was not committed.");
        BridgeUpdateManifest.Validate(job.Target, Current);
        if (job.Backup is not null) Directory.Delete(job.Backup, recursive: true);
        Directory.Delete(Path.GetDirectoryName(file)!, recursive: true);
        return job.StartInTray;
    }
}
