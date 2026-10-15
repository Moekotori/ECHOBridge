using Echo.Bridge.Library;

namespace Echo.Bridge.Windows;

internal sealed class BridgeForm : Form
{
    private readonly BridgeSettings settings;
    private readonly BridgeRuntime runtime = new();
    private readonly BridgeView view;
    private readonly NotifyIcon tray;
    private readonly Func<string?> discover;
    private readonly BridgeStartupRegistration startup;
    private readonly Action<BridgeSettings> persistSettings;
    private readonly System.Windows.Forms.Timer networkStatus = new() { Interval = 2000 };
    private string? databasePath;
    private bool? displayedAutomatic;
    private int interfaces = -1;
    private bool busy, exiting, initialSharingApplied, updatingStartup;

    public BridgeForm(BridgeSettings settings, Func<string?>? discover = null, bool? dark = null,
        BridgeStartupRegistration? startup = null, Action<BridgeSettings>? persistSettings = null)
    {
        this.settings = settings; this.discover = discover ?? (() => BridgeProfileDiscovery.Automatic());
        this.startup = startup ?? new BridgeStartupRegistration();
        this.persistSettings = persistSettings ?? (value => value.Save());
        var theme = BridgeTheme.Resolve(dark);
        Text = "ECHO Bridge"; Font = new Font("Microsoft YaHei UI", 9); AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(440, 260); MinimumSize = new Size(456, 299); StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false; BackColor = theme.Background;
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application;
        view = new BridgeView(theme); Controls.Add(view);
        var menu = new ContextMenuStrip(); menu.Items.Add("打开 ECHO Bridge", null, (_, _) => Restore());
        menu.Items.Add("退出", null, async (_, _) => await Exit());
        tray = new NotifyIcon { Icon = Icon, Text = "ECHO Bridge", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => Restore();
        ResolveLibrary(); view.DeviceName.Text = settings.DeviceName;
        RefreshStartup(); view.StartAtLogin.CheckedChanged += (_, _) => ChangeStartup();
        view.Browse.Click += (_, _) => SelectLibrary();
        view.Automatic.Click += (_, _) => { settings.UseAutomaticLibrary = true; ResolveLibrary(); SaveSelection(); };
        view.Share.Click += async (_, _) => await Toggle();
        Activated += (_, _) => { RefreshStartup(); if (!busy && runtime.Session is null && settings.UseAutomaticLibrary) ResolveLibrary(); };
        Shown += async (_, _) => await ApplyInitialSharing();
        networkStatus.Tick += (_, _) =>
        {
            if (runtime.Session is not null) UpdateSharingStatus();
            else if (Visible && !busy && settings.UseAutomaticLibrary) ResolveLibrary();
        };
    }
    private void RefreshStartup()
    {
        updatingStartup = true;
        try { view.StartAtLogin.Checked = startup.IsEnabled; view.StartAtLogin.Enabled = true; }
        catch { view.StartAtLogin.Enabled = false; }
        finally { updatingStartup = false; }
    }
    private void ChangeStartup()
    {
        if (updatingStartup) return;
        try { startup.SetEnabled(view.StartAtLogin.Checked); }
        catch
        {
            RefreshStartup();
            MessageBox.Show(this, "无法更改开机自启动，请确认当前用户允许修改启动项后重试。",
                "ECHO Bridge", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
    private async Task ApplyInitialSharing()
    {
        if (initialSharingApplied) return;
        initialSharingApplied = true;
        if (settings.SharingEnabled) await Toggle();
    }
    public void StartInTray()
    {
        // Create only the message handle. A form-less ApplicationContext never shows the window.
        _ = Handle;
        BeginInvoke((Action)(async () => await ApplyInitialSharing()));
    }
    private void ResolveLibrary()
    {
        var next = settings.UseAutomaticLibrary ? discover() : settings.DatabasePath;
        if (next is not null && !File.Exists(next)) next = null;
        if (StringComparer.OrdinalIgnoreCase.Equals(databasePath, next) && displayedAutomatic == settings.UseAutomaticLibrary) return;
        databasePath = next; displayedAutomatic = settings.UseAutomaticLibrary;
        view.Library(databasePath, settings.UseAutomaticLibrary);
    }
    private void SaveSelection()
    {
        try { settings.DatabasePath = databasePath; persistSettings(settings); }
        catch { view.Status("无法保存选择", "当前选择仍可共享；下次打开时将重新查找曲库。", warning: true); }
    }
    public void Restore()
    {
        if (IsDisposed || exiting) return;
        if (InvokeRequired) { BeginInvoke((Action)Restore); return; }
        Show(); WindowState = FormWindowState.Normal; Activate();
    }
    internal void CloseForSmoke() { exiting = true; Close(); }
    internal string? SelectedDatabasePath => databasePath;
    internal CheckBox StartupToggle => view.StartAtLogin;
    internal int? SharingPort => runtime.Session?.Server.Port;
    internal Button SharingButton => view.Share;
    private void SelectLibrary()
    {
        using var dialog = new OpenFileDialog { Title = "选择 ECHO 已有曲库", Filter = "ECHO 曲库 (*.sqlite)|*.sqlite", CheckFileExists = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        settings.UseAutomaticLibrary = false; settings.DatabasePath = dialog.FileName; ResolveLibrary(); SaveSelection();
    }
    private async Task Toggle()
    {
        if (busy || exiting) return;
        busy = true; view.Enable(false, runtime.Session is not null);
        try
        {
            if (runtime.Session is not null)
            {
                networkStatus.Stop(); await runtime.Stop(); settings.SharingEnabled = false; persistSettings(settings);
                view.Status("共享已停止", "曲库保留在 ECHO 中，可随时重新开启共享。"); view.Share.Text = "开启共享";
            }
            else
            {
                ResolveLibrary();
                if (databasePath is null) { view.Status("未找到曲库", "先用 ECHO 加载曲库，或选择已有的 .sqlite 文件。", warning: true); return; }
                settings.DatabasePath = databasePath; settings.DeviceName = view.DeviceName.Text.Trim();
                if (settings.DeviceName.Length == 0) settings.DeviceName = "ECHO Bridge";
                view.Status("正在接入曲库", "准备只读连接与局域网广播…");
                await runtime.Start(settings);
                try { settings.SharingEnabled = true; persistSettings(settings); }
                catch { await runtime.Stop(); throw; }
                interfaces = -1; UpdateSharingStatus(); networkStatus.Start(); view.Share.Text = "停止共享";
            }
        }
        catch
        {
            view.Status("接入失败", "请确认曲库可读且与当前 ECHO 版本兼容，然后重试。", warning: true);
            view.Share.Text = runtime.Session is null ? "开启共享" : "停止共享";
        }
        finally
        {
            busy = false;
            if (!exiting) { view.Enable(true, runtime.Session is not null); UpdateStateTimer(); }
        }
    }
    private void UpdateSharingStatus()
    {
        if (runtime.Session is not { } session || interfaces == session.Advertiser.InterfaceCount) return;
        interfaces = session.Advertiser.InterfaceCount;
        view.Status(interfaces == 0 ? "等待局域网连接" : "共享已开启",
            interfaces == 0 ? "连接 Wi-Fi 或有线网络后自动恢复。" : "已广播曲库，可通过 ECHO Link 连接。",
            active: interfaces > 0, warning: interfaces == 0);
        tray.Text = interfaces == 0 ? "ECHO Bridge · 等待网络" : "ECHO Bridge · 共享中";
    }
    private async Task Exit()
    {
        if (exiting) return;
        exiting = true; networkStatus.Stop(); view.Enable(false, runtime.Session is not null);
        try { await runtime.Stop(); Close(); }
        catch { exiting = false; view.Status("暂时无法退出", "服务尚未完成关闭，请稍后重试。", warning: true); view.Enable(true, runtime.Session is not null); }
    }
    protected override void OnFormClosing(FormClosingEventArgs args)
    {
        if (!exiting && args.CloseReason == CloseReason.UserClosing) { args.Cancel = true; Hide(); }
        base.OnFormClosing(args);
    }
    protected override void OnLoad(EventArgs args)
    {
        base.OnLoad(args); view.PerformLayout();
        var available = Screen.FromControl(this).WorkingArea.Height - (Height - ClientSize.Height) - 32;
        var height = Math.Min(available, Math.Max(ClientSize.Height, view.ContentHeight));
        MinimumSize = new Size(MinimumSize.Width, Math.Min(MinimumSize.Height, height + Height - ClientSize.Height));
        ClientSize = new Size(ClientSize.Width, height);
        UpdateStateTimer();
    }
    private void UpdateStateTimer()
    {
        networkStatus.Interval = runtime.Session is null ? 5000 : 2000;
        if (!exiting && (Visible || runtime.Session is not null)) networkStatus.Start();
        else networkStatus.Stop();
    }
    protected override void OnVisibleChanged(EventArgs args)
    {
        base.OnVisibleChanged(args); UpdateStateTimer();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { networkStatus.Dispose(); tray.Visible = false; tray.ContextMenuStrip?.Dispose(); tray.Dispose(); }
        base.Dispose(disposing);
    }
}
