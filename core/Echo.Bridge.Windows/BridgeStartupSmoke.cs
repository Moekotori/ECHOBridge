using System.Drawing.Imaging;
using System.Text.Json;
using Microsoft.Win32;

namespace Echo.Bridge.Windows;

/// <summary>Real checkbox/registry/lifecycle check against one disposable non-startup key.</summary>
internal static class BridgeStartupSmoke
{
    public static void Run(string directory)
    {
        directory = BridgeSmoke.Evidence(directory);
        var keyPath = @"Software\ECHO Bridge\Diagnostics\Startup-" + Guid.NewGuid().ToString("N");
        var realBefore = RealRegistration();
        try
        {
            ApplicationConfiguration.Initialize();
            using (var key = Registry.CurrentUser.CreateSubKey(keyPath)) key.SetValue("other-entry", "keep");
            var startup = new BridgeStartupRegistration(keyPath);
            using var form = new BridgeForm(new BridgeSettings { DeviceName = "ECHO Bridge", SharingEnabled = false }, () => null, startup: startup);
            using var context = new ApplicationContext();
            using var timer = new System.Windows.Forms.Timer { Interval = 100 };
            var stage = 0;
            bool hidden = false, initiallyDisabled = false, enabled = false, quoted = false;
            form.FormClosed += (_, _) => context.ExitThread();
            timer.Tick += (_, _) =>
            {
                try
                {
                    if (stage++ == 0)
                    {
                        hidden = !form.Visible;
                        initiallyDisabled = !startup.IsEnabled && !form.StartupToggle.Checked;
                        form.StartupToggle.Checked = true;
                        enabled = startup.IsEnabled;
                        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
                        quoted = key?.GetValue("ECHO Bridge") as string == "\"" + Environment.ProcessPath + "\" --startup";
                        form.Restore(); return;
                    }
                    timer.Stop();
                    var restored = form.Visible;
                    using (var bitmap = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                        bitmap.Save(Path.Combine(directory, "startup-window.png"), ImageFormat.Png);
                    }
                    form.StartupToggle.Checked = false;
                    var removed = !startup.IsEnabled;
                    using var remaining = Registry.CurrentUser.OpenSubKey(keyPath);
                    var keptOther = remaining?.GetValue("other-entry") as string == "keep";
                    form.Close(); var closeHides = !form.Visible;
                    var realUnchanged = realBefore == RealRegistration();
                    var ok = hidden && initiallyDisabled && enabled && quoted && restored && removed && keptOther && closeHides && realUnchanged;
                    File.WriteAllBytes(Path.Combine(directory, "startup.json"), JsonSerializer.SerializeToUtf8Bytes(new
                        { ok, hidden, initiallyDisabled, enabled, quoted, restored, removed, keptOther, closeHides, realUnchanged }));
                    if (!ok) Environment.ExitCode = 1;
                    form.CloseForSmoke();
                }
                catch
                {
                    timer.Stop(); Environment.ExitCode = 1;
                    File.WriteAllText(Path.Combine(directory, "startup.json"), "{\"ok\":false}");
                    form.CloseForSmoke();
                }
            };
            form.StartInTray(); timer.Start(); Application.Run(context);
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false); }
    }
    private static string? RealRegistration()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        return key?.GetValue("ECHO Bridge", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
    }
}
