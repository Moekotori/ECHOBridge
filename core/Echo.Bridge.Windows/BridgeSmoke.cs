using System.Diagnostics;
using System.Drawing.Imaging;
using System.Text.Json;
using Echo.Bridge.Library;

namespace Echo.Bridge.Windows;

/// <summary>Bounded local diagnostics; evidence contains no media titles, source paths or credentials.</summary>
internal static class BridgeSmoke
{
    internal static string Evidence(string directory)
    {
        var full = Path.GetFullPath(directory);
        var root = Path.Combine(Environment.CurrentDirectory, "misc") + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Evidence must be under workspace misc.");
        Directory.CreateDirectory(full); return full;
    }
    public static void Window(string directory, bool? dark = null)
    {
        directory = Evidence(directory);
        var clock = Stopwatch.StartNew();
        ApplicationConfiguration.Initialize();
        var sample = Path.Combine(directory, "ECHO", "echo-library.sqlite");
        Directory.CreateDirectory(Path.GetDirectoryName(sample)!); File.WriteAllText(sample, "UI fixture only");
        using var form = new BridgeForm(new BridgeSettings { DeviceName = "ECHO Bridge", SharingEnabled = false }, () => sample, dark);
        form.Shown += (_, _) => form.BeginInvoke((Action)(() =>
        {
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            bitmap.Save(Path.Combine(directory, "window.png"), ImageFormat.Png);
            Write(Path.Combine(directory, "window.json"), new { windowShown = form.Visible, elapsedMs = clock.ElapsedMilliseconds,
                privateBytes = Process.GetCurrentProcess().PrivateMemorySize64, steamLoaded = SteamLoaded() });
            form.CloseForSmoke();
        }));
        Application.Run(form);
    }
    public static async Task Library(string database, string directory)
    {
        directory = Evidence(directory);
        var clock = Stopwatch.StartNew();
        try
        {
            var automatic = database == "auto";
            if (automatic) database = BridgeProfileDiscovery.Automatic() ?? throw new InvalidOperationException();
            await using var session = await BridgeSession.StartAsync(database, "bridge-" + Guid.NewGuid().ToString("N"), "Bridge verification", 0);
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            client.DefaultRequestHeaders.Add("X-ECHO-Link-Direct", "1"); client.DefaultRequestHeaders.Add("X-ECHO-Link-Version", "1");
            using var catalog = JsonDocument.Parse(await client.GetStringAsync($"http://127.0.0.1:{session.Server.Port}/echo-link/peer/v1/catalog?kind=songs&page=1"));
            Write(Path.Combine(directory, "library.json"), new { ok = true, automatic, elapsedMs = clock.ElapsedMilliseconds,
                pageCount = catalog.RootElement.GetProperty("catalog").GetProperty("entries").GetArrayLength(),
                privateBytes = Process.GetCurrentProcess().PrivateMemorySize64, steamLoaded = SteamLoaded(), interfaces = session.Advertiser.InterfaceCount });
        }
        catch
        {
            Write(Path.Combine(directory, "library.json"), new { ok = false, code = "library_smoke_failed" });
            Environment.ExitCode = 1;
        }
    }
    public static async Task Discovery(string database, string directory, string? appData = null)
    {
        directory = Evidence(directory);
        try
        {
            if (appData is not null) appData = Evidence(appData);
            if (database == "auto") database = BridgeProfileDiscovery.Automatic(appData) ?? throw new InvalidOperationException();
            var id = "bridge-" + Guid.NewGuid().ToString("N");
            await using (var session = await BridgeSession.StartAsync(database, id, "Bridge verification", 0))
            {
            Write(Path.Combine(directory, "host-ready.json"), new { deviceId = id, interfaces = session.Advertiser.InterfaceCount, port = session.Server.Port });
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(35));
                while (!File.Exists(Path.Combine(directory, "host-stop"))) await Task.Delay(100, deadline.Token);
            }
            Write(Path.Combine(directory, "host-stopped.json"), new { ok = true, steamLoaded = SteamLoaded(),
                privateBytes = Process.GetCurrentProcess().PrivateMemorySize64 });
        }
        catch { Write(Path.Combine(directory, "host-stopped.json"), new { ok = false }); Environment.ExitCode = 1; }
    }
    private static bool SteamLoaded() => Process.GetCurrentProcess().Modules.Cast<ProcessModule>()
        .Any(module => module.ModuleName.Contains("steam_api", StringComparison.OrdinalIgnoreCase));
    private static void Write(string path, object value) => File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(value));
}
