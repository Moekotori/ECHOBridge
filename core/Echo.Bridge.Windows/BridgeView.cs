namespace Echo.Bridge.Windows;

/// <summary>Compact native presentation; sharing and library ownership stay in the form/runtime.</summary>
internal sealed class BridgeView : UserControl
{
    public TextBox DeviceName { get; } = new() { BorderStyle = BorderStyle.FixedSingle, Dock = DockStyle.Fill, MaxLength = 80 };
    public Button Browse { get; }
    public Button Automatic { get; }
    public Button Share { get; }
    public CheckBox StartAtLogin { get; } = new() { Text = "开机自启动", AutoSize = true,
        AccessibleName = "开机自启动", Margin = new Padding(0, 14, 0, 0) };
    private readonly Label libraryTitle, libraryDetail, state, detail;
    private readonly BridgeTheme theme;
    private readonly List<Font> fonts = [];
    private readonly TableLayoutPanel layout;
    internal int ContentHeight => layout.Bottom + Padding.Bottom;

    public BridgeView(BridgeTheme theme)
    {
        this.theme = theme; AutoScaleMode = AutoScaleMode.Inherit;
        Dock = DockStyle.Fill; BackColor = theme.Background; ForeColor = theme.Text; AutoScroll = true;
        Padding = new Padding(20, 18, 20, 16); Font = OwnedFont(9);
        Browse = Button("更改…", 76); Automatic = Button("恢复自动", 90); Share = Button("开启共享", 100);
        layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var caption = Label("曲库", 9, theme.Muted); AddWide(caption);
        libraryTitle = Label("正在查找曲库…", 9, theme.Text);
        libraryTitle.AutoSize = false; libraryTitle.Dock = DockStyle.Fill; libraryTitle.AutoEllipsis = true;
        libraryTitle.TextAlign = ContentAlignment.MiddleLeft; libraryTitle.Height = 28;
        var libraryRow = layout.RowCount++;
        layout.Controls.Add(libraryTitle, 0, libraryRow); layout.Controls.Add(Browse, 1, libraryRow);
        libraryDetail = Label("", 8.5f, theme.Muted); libraryDetail.AutoSize = false; libraryDetail.Height = 20;
        libraryDetail.Dock = DockStyle.Fill; libraryDetail.AutoEllipsis = true; AddWide(libraryDetail);
        var deviceLabel = Label("设备名称", 9, theme.Muted); deviceLabel.Margin = new Padding(0, 12, 0, 5); AddWide(deviceLabel);
        DeviceName.BackColor = theme.Field; DeviceName.ForeColor = theme.Text; DeviceName.AccessibleName = "共享设备名称";
        DeviceName.Margin = Padding.Empty; AddWide(DeviceName);
        var separator = new Panel { Height = 1, Dock = DockStyle.Top, BackColor = theme.Outline,
            Margin = new Padding(0, 18, 0, 12) }; AddWide(separator);
        state = Label("未共享", 9, theme.Text, true);
        state.AutoSize = false; state.Height = 28; state.Dock = DockStyle.Fill;
        state.TextAlign = ContentAlignment.MiddleLeft; state.Margin = Padding.Empty;
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
            Margin = Padding.Empty };
        Share.Margin = Padding.Empty; Automatic.Margin = new Padding(0, 0, 8, 0);
        actions.Controls.Add(Share); actions.Controls.Add(Automatic);
        var statusRow = layout.RowCount++;
        layout.Controls.Add(state, 0, statusRow); layout.Controls.Add(actions, 1, statusRow);
        detail = Label("同一局域网内，通过 ECHO Link 连接。", 8.5f, theme.Muted);
        detail.MaximumSize = new Size(400, 0); detail.Margin = new Padding(0, 6, 0, 0); AddWide(detail);
        StartAtLogin.ForeColor = theme.Text; StartAtLogin.BackColor = theme.Background; AddWide(StartAtLogin);
        var footer = Label("关闭窗口后继续在托盘运行", 8, theme.Muted);
        footer.Margin = new Padding(0, 16, 0, 0); AddWide(footer); Controls.Add(layout);
    }
    private void AddWide(Control control)
    {
        layout.Controls.Add(control, 0, layout.RowCount++); layout.SetColumnSpan(control, 2);
    }
    private Button Button(string text, int width)
    {
        var button = new Button { Text = text, Width = width, Height = 28,
            FlatStyle = theme.Background.GetBrightness() > .5f ? FlatStyle.System : FlatStyle.Flat,
            BackColor = theme.Field, ForeColor = theme.Text, Margin = Padding.Empty };
        button.FlatAppearance.BorderColor = theme.Outline; return button;
    }
    private Label Label(string text, float size, Color color, bool bold = false) => new()
    {
        Text = text, AutoSize = true, ForeColor = color, BackColor = Color.Transparent,
        Font = OwnedFont(size, bold), Margin = new Padding(0, 0, 0, 4)
    };
    private Font OwnedFont(float size, bool bold = false)
    {
        var font = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular); fonts.Add(font); return font;
    }
    public void Library(string? path, bool automatic)
    {
        libraryTitle.Text = path is null ? "未找到 ECHO 曲库" : Path.GetFileName(path);
        libraryDetail.Text = path is null ? "先用 ECHO 导入音乐，或选择已有曲库。"
            : automatic ? "自动读取 ECHO 当前曲库" : "使用手动选择的曲库";
        Automatic.Visible = !automatic;
    }
    public void Status(string title, string description, bool active = false, bool warning = false)
    {
        state.Text = title; detail.Text = description;
        state.ForeColor = warning ? theme.Warning : active ? theme.Positive : theme.Text;
    }
    public void Enable(bool enabled, bool sharing)
    {
        Share.Enabled = enabled; Browse.Enabled = Automatic.Enabled = DeviceName.Enabled = enabled && !sharing;
    }
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) { foreach (var font in fonts) font.Dispose(); fonts.Clear(); }
    }
}
