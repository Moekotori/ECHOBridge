namespace Echo.Bridge.Windows.Updates;

/// <summary>One update operation per window; automatic checks never restart a sharing session.</summary>
internal sealed class BridgeUpdateController : IDisposable
{
    private readonly Form form;
    private readonly CheckBox automatic;
    private readonly NotifyIcon tray;
    private readonly BridgeSettings settings;
    private readonly Action<BridgeSettings> save;
    private readonly Func<Action, Task> exit;
    private readonly HttpClient http = BridgeReleaseClient.CreateHttpClient();
    private readonly CancellationTokenSource lifetime = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 24 * 60 * 60 * 1000 };
    private readonly ToolStripMenuItem action = new("检查更新");
    private readonly BridgeReleaseClient client;
    private BridgeRelease? available;
    private bool busy, started, changing, disposed;
    private CancellationTokenSource? automaticCheck;

    public BridgeUpdateController(Form form, CheckBox automatic, NotifyIcon tray, BridgeSettings settings,
        Action<BridgeSettings> save, Func<Action, Task> exit)
    {
        this.form = form; this.automatic = automatic; this.tray = tray; this.settings = settings; this.save = save; this.exit = exit;
        client = new(http, BridgeUpdateHandoff.Current);
        automatic.Checked = settings.AutomaticUpdates;
        automatic.CheckedChanged += Changed;
        action.Click += async (_, _) => { if (available is null) await Check(true); else await Install(); };
        tray.ContextMenuStrip!.Items.Insert(tray.ContextMenuStrip.Items.Count - 1, action);
        tray.BalloonTipClicked += BalloonClicked;
        timer.Tick += async (_, _) => await Check(false);
        form.Shown += (_, _) => Start();
    }

    public async void Start()
    {
        if (started) return;
        started = true;
        if (!settings.AutomaticUpdates) return;
        timer.Start();
        try { await Task.Delay(TimeSpan.FromSeconds(10), lifetime.Token); if (settings.AutomaticUpdates) await Check(false); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }

    private void Changed(object? sender, EventArgs args)
    {
        if (changing) return;
        var previous = settings.AutomaticUpdates;
        try { settings.AutomaticUpdates = automatic.Checked; save(settings); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            settings.AutomaticUpdates = previous; changing = true; automatic.Checked = previous; changing = false;
            MessageBox.Show(form, "无法保存自动检查更新设置。", "ECHO Bridge", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        if (settings.AutomaticUpdates) { timer.Start(); _ = Check(false); }
        else { timer.Stop(); automaticCheck?.Cancel(); }
    }

    private async Task Check(bool manual)
    {
        if (busy || lifetime.IsCancellationRequested) return;
        busy = true; action.Enabled = false;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        if (!manual) automaticCheck = cancellation;
        try
        {
            var release = await client.Check(cancellation.Token);
            if (cancellation.IsCancellationRequested) return;
            var changed = release?.Version != available?.Version;
            available = release; action.Text = release is null ? "检查更新" : "更新到 " + release.Version;
            if (release is null && manual)
                MessageBox.Show(form, "当前版本 " + BridgeUpdateHandoff.Current + " 已是最新版本。", "ECHO Bridge");
            if (release is not null && !manual && changed)
                tray.ShowBalloonTip(8000, "ECHO Bridge 有新版本", "发现 " + release.Version + "，点击下载并重启更新。", ToolTipIcon.Info);
        }
        catch (Exception error) when (Known(error))
        {
            if (manual && !lifetime.IsCancellationRequested)
                MessageBox.Show(form, "暂时无法检查 GitHub Release，请稍后从托盘重试。", "ECHO Bridge", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            if (automaticCheck == cancellation) automaticCheck = null;
            busy = false; if (!lifetime.IsCancellationRequested) action.Enabled = true;
        }
        if (manual && available is not null && !lifetime.IsCancellationRequested) await Install();
    }

    private async void BalloonClicked(object? sender, EventArgs args) => await Install();
    private async Task Install()
    {
        if (busy || available is null || lifetime.IsCancellationRequested) return;
        var release = available;
        if (MessageBox.Show(form, "下载并更新到 " + release.Version + "？更新将短暂停止共享并重新启动，保留曲库和设置。",
                "ECHO Bridge", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        busy = true; action.Enabled = false; action.Text = "正在下载更新…";
        string? stage = null; var launched = false;
        try
        {
            stage = await Task.Run(BridgeUpdateHandoff.CreateStage, lifetime.Token);
            var archive = Path.Combine(stage, "update.zip");
            await client.Download(release, archive, lifetime.Token);
            action.Text = "正在校验更新…";
            await Task.Run(() => BridgeUpdatePackage.Extract(archive, Path.Combine(stage, "package"), release), lifetime.Token);
            File.WriteAllText(Path.Combine(stage, "version"), release.Version.ToString());
            var job = await Task.Run(() => BridgeUpdateHandoff.Prepare(stage, !form.Visible), lifetime.Token);
            lifetime.Token.ThrowIfCancellationRequested();
            await exit(() => { BridgeUpdateHandoff.Launch(job); launched = true; });
        }
        catch (Exception error) when (Known(error))
        {
            if (!lifetime.IsCancellationRequested)
                MessageBox.Show(form, "更新未完成，当前版本已保留。请确认便携目录可写，并稍后从托盘重试。", "ECHO Bridge", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            if (!launched && stage is not null)
            {
                try { Directory.Delete(stage, recursive: true); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                { if (!lifetime.IsCancellationRequested) MessageBox.Show(form, "更新临时文件未能清理，下次更新前会重新检查。", "ECHO Bridge", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
            busy = false;
            if (!lifetime.IsCancellationRequested) { action.Text = "更新到 " + release.Version; action.Enabled = true; }
        }
    }

    private static bool Known(Exception error) => error is IOException or UnauthorizedAccessException or HttpRequestException
        or System.Text.Json.JsonException or OperationCanceledException or FormatException or InvalidOperationException
        or KeyNotFoundException or System.ComponentModel.Win32Exception;

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        lifetime.Cancel(); timer.Dispose(); http.Dispose(); automatic.CheckedChanged -= Changed;
        tray.BalloonTipClicked -= BalloonClicked; action.Dispose(); lifetime.Dispose();
    }
}
