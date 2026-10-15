using System.Diagnostics;
using System.Text.Json;
using Echo.Bridge.Library;
using Echo.Core.Protocol;

namespace Echo.Bridge.Windows;

/// <summary>Actual window selection against bounded misc fixtures; no production settings are saved.</summary>
internal static class BridgeLibrarySelectionSmoke
{
    public static void Run(string appData, string expected, string directory, string mode)
    {
        appData = BridgeSmoke.Evidence(appData); directory = BridgeSmoke.Evidence(directory);
        expected = Path.GetFullPath(expected);
        var workspaceMisc = Path.Combine(Environment.CurrentDirectory, "misc") + Path.DirectorySeparatorChar;
        if (!expected.StartsWith(workspaceMisc, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Fixture must be under misc.");
        ApplicationConfiguration.Initialize();
        var settings = new BridgeSettings { DeviceName = "ECHO Bridge", SharingEnabled = false, UseAutomaticLibrary = mode != "manual", DatabasePath = expected };
        using var form = new BridgeForm(settings, () => BridgeProfileDiscovery.Automatic(appData));
        using var timer = new System.Windows.Forms.Timer { Interval = 100 };
        var clock = Stopwatch.StartNew();
        form.Shown += (_, _) =>
        {
            Write(directory, "selection-ready.json", new { selected = form.SelectedDatabasePath is not null,
                initiallyMatched = Matches(form.SelectedDatabasePath, expected), automatic = settings.UseAutomaticLibrary });
            timer.Start();
        };
        timer.Tick += (_, _) =>
        {
            var match = Matches(form.SelectedDatabasePath, expected);
            if (!match && mode == "watch" && clock.Elapsed < TimeSpan.FromSeconds(20)) return;
            timer.Stop();
            var readable = false;
            try
            {
                if (match) new BridgeLibraryReader(BridgeProfile.Open(form.SelectedDatabasePath!))
                    .Read("getTracksByIds", new CoreRequestParams { TrackIds = ["selection-probe-" + Guid.NewGuid().ToString("N")] });
                readable = match;
            }
            catch { }
            Write(directory, "selection.json", new { matched = match, readable, automatic = settings.UseAutomaticLibrary,
                elapsedMs = clock.ElapsedMilliseconds });
            if (!readable) Environment.ExitCode = 1;
            form.CloseForSmoke();
        };
        Application.Run(form);
    }
    private static bool Matches(string? actual, string expected) => StringComparer.OrdinalIgnoreCase.Equals(actual, expected);
    private static void Write(string directory, string name, object value) => File.WriteAllBytes(Path.Combine(directory, name), JsonSerializer.SerializeToUtf8Bytes(value));
}
