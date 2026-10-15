using System.Diagnostics;
using System.Text.Json;
using Echo.Bridge.Library;

namespace Echo.Bridge.Windows;

/// <summary>Real default window and stop button, with only misc-backed profile/settings.</summary>
internal static class BridgeSharingSmoke
{
    public static void Run(string database, string directory, string? appData = null)
    {
        directory = BridgeSmoke.Evidence(directory);
        if (appData is not null) appData = BridgeSmoke.Evidence(appData);
        if (database != "auto")
        {
            database = Path.GetFullPath(database);
            if (!database.StartsWith(Path.Combine(Environment.CurrentDirectory, "misc") + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Fixture must be under misc.");
        }
        var config = Path.Combine(directory, "bridge-settings.json");
        var settings = BridgeSettings.LoadFile(config);
        settings.DeviceName = "Bridge verification";
        ApplicationConfiguration.Initialize();
        using var form = new BridgeForm(settings,
            () => database == "auto" ? BridgeProfileDiscovery.Automatic(appData) : database,
            persistSettings: value => value.SaveFile(config));
        using var timer = new System.Windows.Forms.Timer { Interval = 100 };
        var clock = Stopwatch.StartNew();
        bool ready = false, stopClicked = false;
        form.Shown += (_, _) => timer.Start();
        timer.Tick += (_, _) =>
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(50))
            {
                // Use the real button to revoke media/discovery before ending this owned session.
                if (form.SharingPort is not null && form.SharingButton.Enabled && !stopClicked)
                { form.SharingButton.PerformClick(); stopClicked = true; return; }
                if (form.SharingPort is not null) return;
                Finish(false); return;
            }
            if (!ready && form.SharingPort is { } port && form.SharingButton.Enabled)
            {
                Write("host-ready.json", new { deviceId = settings.DeviceId, port, automaticSharing = settings.SharingEnabled,
                    windowVisible = form.Visible, automaticLibrary = settings.UseAutomaticLibrary });
                ready = true;
            }
            if (ready && !stopClicked && File.Exists(Path.Combine(directory, "host-stop")))
            {
                form.SharingButton.PerformClick(); stopClicked = true;
            }
            if (stopClicked && form.SharingPort is null && form.SharingButton.Enabled)
                Finish(!BridgeSettings.LoadFile(config).SharingEnabled);
        };
        void Write(string name, object value)
        {
            var path = Path.Combine(directory, name);
            File.WriteAllBytes(path + ".tmp", JsonSerializer.SerializeToUtf8Bytes(value));
            File.Move(path + ".tmp", path, overwrite: true);
        }
        void Finish(bool ok)
        {
            timer.Stop();
            Write("host-stopped.json", new { ok, stoppedByButton = stopClicked, closedPreferenceSaved = !settings.SharingEnabled,
                elapsedMs = clock.ElapsedMilliseconds });
            if (!ok) Environment.ExitCode = 1;
            form.CloseForSmoke();
        }
        Application.Run(form);
    }
}
