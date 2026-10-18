using System.Diagnostics;
using System.Text.Json;
using Echo.Bridge.Library;

namespace Echo.Bridge.Windows;

/// <summary>Real window/tray recovery with synthetic discovery and no user settings or startup writes.</summary>
internal static class BridgeSharingRecoverySmoke
{
    public static void Run(string database, string directory, string mode)
    {
        directory = BridgeSmoke.Evidence(directory);
        database = Path.GetFullPath(database);
        if (!database.StartsWith(Path.Combine(Environment.CurrentDirectory, "misc") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || mode is not ("window" or "tray" or "off")) throw new ArgumentException("Invalid recovery fixture.");
        ApplicationConfiguration.Initialize();
        string? discovered = null;
        var settings = new BridgeSettings { SharingEnabled=mode!="off", AutomaticUpdates=false, DeviceName="Bridge recovery verification" };
        using var form = new BridgeForm(settings, () => discovered, startup:new BridgeStartupRegistration(
            @"Software\ECHO Bridge\Diagnostics\ReadOnly-"+Guid.NewGuid().ToString("N")), persistSettings:_=>{});
        using var context = new ApplicationContext();
        using var timer = new System.Windows.Forms.Timer { Interval=100 };
        var clock = Stopwatch.StartNew();
        timer.Tick += async (_,_) =>
        {
            if (clock.ElapsedMilliseconds >= 300) discovered=database;
            if (clock.ElapsedMilliseconds < 5500 || form.SharingPort is null && mode!="off" && clock.ElapsedMilliseconds < 14000) return;
            timer.Stop();
            var selected=StringComparer.OrdinalIgnoreCase.Equals(form.SelectedDatabasePath,database);
            var sharing=form.SharingPort.HasValue;
            var visibilityKept=mode=="window" ? form.Visible : !form.Visible;
            var ok=mode=="off" ? !sharing && !settings.SharingEnabled : selected && sharing;
            if (sharing)
            {
                using var http=new HttpClient { Timeout=TimeSpan.FromSeconds(3) };
                http.DefaultRequestHeaders.Add("X-ECHO-Link-Direct","1"); http.DefaultRequestHeaders.Add("X-ECHO-Link-Version","1");
                using var response=await http.GetAsync($"http://127.0.0.1:{form.SharingPort}/echo-link/peer/v1/device");
                ok &= response.IsSuccessStatusCode;
                if (!form.Visible) form.Restore();
                form.SharingButton.PerformClick();
                while (form.SharingPort is not null && clock.ElapsedMilliseconds<16000) await Task.Delay(50);
                ok &= form.SharingPort is null && !settings.SharingEnabled;
            }
            File.WriteAllBytes(Path.Combine(directory,"recovery.json"),JsonSerializer.SerializeToUtf8Bytes(new { ok,mode,selected,sharing,
                stopped=form.SharingPort is null,visibilityKept }));
            if (!ok) Environment.ExitCode=1;
            form.CloseForSmoke(); context.ExitThread();
        };
        timer.Start();
        if (mode=="window") Application.Run(form);
        else { form.StartInTray(); Application.Run(context); }
    }
}
