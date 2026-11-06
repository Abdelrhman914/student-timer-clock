namespace StudentTimerClock;

/// <summary>Big stopwatch window with named laps - like Hot Alarm Clock's stopwatch dialog.</summary>
public class StopwatchWindow : Form
{
    readonly StopwatchItem _s;
    readonly Panel _display = new() { Dock = DockStyle.Top };
    readonly Panel _lapDisplay = new();
    readonly Button _start = new(), _reset = new(), _lap = new();
    readonly ComboBox _lapName = new() { DropDownStyle = ComboBoxStyle.DropDown };
    readonly ListView _list = new() { View = View.Details, FullRowSelect = true, GridLines = true, HeaderStyle = ColumnHeaderStyle.Nonclickable };
    readonly System.Windows.Forms.Timer _tick = new() { Interval = 40 };
    int _lapCount = -1;

    public StopwatchWindow(StopwatchItem s)
    {
        _s = s;
        Text = $"{s.Name} - Stopwatch";
        Icon = Program.AppIcon;
        Font = Theme.Normal;
        BackColor = Color.White;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(Theme.S(470), Theme.S(620));
        MinimumSize = new Size(Theme.S(400), Theme.S(480));
        KeyPreview = true;

        _display.Height = Theme.S(96);
        _display.Paint += PaintDisplay;
        typeof(Panel).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(_display, true);
        typeof(Panel).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(_lapDisplay, true);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(Theme.S(10), 0, Theme.S(10), Theme.S(4)) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(50)));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(36)));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(40)));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // buttons row
        var btns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = new Padding(0) };
        for (int i = 0; i < 3; i++) btns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
        StyleBig(_start, "Start", "start"); StyleBig(_lap, "Lap", "lap"); StyleBig(_reset, "Reset", "reset");
        btns.Controls.Add(_start, 0, 0); btns.Controls.Add(_lap, 1, 0); btns.Controls.Add(_reset, 2, 0);
        _start.Click += (s2, e) => { AppState.Toggle(_s); UpdateButtons(); };
        _lap.Click += (s2, e) => DoLap();
        _reset.Click += (s2, e) =>
        {
            if (_s.Laps.Count > 0 && MessageBox.Show(this, "Reset the stopwatch and clear the lap list?\n(Your laps stay saved in Statistics.)", Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            AppState.Reset(_s); UpdateButtons(); RefreshLaps();
        };

        // current lap row
        var lapRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = new Padding(0) };
        lapRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        lapRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        lapRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var lbl = new Label { Text = "Current lap:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 6, 0) };
        _lapName.Dock = DockStyle.Fill; _lapName.Margin = new Padding(0, Theme.S(6), 6, 0);
        _lapName.Items.AddRange(_s.LapNames.Cast<object>().ToArray());
        _lapName.Text = _s.CurrentLapName;
        _lapName.TextChanged += (s2, e) => { _s.CurrentLapName = _lapName.Text; };
        _lapName.Leave += (s2, e) => AppState.Save();
        var del = new Button { Text = "✖ Delete", AutoSize = true, Height = Theme.S(26), ForeColor = Theme.Red, Margin = new Padding(0, Theme.S(4), 0, 0) };
        del.Click += (s2, e) =>
        {
            var n = _lapName.Text.Trim();
            if (_s.LapNames.Remove(n)) { _lapName.Items.Remove(n); _lapName.Text = _s.LapNames.FirstOrDefault() ?? ""; AppState.Save(); }
        };
        new ToolTip().SetToolTip(del, "Remove this name from the lap-name list");
        lapRow.Controls.Add(lbl, 0, 0); lapRow.Controls.Add(_lapName, 1, 0); lapRow.Controls.Add(del, 2, 0);

        _lapDisplay.Dock = DockStyle.Fill;
        _lapDisplay.Paint += (s2, e) =>
        {
            var r = _lapDisplay.ClientRectangle;
            SevenSegment.Draw(e.Graphics, Fmt.Time(_s.CurrentLap(DateTime.UtcNow), true, false), new RectangleF(r.X, r.Y + 4, r.Width, r.Height - 8), Color.FromArgb(40, 40, 40), Color.FromArgb(14, 0, 0, 0), smallTail: Fmt.Time(_s.CurrentLap(DateTime.UtcNow), true, false).Length - 3);
        };

        _list.Dock = DockStyle.Fill;
        _list.Columns.Add("#", Theme.S(34), HorizontalAlignment.Right);
        _list.Columns.Add("Lap", Theme.S(140));
        _list.Columns.Add("Time", Theme.S(105), HorizontalAlignment.Center);
        _list.Columns.Add("Total time", Theme.S(105), HorizontalAlignment.Center);
        var lm = new ContextMenuStrip();
        lm.Items.Add("Rename lap…", null, (s2, e) => RenameLap());
        lm.Items.Add("Delete lap", null, (s2, e) => DeleteLap());
        _list.ContextMenuStrip = lm;
        _list.DoubleClick += (s2, e) => RenameLap();

        layout.Controls.Add(btns, 0, 0);
        layout.Controls.Add(lapRow, 0, 1);
        layout.Controls.Add(_lapDisplay, 0, 2);
        layout.Controls.Add(_list, 0, 3);

        // bottom bar
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(Theme.S(8)), BackColor = Theme.HeaderBack };
        Button B(string t, string icon, Action a)
        {
            var b = new Button { Text = " " + t, AutoSize = true, Height = Theme.S(28), Image = Icons.Get(icon, Theme.S(16)), TextImageRelation = TextImageRelation.ImageBeforeText, Padding = new Padding(4, 0, 4, 0) };
            b.Click += (s2, e) => a();
            bottom.Controls.Add(b);
            return b;
        }
        B("Copy to Clipboard", "clipboard", CopyLaps);
        B("Report", "report", () => new LapReportForm(_s).Show(this));
        B("Float widget", "widget", () => AppState.RequestWidget(_s));
        B("Edit…", "edit", () => { Program.Main?.EditItem(_s); Text = $"{_s.Name} - Stopwatch"; ReloadNames(); });

        Controls.Add(layout);
        Controls.Add(_display);
        Controls.Add(bottom);

        _tick.Tick += (s2, e) =>
        {
            _display.Invalidate(); _lapDisplay.Invalidate();
            if (_s.Laps.Count != _lapCount) RefreshLaps();
            UpdateButtons();
        };
        _tick.Start();
        FormClosed += (s2, e) => { _tick.Stop(); AppState.Save(); };
        RefreshLaps();
        UpdateButtons();
    }

    void ReloadNames()
    {
        _lapName.Items.Clear();
        _lapName.Items.AddRange(_s.LapNames.Cast<object>().ToArray());
        _lapName.Text = _s.CurrentLapName;
    }

    static void StyleBig(Button b, string text, string icon)
    {
        b.Text = "  " + text;
        b.Dock = DockStyle.Fill;
        b.Margin = new Padding(3, 6, 3, 6);
        b.Font = Theme.UI(11f);
        b.Image = Icons.Get(icon, Theme.S(22));
        b.TextImageRelation = TextImageRelation.ImageBeforeText;
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderColor = Theme.Border;
        b.BackColor = Color.FromArgb(250, 250, 250);
    }

    void UpdateButtons()
    {
        string t = _s.IsRunning ? "  Pause" : _s.ElapsedBefore > 0 ? "  Resume" : "  Start";
        if (_start.Text != t) { _start.Text = t; _start.Image = Icons.Get(_s.IsRunning ? "pause" : "start", Theme.S(22)); }
        _lap.Enabled = _s.IsRunning;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_lapName.Focused) { base.OnKeyDown(e); return; }
        if (e.KeyCode == Keys.Space) { AppState.Toggle(_s); e.Handled = true; }
        else if (e.KeyCode == Keys.L || e.KeyCode == Keys.Enter) { DoLap(); e.Handled = true; }
        else if (e.KeyCode == Keys.R) { _reset.PerformClick(); e.Handled = true; }
        base.OnKeyDown(e);
    }

    void DoLap()
    {
        if (!_s.IsRunning) return;
        var n = _lapName.Text.Trim();
        AppState.Lap(_s, n);
        if (n != "" && !_lapName.Items.Contains(n)) _lapName.Items.Add(n);
        // auto-advance to the next name (handy for drills with several people)
        if (_s.LapNames.Count > 1 && n != "")
        {
            int i = _s.LapNames.IndexOf(n);
            if (i >= 0) _lapName.Text = _s.LapNames[(i + 1) % _s.LapNames.Count];
        }
        RefreshLaps();
    }

    void PaintDisplay(object sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        var r = _display.ClientRectangle;
        g.Clear(Color.White);
        string t = Fmt.Time(_s.Elapsed(DateTime.UtcNow), true, false);
        if (t.Length < 8) t = "00:" + t;
        var inner = new RectangleF(r.X + Theme.S(16), r.Y + Theme.S(10), r.Width - Theme.S(32), r.Height - Theme.S(18));
        SevenSegment.Draw(g, t, inner, _s.IsRunning ? Color.FromArgb(30, 30, 30) : Color.FromArgb(70, 70, 70), Color.FromArgb(14, 0, 0, 0), smallTail: -1);
        using var p = new Pen(Theme.Border);
        g.DrawLine(p, r.X + Theme.S(10), r.Bottom - 1, r.Right - Theme.S(10), r.Bottom - 1);
    }

    void RefreshLaps()
    {
        _lapCount = _s.Laps.Count;
        _list.BeginUpdate();
        _list.Items.Clear();
        for (int i = 0; i < _s.Laps.Count; i++)
        {
            var l = _s.Laps[i];
            var it = new ListViewItem(new[] { (i + 1).ToString(), l.Name, Fmt.Time(l.LapSeconds, true, false), Fmt.Time(l.TotalSeconds, true, false) }) { Tag = l };
            it.Font = Theme.Bold;
            _list.Items.Add(it);
        }
        if (_list.Items.Count > 0) _list.EnsureVisible(_list.Items.Count - 1);
        _list.EndUpdate();
    }

    void RenameLap()
    {
        if (_list.SelectedItems.Count == 0) return;
        var lap = (Lap)_list.SelectedItems[0].Tag;
        var n = Prompt.Show(this, "Rename lap", "Lap name:", lap.Name);
        if (string.IsNullOrWhiteSpace(n)) return;
        // keep statistics in sync
        var rec = AppState.Data.History.LastOrDefault(h => h.Kind == "Lap" && h.Parent == _s.Name && h.Name == lap.Name && Math.Abs((h.EndUtc - lap.TimeUtc).TotalSeconds) < 1);
        if (rec != null) rec.Name = n.Trim();
        lap.Name = n.Trim();
        AppState.NotifyChanged();
        RefreshLaps();
    }

    void DeleteLap()
    {
        if (_list.SelectedItems.Count == 0) return;
        var lap = (Lap)_list.SelectedItems[0].Tag;
        _s.Laps.Remove(lap);
        AppState.NotifyChanged();
        RefreshLaps();
    }

    void CopyLaps()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{_s.Name}\t{Fmt.Time(_s.Elapsed(DateTime.UtcNow), true)}");
        sb.AppendLine("#\tLap\tTime\tTotal time");
        for (int i = 0; i < _s.Laps.Count; i++) sb.AppendLine($"{i + 1}\t{_s.Laps[i].Name}\t{Fmt.Time(_s.Laps[i].LapSeconds, true)}\t{Fmt.Time(_s.Laps[i].TotalSeconds, true)}");
        try { Clipboard.SetText(sb.ToString()); } catch { }
    }
}

public class LapReportForm : Form
{
    readonly StopwatchItem _s;
    readonly ChartControl _chart = new() { Dock = DockStyle.Fill };
    readonly RadioButton _bar = new() { Text = "Bar", Checked = true, AutoSize = true };
    readonly RadioButton _pie = new() { Text = "Pie", AutoSize = true };
    readonly CheckBox _group = new() { Text = "Grouping (sum laps with the same name)", Checked = true, AutoSize = true };
    readonly CheckBox _avg = new() { Text = "Average", AutoSize = true };

    public LapReportForm(StopwatchItem s)
    {
        _s = s;
        Text = $"{s.Name} - Report";
        Icon = Program.AppIcon;
        Font = Theme.Normal;
        Size = new Size(Theme.S(760), Theme.S(520));
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.White;
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(Theme.S(8)), BackColor = Theme.HeaderBack };
        var save = new Button { Text = "Save image…", AutoSize = true };
        var copy = new Button { Text = "Copy image", AutoSize = true };
        foreach (Control c in new Control[] { _bar, _pie, _group, _avg }) { c.Margin = new Padding(4, 6, 12, 0); bottom.Controls.Add(c); }
        bottom.Controls.Add(copy); bottom.Controls.Add(save);
        foreach (var c in new[] { _bar, _pie }) c.CheckedChanged += (a, b) => Build();
        _group.CheckedChanged += (a, b) => Build();
        _avg.CheckedChanged += (a, b) => Build();
        save.Click += (a, b) => SaveImage(_chart, this, _s.Name + " report");
        copy.Click += (a, b) => { using var bmp = _chart.Snapshot(); Clipboard.SetImage(bmp); };
        Controls.Add(_chart);
        Controls.Add(bottom);
        Build();
    }

    void Build()
    {
        _chart.Type = _pie.Checked ? ChartType.Pie : ChartType.Bar;
        _chart.Title = _s.Name;
        _avg.Enabled = _group.Checked;
        var pts = new List<ChartPoint>();
        if (_group.Checked)
        {
            int i = 0;
            foreach (var g in _s.Laps.GroupBy(l => l.Name))
                pts.Add(new ChartPoint { Label = g.Key, Value = _avg.Checked ? g.Average(l => l.LapSeconds) : g.Sum(l => l.LapSeconds), Color = Theme.ChartColors[i++ % Theme.ChartColors.Length], Count = g.Count() });
            _chart.Subtitle = $"{_s.Laps.Count} laps  •  {(_avg.Checked ? "average" : "total")} time per name";
        }
        else
        {
            var names = _s.Laps.Select(l => l.Name).Distinct().ToList();
            for (int i = 0; i < _s.Laps.Count; i++)
                pts.Add(new ChartPoint { Label = $"{i + 1}. {_s.Laps[i].Name}", Value = _s.Laps[i].LapSeconds, Color = Theme.ChartColors[names.IndexOf(_s.Laps[i].Name) % Theme.ChartColors.Length], Count = 1 });
            _chart.Subtitle = $"{_s.Laps.Count} laps";
        }
        _chart.SetData(pts);
    }

    public static void SaveImage(ChartControl chart, IWin32Window owner, string name)
    {
        using var sfd = new SaveFileDialog { Filter = "PNG image|*.png", FileName = string.Concat(name.Split(Path.GetInvalidFileNameChars())) + ".png" };
        if (sfd.ShowDialog(owner) != DialogResult.OK) return;
        using var bmp = chart.Snapshot();
        bmp.Save(sfd.FileName, System.Drawing.Imaging.ImageFormat.Png);
    }
}

public static class Prompt
{
    public static string Show(IWin32Window owner, string title, string label, string value)
    {
        using var f = new Form { Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false, Font = Theme.Normal, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(10) };
        var t = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill };
        var tb = new TextBox { Text = value, Width = Theme.S(280) };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
        var fl = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
        fl.Controls.Add(cancel); fl.Controls.Add(ok);
        t.Controls.Add(new Label { Text = label, AutoSize = true });
        t.Controls.Add(tb); t.Controls.Add(fl);
        f.Controls.Add(t);
        f.AcceptButton = ok; f.CancelButton = cancel;
        return f.ShowDialog(owner) == DialogResult.OK ? tb.Text : null;
    }
}
